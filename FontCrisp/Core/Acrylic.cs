using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace FontCrisp.Core;

/// <summary>
/// Blurred window backdrop. Windows 11 22H2+ uses the documented DWM system backdrop (Mica); older builds fall
/// back to the composition-attribute acrylic. The window needs a transparent client area
/// (WindowChrome GlassFrameThickness=-1 + Background=Transparent) so the blur shows through.
/// </summary>
internal static class Acrylic
{
    static readonly int Build = Environment.OSVersion.Version.Build;
    public static bool SystemBackdropSupported => Build >= 22621;

    [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref MARGINS margins);

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_SYSTEMBACKDROP_TYPE = 38;
    const int DWMWCP_ROUND = 2, DWMSBT_MAINWINDOW = 2;

    [StructLayout(LayoutKind.Sequential)] struct AccentPolicy { public int AccentState, AccentFlags; public uint GradientColor; public int AnimationId; }
    [StructLayout(LayoutKind.Sequential)] struct WindowCompositionAttributeData { public int Attribute; public nint Data; public int SizeOfData; }
    [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(nint hwnd, ref WindowCompositionAttributeData data);

    /// <summary>Windows "Choose your mode → App mode" is dark.</summary>
    public static bool SystemIsDark
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
    }

    public static bool Apply(Window window, bool dark)
    {
        nint hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0) return false;
        try
        {
            int dm = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dm, sizeof(int));
            if (SystemBackdropSupported)
            {
                int corner = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
                var m = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref m);
                int type = DWMSBT_MAINWINDOW;
                return DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int)) == 0;
            }
            if (Build >= 17134)
            {
                var policy = new AccentPolicy { AccentState = 4 /*ACRYLICBLURBEHIND*/, AccentFlags = 2, GradientColor = dark ? 0x99202020u : 0x99F2F2F2u };
                int size = Marshal.SizeOf(policy);
                nint p = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(policy, p, false);
                    var data = new WindowCompositionAttributeData { Attribute = 19 /*WCA_ACCENT_POLICY*/, Data = p, SizeOfData = size };
                    return SetWindowCompositionAttribute(hwnd, ref data) != 0;
                }
                finally { Marshal.FreeHGlobal(p); }
            }
        }
        catch { }
        return false;
    }
}
