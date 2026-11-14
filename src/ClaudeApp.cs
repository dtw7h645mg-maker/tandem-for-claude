using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace Tandem
{
    /// <summary>One open Claude Desktop window.</summary>
    class RunningClaude
    {
        public int Pid;
        public string Dir;      // its --user-data-dir, null for the normal install
        public int Sessions;    // Claude Code sessions it is running
    }

    /// <summary>
    /// Finds, starts and stops Claude Desktop.
    ///
    /// The whole trick: every extra profile starts the installed Claude.exe once more with
    ///   --user-data-dir=FOLDER            its own login and app settings
    ///   CLAUDE_CONFIG_DIR=FOLDER\.claude  its own Claude Code memory, skills and history
    /// </summary>
    static class ClaudeApp
    {
        /// <summary>Anthropic's publisher id in the Microsoft Store package name.</summary>
        public const string PublisherId = "pzs8sxrjxfjjc";

        static readonly Regex StoreExe = new Regex(@"\\WindowsApps\\Claude_[^\\]+\\app\\claude\.exe$", RegexOptions.IgnoreCase);
        static readonly Regex ClassicExe = new Regex(@"\\AnthropicClaude\\(app-[^\\]+\\)?claude\.exe$", RegexOptions.IgnoreCase);
        static readonly Regex DataDirArg = new Regex("--user-data-dir=(?:\"([^\"]+)\"|(\\S+))");

        // Variables a Claude Code session gives its child processes. They carry that
        // session's account, so they must not reach a Claude window started from inside one.
        static readonly string[] SessionVariables =
        {
            "AI_AGENT", "BAGGAGE", "API_TIMEOUT_MS", "DISABLE_AUTOUPDATER", "DISABLE_MICROCOMPACT",
            "USE_LOCAL_OAUTH", "USE_STAGING_OAUTH", "MCP_CONNECTION_NONBLOCKING", "MCP_SERVER_CONNECTION_BATCH_SIZE"
        };

        static string cachedExe;

        public static string FindExe()
        {
            if (cachedExe != null && File.Exists(cachedExe)) return cachedExe;
            string exe = FindStoreExe() ?? FindClassicExe() ?? FindRunningExe();
            if (exe == null)
                throw new FileNotFoundException("Claude Desktop was not found on this PC. Install it from claude.ai/download, open it once, then try again.");
            cachedExe = exe;
            return exe;
        }

        // The Microsoft Store (MSIX) install. Its folder name changes with every update,
        // so read the current one from the registry.
        static string FindStoreExe()
        {
            string best = null;
            Version bestVersion = null;
            const string path = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
            using (RegistryKey packages = Registry.CurrentUser.OpenSubKey(path))
            {
                if (packages == null) return null;
                foreach (string name in packages.GetSubKeyNames())
                {
                    if (!name.StartsWith("Claude_", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!name.EndsWith("__" + PublisherId, StringComparison.OrdinalIgnoreCase)) continue;
                    using (RegistryKey package = packages.OpenSubKey(name))
                    {
                        string root = package == null ? null : package.GetValue("PackageRootFolder") as string;
                        if (string.IsNullOrEmpty(root)) continue;
                        string exe = Path.Combine(root, @"app\Claude.exe");
                        if (!File.Exists(exe)) continue;
                        Version version;
                        Match m = Regex.Match(name, @"^Claude_([\d.]+)_");
                        if (!m.Success || !Version.TryParse(m.Groups[1].Value, out version)) version = new Version(0, 0);
                        if (best == null || version > bestVersion) { best = exe; bestVersion = version; }
                    }
                }
            }
            return best;
        }

        // The older installer: %LOCALAPPDATA%\AnthropicClaude\app-x.y.z\claude.exe
        static string FindClassicExe()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnthropicClaude");
            if (!Directory.Exists(root)) return null;
            string best = null;
            Version bestVersion = null;
            foreach (string dir in Directory.GetDirectories(root, "app-*"))
            {
                string exe = Path.Combine(dir, "claude.exe");
                if (!File.Exists(exe)) continue;
                Version version;
                if (!Version.TryParse(Path.GetFileName(dir).Substring(4), out version)) version = new Version(0, 0);
                if (best == null || version > bestVersion) { best = exe; bestVersion = version; }
            }
            if (best != null) return best;
            string stub = Path.Combine(root, "claude.exe");
            return File.Exists(stub) ? stub : null;
        }

        static string FindRunningExe()
        {
            foreach (ProcessRow row in QueryProcesses())
                if (IsDesktopExe(row.Path)) return row.Path;
            return null;
        }

        static bool IsDesktopExe(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (cachedExe != null && string.Equals(path, cachedExe, StringComparison.OrdinalIgnoreCase)) return true;
            return StoreExe.IsMatch(path) || ClassicExe.IsMatch(path);
        }

        public static string VersionText()
        {
            try
            {
                string exe = FindExe();
                Match m = Regex.Match(exe, @"\\Claude_([\d.]+)_");
                if (m.Success)
                {
                    string v = m.Groups[1].Value;
                    return v.EndsWith(".0") ? v.Substring(0, v.Length - 2) : v;
                }
                return FileVersionInfo.GetVersionInfo(exe).ProductVersion ?? "";
            }
            catch { return ""; }
        }

        class ProcessRow { public int Pid, Parent; public string Path, Command; }

        static List<ProcessRow> QueryProcesses()
        {
            var rows = new List<ProcessRow>();
            const string query = "SELECT ProcessId, ParentProcessId, ExecutablePath, CommandLine FROM Win32_Process WHERE Name = 'claude.exe'";
            using (var searcher = new ManagementObjectSearcher(query))
            using (ManagementObjectCollection found = searcher.Get())
            {
                foreach (ManagementBaseObject o in found)
                {
                    using (o)
                    {
                        rows.Add(new ProcessRow
                        {
                            Pid = Convert.ToInt32(o["ProcessId"]),
                            Parent = Convert.ToInt32(o["ParentProcessId"]),
                            Path = o["ExecutablePath"] as string,
                            Command = o["CommandLine"] as string
                        });
                    }
                }
            }
            return rows;
        }

        /// <summary>Every open Claude Desktop window. Slow (a WMI query): call it off the UI thread.</summary>
        public static List<RunningClaude> GetRunning()
        {
            var result = new List<RunningClaude>();
            List<ProcessRow> rows = QueryProcesses();
            foreach (ProcessRow row in rows)
            {
                // Electron's helper processes carry --type=...; the app itself does not.
                if (row.Command == null || row.Command.Contains("--type=") || !IsDesktopExe(row.Path)) continue;
                var running = new RunningClaude { Pid = row.Pid };
                Match m = DataDirArg.Match(row.Command);
                if (m.Success) running.Dir = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).TrimEnd('\\');
                foreach (ProcessRow child in rows)
                    if (child.Parent == row.Pid && child.Command != null && child.Command.Contains("--output-format")) running.Sessions++;
                result.Add(running);
            }
            return result;
        }

        public static RunningClaude Find(Profile p, List<RunningClaude> running)
        {
            foreach (RunningClaude r in running)
            {
                if (p.IsDefault) { if (r.Dir == null) return r; }
                else if (r.Dir != null && string.Equals(r.Dir, p.Dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return r;
            }
            return null;
        }

        static void CleanEnvironment(ProcessStartInfo psi)
        {
            var env = psi.EnvironmentVariables;
            // Windows tags a newly seen program with a compatibility marker. Claude should
            // start as it does from the Start menu, without inheriting this app's marker.
            if (env.ContainsKey("__COMPAT_LAYER")) env.Remove("__COMPAT_LAYER");
            if (!env.ContainsKey("CLAUDECODE") && !env.ContainsKey("CLAUDE_CODE_ENTRYPOINT")) return;
            var remove = new List<string>();
            foreach (string key in env.Keys)
            {
                string k = key.ToUpperInvariant();
                if (k.StartsWith("CLAUDE") || k.StartsWith("ANTHROPIC") || k.StartsWith("OTEL_") || k.StartsWith("SENTRY")
                    || Array.IndexOf(SessionVariables, k) >= 0)
                    remove.Add(key);
            }
            foreach (string key in remove) env.Remove(key);
        }

        public static void Start(Profile p)
        {
            var psi = new ProcessStartInfo(FindExe());
            psi.UseShellExecute = false;
            psi.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            CleanEnvironment(psi);
            if (!p.IsDefault)
            {
                string dir = p.Dir.TrimEnd('\\');
                Directory.CreateDirectory(dir);
                psi.Arguments = "--user-data-dir=\"" + dir + "\"";
                psi.EnvironmentVariables["CLAUDE_CONFIG_DIR"] = Path.Combine(dir, ".claude");
            }
            using (Process.Start(psi)) { }
        }

        public static void Activate(Profile p, RunningClaude run)
        {
            IntPtr h = Win32.MainWindowOf(run.Pid);
            if (h != IntPtr.Zero)
            {
                Win32.Restore(h);
                if (Win32.SetForegroundWindow(h)) return;
            }
            // The window is hidden in the tray. Starting the same profile again makes the
            // running Claude show its window; the second start then exits by itself.
            Start(p);
        }

        /// <summary>Starts the profile, or brings it to the front when it is already open. True when it was started.</summary>
        public static bool StartOrActivate(Profile p)
        {
            RunningClaude run = Find(p, GetRunning());
            if (run != null) { Activate(p, run); return false; }
            Start(p);
            return true;
        }

        /// <summary>Closes the Claude window and everything it started. False when it was not running.</summary>
        public static bool Stop(Profile p)
        {
            RunningClaude run = Find(p, GetRunning());
            if (run == null) return false;
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "taskkill.exe"), "/PID " + run.Pid + " /T /F");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            using (Process kill = Process.Start(psi)) kill.WaitForExit();
            return true;
        }

        /// <summary>Paints (or clears) the profile's border colour on its windows. True when a window was found.</summary>
        public static bool PaintAccent(Profile p, RunningClaude run)
        {
            int value = -1;
            if (!string.IsNullOrEmpty(p.Color))
            {
                Color c = ColorTranslator.FromHtml(p.Color);
                value = c.R | (c.G << 8) | (c.B << 16);
            }
            bool found = false;
            foreach (IntPtr h in Win32.WindowsOf(run.Pid))
            {
                Win32.SetBorder(h, value);
                found = true;
            }
            return found;
        }

        /// <summary>
        /// After a start the window takes a few seconds to appear, and Claude swaps its
        /// loading window for the real one, so paint more than once.
        /// </summary>
        public static void WaitAndPaintAccent(Profile p)
        {
            int painted = 0;
            for (int i = 0; i < 60 && painted < 6; i++)
            {
                Thread.Sleep(500);
                RunningClaude run = Find(p, GetRunning());
                if (run != null && PaintAccent(p, run)) { painted++; Thread.Sleep(1000); }
            }
        }

        /// <summary>Tiles the open Claude windows in equal columns. Returns how many were placed.</summary>
        public static int SideBySide(List<Profile> profiles, Rectangle area)
        {
            List<RunningClaude> running = GetRunning();
            var windows = new List<IntPtr>();
            foreach (Profile p in profiles)
            {
                RunningClaude run = Find(p, running);
                if (run == null) continue;
                IntPtr h = Win32.MainWindowOf(run.Pid);
                if (h != IntPtr.Zero) windows.Add(h);
            }
            if (windows.Count < 2)
                throw new InvalidOperationException("Open at least two Claude windows first. A window that is hidden in the tray does not count: press Bring to front on it.");
            int width = area.Width / windows.Count;
            for (int i = 0; i < windows.Count; i++)
                Win32.Place(windows[i], area.Left + i * width, area.Top, width, area.Height);
            return windows.Count;
        }

        /// <summary>A PowerShell window where the "claude" command uses this profile's own settings and login.</summary>
        public static void OpenTerminal(Profile p)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"));
            psi.Arguments = "/c start \"Claude Code - " + p.Name + "\" /D \"" + home + "\" powershell.exe -NoLogo -NoExit";
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            CleanEnvironment(psi);
            if (!p.IsDefault) psi.EnvironmentVariables["CLAUDE_CONFIG_DIR"] = p.ConfigDir;
            using (Process.Start(psi)) { }
        }
    }
}
