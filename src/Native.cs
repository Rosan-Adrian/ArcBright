using System;
using System.Runtime.InteropServices;

namespace ArcBright
{
    // Windows API calls used by ArcBright. Nothing here touches another process:
    // it reads which window is in front and changes the display's gamma ramp.
    internal static class Native
    {
        public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_NOREPEAT = 0x4000;
        public const int WM_HOTKEY = 0x0312;

        public delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc proc, uint processId, uint threadId, uint flags);

        [DllImport("user32.dll")]
        public static extern bool UnhookWinEvent(IntPtr hook);

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateDC(string driver, string device, string output, IntPtr initData);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern bool GetDeviceGammaRamp(IntPtr hdc, [Out] ushort[] ramp);

        [DllImport("gdi32.dll")]
        public static extern bool SetDeviceGammaRamp(IntPtr hdc, [In] ushort[] ramp);
    }
}
