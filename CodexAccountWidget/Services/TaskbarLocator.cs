using System.Runtime.InteropServices;

namespace CodexAccountWidget.Services;

internal static class TaskbarLocator
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rect);

    public static bool TryGetPrimaryTaskbar(out NativeRect bounds)
    {
        return TryGetPrimaryTaskbar(out _, out bounds);
    }

    public static bool TryGetPrimaryTaskbar(out IntPtr handle, out NativeRect bounds)
    {
        handle = FindWindow("Shell_TrayWnd", null);
        if (handle != IntPtr.Zero && GetWindowRect(handle, out bounds)) return true;
        handle = IntPtr.Zero;
        bounds = default;
        return false;
    }
}
