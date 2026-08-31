using System.Runtime.InteropServices;

namespace CodexAccountWidget.Services;

internal static class TaskbarContrastDetector
{
    private const uint InvalidPixel = 0xFFFFFFFF;
    private const double DarkTextThreshold = 0.2;

    public static bool TryShouldUseDarkText(
        TaskbarLocator.NativeRect taskbar,
        int widgetRight,
        out bool useDarkText)
    {
        useDarkText = false;
        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return false;

        try
        {
            var startX = Math.Clamp(widgetRight + 8, taskbar.Left, taskbar.Right - 1);
            var taskbarWidth = taskbar.Right - taskbar.Left;
            var taskbarHeight = taskbar.Bottom - taskbar.Top;
            var endX = Math.Min(taskbar.Right - 1, startX + Math.Min(480, taskbarWidth / 3));
            if (endX <= startX) return false;

            var sampleRows = new[] { 0.18, 0.32, 0.68, 0.82 };
            var luminances = new List<double>(80);
            for (var column = 0; column < 20; column++)
            {
                var x = startX + (endX - startX) * column / 19;
                foreach (var row in sampleRows)
                {
                    var y = taskbar.Top + (int)Math.Round((taskbarHeight - 1) * row);
                    var pixel = GetPixel(screenDc, x, y);
                    if (pixel != InvalidPixel) luminances.Add(GetRelativeLuminance(pixel));
                }
            }

            if (luminances.Count == 0) return false;
            luminances.Sort();
            var middle = luminances.Count / 2;
            var median = luminances.Count % 2 == 0
                ? (luminances[middle - 1] + luminances[middle]) / 2
                : luminances[middle];
            useDarkText = median >= DarkTextThreshold;
            return true;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static double GetRelativeLuminance(uint colorReference)
    {
        var red = ToLinear((byte)(colorReference & 0xFF));
        var green = ToLinear((byte)((colorReference >> 8) & 0xFF));
        var blue = ToLinear((byte)((colorReference >> 16) & 0xFF));
        return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
    }

    private static double ToLinear(byte channel)
    {
        var value = channel / 255d;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr windowHandle);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr deviceContext, int x, int y);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr windowHandle, IntPtr deviceContext);
}
