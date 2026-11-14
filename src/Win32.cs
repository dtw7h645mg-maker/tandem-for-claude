using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Tandem
{
    static class Win32
    {
        delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);
        [DllImport("kernel32.dll")] public static extern bool AttachConsole(int processId);
        [DllImport("kernel32.dll")] public static extern IntPtr GetStdHandle(int handle);

        const int SW_RESTORE = 9;
        const int DWMWA_BORDER_COLOR = 34;      // Windows 11
        const uint SWP_SHOWWINDOW = 0x0040;

        /// <summary>Visible, titled top-level windows of one process.</summary>
        public static List<IntPtr> WindowsOf(int pid)
        {
            var found = new List<IntPtr>();
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                uint owner;
                GetWindowThreadProcessId(h, out owner);
                if (owner == (uint)pid && IsWindowVisible(h) && GetWindowTextLength(h) > 0) found.Add(h);
                return true;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>The biggest of them is the app window.</summary>
        public static IntPtr MainWindowOf(int pid)
        {
            IntPtr best = IntPtr.Zero;
            long bestArea = -1;
            foreach (IntPtr h in WindowsOf(pid))
            {
                RECT r;
                if (!GetWindowRect(h, out r)) continue;
                long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                if (area > bestArea) { bestArea = area; best = h; }
            }
            return best;
        }

        /// <summary>Sets the window border colour (0x00BBGGRR). -1 gives the default back. Windows 11 only.</summary>
        public static bool SetBorder(IntPtr h, int colorRef)
        {
            return DwmSetWindowAttribute(h, DWMWA_BORDER_COLOR, ref colorRef, 4) == 0;
        }

        public static void Place(IntPtr h, int x, int y, int width, int height)
        {
            if (IsIconic(h) || IsZoomed(h)) ShowWindow(h, SW_RESTORE);
            SetWindowPos(h, IntPtr.Zero, x, y, width, height, SWP_SHOWWINDOW);
        }

        public static void Restore(IntPtr h)
        {
            if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
        }
    }
}
