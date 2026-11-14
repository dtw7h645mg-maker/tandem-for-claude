using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Tandem
{
    /// <summary>One Claude account: a name, a data folder and an optional window colour.</summary>
    class Profile
    {
        public string Name;
        public string Dir;      // null = the normal Claude install
        public string Color;    // "#RRGGBB" or null

        public bool IsDefault { get { return string.IsNullOrEmpty(Dir); } }

        /// <summary>Claude Code's own folder for this profile (memory, skills, history).</summary>
        public string ConfigDir { get { return IsDefault ? null : Path.Combine(Dir, ".claude"); } }
    }

    /// <summary>
    /// The profile list and settings, kept in %USERPROFILE%\.tandem\config.json.
    ///
    /// Why not AppData: Claude from the Microsoft Store runs in a package sandbox that
    /// redirects new AppData folders into its own storage. Anything started from inside
    /// Claude (a Claude Code session, for one) inherits that, so the same AppData path
    /// would be two different folders depending on who started this app. Windows also
    /// deletes that storage when Claude is uninstalled. The user profile is never redirected.
    /// </summary>
    class Store
    {
        public readonly List<Profile> Profiles = new List<Profile>();
        public bool KeepInTray;
        public string Notice;   // a one-time message for the window, set on first run

        static readonly Regex NameRule = new Regex(@"^[A-Za-z0-9][A-Za-z0-9 _-]{0,30}$");
        static readonly Regex ColorRule = new Regex(@"^#[0-9A-Fa-f]{6}$");

        static string Roaming { get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } }
        public static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "." + AppInfo.Name.ToLowerInvariant()); } }
        public static string ProfilesRoot { get { return Path.Combine(Folder, "profiles"); } }
        static string FilePath { get { return Path.Combine(Folder, "config.json"); } }

        public static Store Load()
        {
            var store = new Store();
            if (!File.Exists(FilePath))
            {
                store.FirstRun();
                return store;
            }
            var root = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(FilePath, Encoding.UTF8)) as Dictionary<string, object>;
            if (root == null) throw new InvalidDataException("The settings file is damaged: " + FilePath);

            object value;
            if (root.TryGetValue("keepInTray", out value) && value is bool) store.KeepInTray = (bool)value;
            if (root.TryGetValue("profiles", out value) && value is IEnumerable && !(value is string))
            {
                foreach (object item in (IEnumerable)value)
                {
                    var d = item as Dictionary<string, object>;
                    if (d == null) continue;
                    var p = new Profile();
                    p.Name = Text(d, "name");
                    p.Dir = Text(d, "dir");
                    p.Color = Text(d, "color");
                    if (p.Color != null && !ColorRule.IsMatch(p.Color)) p.Color = null;
                    if (!string.IsNullOrEmpty(p.Name)) store.Profiles.Add(p);
                }
            }
            if (store.Profiles.Count == 0) store.Profiles.Add(new Profile { Name = "Claude" });
            return store;
        }

        static string Text(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v)) return null;
            string s = v as string;
            return string.IsNullOrEmpty(s) ? null : s;
        }

        /// <summary>Starts the list with the normal Claude, plus any profile folders already on this PC.</summary>
        void FirstRun()
        {
            Profiles.Add(new Profile { Name = "Claude" });
            var added = new List<string>();
            foreach (string dir in FindExistingFolders())
            {
                string name = Path.GetFileName(dir);
                if (!NameRule.IsMatch(name) || Find(name) != null) continue;
                Profiles.Add(new Profile { Name = name, Dir = dir });
                added.Add(name);
            }
            if (added.Count > 0)
                Notice = "Found existing profile folder" + (added.Count > 1 ? "s" : "") + " and added: " + string.Join(", ", added.ToArray()) + ".";
            Save();
        }

        /// <summary>
        /// Profile folders left by this app or by similar tools. Claude from the Microsoft
        /// Store keeps part of each folder inside its own package storage, so look there too.
        /// </summary>
        public static List<string> FindExistingFolders()
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(ProfilesRoot))
            {
                foreach (string sub in Directory.GetDirectories(ProfilesRoot))
                    if (seen.Add(sub)) result.Add(sub);
            }
            // Folders made by similar tools live in AppData.
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string packaged = Path.Combine(local, @"Packages\Claude_" + ClaudeApp.PublisherId + @"\LocalCache\Roaming");
            string[] roots = { "ClaudeProfiles", "MultiDeskClaude-Profiles" };
            foreach (string root in roots)
            {
                foreach (string baseDir in new[] { Roaming, packaged })
                {
                    string dir = Path.Combine(baseDir, root);
                    if (!Directory.Exists(dir)) continue;
                    try
                    {
                        foreach (string sub in Directory.GetDirectories(dir))
                        {
                            string real = Path.Combine(Roaming, root, Path.GetFileName(sub));
                            if (seen.Add(real)) result.Add(real);
                        }
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (IOException) { }
                }
            }
            return result;
        }

        public Profile Find(string name)
        {
            foreach (Profile p in Profiles)
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        void CheckName(string name, Profile except)
        {
            if (!NameRule.IsMatch(name))
                throw new ArgumentException("Use letters, numbers, spaces, - or _ for the name (31 characters at most).");
            Profile clash = Find(name);
            if (clash != null && clash != except)
                throw new ArgumentException("A profile named '" + name + "' already exists.");
        }

        /// <summary>Adds a profile. A folder with the same name from before keeps its login, so it is reused.</summary>
        public Profile Add(string name)
        {
            name = (name ?? "").Trim();
            CheckName(name, null);
            string dir = Path.Combine(ProfilesRoot, name);
            if (!Directory.Exists(dir))
            {
                foreach (string existing in FindExistingFolders())
                {
                    if (!string.Equals(Path.GetFileName(existing), name, StringComparison.OrdinalIgnoreCase)) continue;
                    bool taken = false;
                    foreach (Profile other in Profiles)
                        if (!other.IsDefault && string.Equals(other.Dir.TrimEnd('\\'), existing, StringComparison.OrdinalIgnoreCase)) taken = true;
                    if (!taken) { dir = existing; break; }
                }
            }
            var p = new Profile { Name = name, Dir = dir };
            Profiles.Add(p);
            Save();
            return p;
        }

        public void Rename(Profile p, string newName)
        {
            newName = (newName ?? "").Trim();
            CheckName(newName, p);
            p.Name = newName;
            Save();
        }

        public void Remove(Profile p)
        {
            Profiles.Remove(p);
            Save();
        }

        public void Save()
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"keepInTray\": ").Append(KeepInTray ? "true" : "false").Append(",\n");
            sb.Append("  \"profiles\": [\n");
            for (int i = 0; i < Profiles.Count; i++)
            {
                Profile p = Profiles[i];
                sb.Append("    { \"name\": ").Append(Quote(p.Name))
                  .Append(", \"dir\": ").Append(Quote(p.Dir))
                  .Append(", \"color\": ").Append(Quote(p.Color)).Append(" }")
                  .Append(i < Profiles.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");

            Directory.CreateDirectory(Folder);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(temp, FilePath);
        }

        static string Quote(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
