using System.Runtime.InteropServices;
using System.Text;

namespace FontCrisp.Core;

/// <summary>Win32 declarations: font smoothing SPI, GDI text rendering, DPI awareness queries.</summary>
internal static class Native
{
    // ---- SystemParametersInfo: font smoothing (GDI ClearType) ----
    public const uint SPI_GETFONTSMOOTHING = 0x004A, SPI_SETFONTSMOOTHING = 0x004B;
    public const uint SPI_GETFONTSMOOTHINGTYPE = 0x200A, SPI_SETFONTSMOOTHINGTYPE = 0x200B;
    public const uint SPI_GETFONTSMOOTHINGCONTRAST = 0x200C, SPI_SETFONTSMOOTHINGCONTRAST = 0x200D;
    public const uint SPI_GETFONTSMOOTHINGORIENTATION = 0x2012, SPI_SETFONTSMOOTHINGORIENTATION = 0x2013;
    const uint SPIF_UPDATEINIFILE = 1, SPIF_SENDCHANGE = 2;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SystemParametersInfoW(uint action, uint param, ref uint value, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SystemParametersInfoW(uint action, uint param, nint value, uint flags);

    public static uint GetSpi(uint action) { uint v = 0; SystemParametersInfoW(action, 0, ref v, 0); return v; }
    /// <summary>For the SET font-smoothing actions the value travels in pvParam itself, not behind a pointer.</summary>
    public static bool SetSpiValue(uint action, uint value) => SystemParametersInfoW(action, 0, (nint)value, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
    public static bool SetSpiFlag(uint action, bool on) => SystemParametersInfoW(action, on ? 1u : 0u, 0, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern nint SendMessageTimeoutW(nint hWnd, uint msg, nint wParam, string lParam, uint flags, uint timeout, out nint result);
    public static void BroadcastSettingChange(string section) =>
        SendMessageTimeoutW(0xFFFF, 0x001A /*WM_SETTINGCHANGE*/, 0, section, 0x0002 /*SMTO_ABORTIFHUNG*/, 1000, out _);

    // ---- windows / DPI ----
    public delegate bool EnumWindowsProc(nint hwnd, nint lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, nint lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll")] public static extern int GetWindowTextLengthW(nint h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(nint h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint h, out uint pid);
    [DllImport("user32.dll")] public static extern nint GetWindowDpiAwarenessContext(nint h);
    [DllImport("user32.dll")] public static extern int GetAwarenessFromDpiAwarenessContext(nint ctx);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(nint h);
    [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] public static extern nint GetAncestor(nint h, uint flags);
    [DllImport("user32.dll")] public static extern nint MonitorFromWindow(nint h, uint flags);
    [DllImport("user32.dll")] public static extern nint GetDC(nint h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(nint h, nint dc);
    [DllImport("gdi32.dll")] public static extern int GetDeviceCaps(nint dc, int index);

    [DllImport("kernel32.dll", SetLastError = true)] static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool QueryFullProcessImageNameW(nint h, uint flags, StringBuilder exe, ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(nint h);

    public static string? GetProcessPath(uint pid)
    {
        nint h = OpenProcess(0x1000 /*PROCESS_QUERY_LIMITED_INFORMATION*/, false, pid);
        if (h == 0) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int n = sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref n) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    // ---- display devices ----
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool EnumDisplayDevicesW(string? device, uint index, ref DISPLAY_DEVICE dd, uint flags);

    /// <summary>GDI names of displays attached to the desktop, e.g. "DISPLAY1" (the Avalon.Graphics sub-key name).</summary>
    public static List<string> ActiveDisplayNames()
    {
        var list = new List<string>();
        for (uint i = 0; ; i++)
        {
            var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevicesW(null, i, ref dd, 0)) break;
            if ((dd.StateFlags & 1 /*DISPLAY_DEVICE_ATTACHED_TO_DESKTOP*/) != 0)
                list.Add(dd.DeviceName.TrimStart('\\', '.'));
        }
        return list;
    }

    // ---- GDI text rendering into a DIB ----
    [DllImport("gdi32.dll")] public static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(nint hdc);
    [DllImport("gdi32.dll")] public static extern nint SelectObject(nint hdc, nint obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] public static extern int SetBkMode(nint hdc, int mode);
    [DllImport("gdi32.dll")] public static extern uint SetTextColor(nint hdc, uint color);
    [DllImport("gdi32.dll")] public static extern bool GdiFlush();
    [DllImport("gdi32.dll")]
    public static extern nint CreateDIBSection(nint hdc, ref BITMAPINFO bmi, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern nint CreateFontIndirectW(ref LOGFONT lf);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int DrawTextW(nint hdc, string text, int len, ref RECT rc, uint fmt);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
    }
    [StructLayout(LayoutKind.Sequential)] public struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public uint bmiColors; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct LOGFONT
    {
        public int lfHeight, lfWidth, lfEscapement, lfOrientation, lfWeight;
        public byte lfItalic, lfUnderline, lfStrikeOut, lfCharSet, lfOutPrecision, lfClipPrecision, lfQuality, lfPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string lfFaceName;
    }
}
