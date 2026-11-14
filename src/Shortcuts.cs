using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Tandem
{
    /// <summary>Windows shortcuts (.lnk) that open this app, or one profile through it.</summary>
    static class Shortcuts
    {
        public static string ForProfile(Profile p, Environment.SpecialFolder folder)
        {
            return Path.Combine(Environment.GetFolderPath(folder), "Claude - " + p.Name + ".lnk");
        }

        public static string ForApp(Environment.SpecialFolder folder)
        {
            return Path.Combine(Environment.GetFolderPath(folder), AppInfo.Title + ".lnk");
        }

        public static void CreateForProfile(Profile p, Environment.SpecialFolder folder)
        {
            Create(ForProfile(p, folder), "--launch \"" + p.Name + "\"", "Open Claude with the '" + p.Name + "' account");
        }

        public static void CreateForApp(Environment.SpecialFolder folder)
        {
            Create(ForApp(folder), "", AppInfo.Tagline);
        }

        static void Create(string lnkPath, string arguments, string description)
        {
            string exe = Application.ExecutablePath;
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            try
            {
                dynamic link = ((dynamic)shell).CreateShortcut(lnkPath);
                link.TargetPath = exe;
                link.Arguments = arguments;
                link.WorkingDirectory = Path.GetDirectoryName(exe);
                link.Description = description;
                link.IconLocation = exe + ",0";
                link.Save();
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }

        /// <summary>A profile's shortcuts carry its name, so they follow a rename and go with a removal.</summary>
        public static void Rename(Profile p, string oldName)
        {
            var old = new Profile { Name = oldName, Dir = p.Dir };
            foreach (Environment.SpecialFolder folder in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.Startup })
            {
                string path = ForProfile(old, folder);
                if (!File.Exists(path)) continue;
                File.Delete(path);
                CreateForProfile(p, folder);
            }
        }

        public static void Delete(Profile p)
        {
            foreach (Environment.SpecialFolder folder in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.Startup })
            {
                string path = ForProfile(p, folder);
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
