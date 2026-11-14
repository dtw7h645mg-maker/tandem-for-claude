using System.Reflection;

[assembly: AssemblyTitle("Tandem for Claude")]
[assembly: AssemblyProduct("Tandem for Claude")]
[assembly: AssemblyDescription("Run several Claude Desktop accounts side by side. Unofficial tool.")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace Tandem
{
    /// <summary>Everything that carries the app's name lives here, so a rename is one edit.</summary>
    static class AppInfo
    {
        public const string Name = "Tandem";                // exe name and settings folder
        public const string Title = "Tandem for Claude";    // window title and shortcuts
        public const string Version = "1.0.0";
        public const string Tagline = "Run several Claude Desktop accounts side by side.";
        public const string Disclaimer =
            "Unofficial tool. Not made by, affiliated with or endorsed by Anthropic.\n" +
            "\"Claude\" is a trademark of Anthropic.";
        public static readonly string Website = "https://github.com/dtw7h645mg-maker/tandem-for-claude";
    }
}
