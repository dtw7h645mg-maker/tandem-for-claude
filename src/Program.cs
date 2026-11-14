using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;

namespace Tandem
{
    static class Program
    {
        const string Help =
            "Tandem for Claude - run several Claude Desktop accounts side by side.\n\n" +
            "  Tandem.exe                  open the window\n" +
            "  Tandem.exe --launch NAME    start one profile, or bring it to the front\n" +
            "  Tandem.exe --stop NAME      stop one profile and everything it started\n" +
            "  Tandem.exe --side-by-side   put the open Claude windows next to each other\n" +
            "  Tandem.exe --list           print the profiles and their state\n";

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool quiet = args.Length > 0 && (args[0] == "--list" || args[0] == "--help");
            try
            {
                if (args.Length == 0) return RunWindow(null);
                switch (args[0])
                {
                    case "--launch": return Launch(Argument(args));
                    case "--stop": ClaudeApp.Stop(Named(Argument(args))); return 0;
                    case "--side-by-side": ClaudeApp.SideBySide(Store.Load().Profiles, Screen.PrimaryScreen.WorkingArea); return 0;
                    case "--list": return List();
                    case "--snapshot": return RunWindow(Argument(args));
                    case "--help": Print(Help); return 0;
                    default: throw new ArgumentException("Unknown option " + args[0] + "\n\n" + Help);
                }
            }
            catch (Exception ex)
            {
                if (quiet) Print("Error: " + ex.Message);
                else MessageBox.Show(ex.Message, AppInfo.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
        }

        static string Argument(string[] args)
        {
            if (args.Length < 2 || args[1].Trim() == "") throw new ArgumentException(args[0] + " needs a value.\n\n" + Help);
            return args[1];
        }

        static Profile Named(string name)
        {
            Profile p = Store.Load().Find(name);
            if (p == null) throw new ArgumentException("There is no profile named '" + name + "'.");
            return p;
        }

        static int RunWindow(string snapshotPath)
        {
            Ui.Init();
            if (snapshotPath != null)
            {
                Application.Run(new MainForm(Store.Load(), snapshotPath));
                return 0;
            }
            bool isNew;
            using (var mutex = new Mutex(true, @"Local\TandemForClaude.Window", out isNew))
            {
                if (!isNew)
                {
                    // Already open: ask that window to come forward.
                    try { using (EventWaitHandle signal = EventWaitHandle.OpenExisting(MainForm.ShowEventName)) signal.Set(); }
                    catch { }
                    return 0;
                }
                Application.Run(new MainForm(Store.Load(), null));
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        static int Launch(string name)
        {
            Profile p = Named(name);
            ClaudeApp.StartOrActivate(p);
            if (!string.IsNullOrEmpty(p.Color)) ClaudeApp.WaitAndPaintAccent(p);
            return 0;
        }

        static int List()
        {
            List<RunningClaude> running = ClaudeApp.GetRunning();
            foreach (Profile p in Store.Load().Profiles)
            {
                RunningClaude r = ClaudeApp.Find(p, running);
                string state = r == null ? "not running" : "running (pid " + r.Pid + ", Code sessions: " + r.Sessions + ")";
                Print(string.Format("{0,-14} {1,-40} {2}", p.Name, state, p.IsDefault ? "(normal Claude install)" : p.Dir));
            }
            return 0;
        }

        // A windowed program has no console of its own. Output still reaches a pipe or
        // file it was started with; otherwise borrow the console it was started from.
        static void Print(string text)
        {
            try
            {
                IntPtr handle = Win32.GetStdHandle(-11);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1)) Win32.AttachConsole(-1);
                Console.WriteLine(text);
            }
            catch { }
        }
    }
}
