using System.Runtime.InteropServices;
using Robust.Client.Graphics;

namespace Content.Client._ERRORGATE.Windowing;

/// <summary>
///     Asks Windows for the dark title bar on the game window. The engine only does this on Windows 11; Windows 10 (2004 and later)
///     honors the same window attribute but nothing sets it. Does nothing elsewhere, and fails quietly.
/// </summary>
public static class DarkTitleBar
{
    // DWMWA_USE_IMMERSIVE_DARK_MODE: 20 from Windows 10 2004, 19 before that
    private const int UseImmersiveDarkMode = 20;
    private const int UseImmersiveDarkModeOld = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    public static void Apply(IClyde clyde)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            if ((clyde.MainWindow as IClydeWindowInternal)?.WindowsHWnd is not { } hwnd)
                return;

            var on = 1;
            if (DwmSetWindowAttribute(hwnd, UseImmersiveDarkMode, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, UseImmersiveDarkModeOld, ref on, sizeof(int));
        }
        catch (Exception)
        {
            // Not worth failing the start over a title bar
        }
    }
}
