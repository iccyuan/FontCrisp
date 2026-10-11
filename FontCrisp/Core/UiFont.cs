using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FontCrisp.Core;

/// <summary>The shell UI font: what Explorer's navigation pane / file list, dialogs, menus and tooltips draw with.</summary>
public sealed record UiFontSettings(string Face, int SizePt, int Weight)
{
    public override string ToString() => $"{Face} {SizePt}pt {WeightName(Weight)}";
    public static string WeightName(int w) => w switch { < 450 => "常规", < 550 => "中等", < 650 => "半粗", _ => "粗体" };
}

/// <summary>
/// Reads / writes the system UI fonts (SPI_SETNONCLIENTMETRICS + SPI_SETICONTITLELOGFONT) and keeps a one-time backup.
/// Explorer (nav pane, list view), Win32 dialogs, menus and most classic apps re-read these on WM_SETTINGCHANGE;
/// WinUI surfaces (Explorer's command bar, Settings) keep Segoe UI Variable and ignore them.
/// </summary>
public static class UiFont
{
    const uint SPI_GETICONTITLELOGFONT = 0x1F, SPI_SETICONTITLELOGFONT = 0x22;
    const uint SPI_GETNONCLIENTMETRICS = 0x29, SPI_SETNONCLIENTMETRICS = 0x2A;
    const uint SPIF_UPDATEINIFILE = 1, SPIF_SENDCHANGE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NONCLIENTMETRICS
    {
        public int cbSize, iBorderWidth, iScrollWidth, iScrollHeight, iCaptionWidth, iCaptionHeight;
        public Native.LOGFONT lfCaptionFont;
        public int iSmCaptionWidth, iSmCaptionHeight;
        public Native.LOGFONT lfSmCaptionFont;
        public int iMenuWidth, iMenuHeight;
        public Native.LOGFONT lfMenuFont, lfStatusFont, lfMessageFont;
        public int iPaddedBorderWidth;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SystemParametersInfoW(uint action, uint param, ref NONCLIENTMETRICS ncm, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SystemParametersInfoW(uint action, uint param, ref Native.LOGFONT lf, uint flags);

    public static string BackupPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FontCrisp", "uifont.json");
    public static bool HasBackup => File.Exists(BackupPath);

    /// <summary>The recommended replacement, if installed: an unhinted medium-weight CJK sans renders 2px strokes like PopBar's Ideal mode.</summary>
    public static readonly string[] PreferredFaces = ["HarmonyOS Sans SC Medium", "MiSans Medium", "Source Han Sans CN Medium", "思源黑体 CN Medium", "Noto Sans CJK SC Medium"];

    // this process is per-monitor DPI aware, so the plain SPI calls speak in system-DPI pixels
    static double SysDpi => Native.GetDpiForSystem();
    static int PtToPx(int pt) => (int)Math.Round(pt * SysDpi / 72.0);
    static int PxToPt(int height) => (int)Math.Round(Math.Abs(height) * 72.0 / SysDpi);

    static NONCLIENTMETRICS GetNcm()
    {
        var n = new NONCLIENTMETRICS { cbSize = Marshal.SizeOf<NONCLIENTMETRICS>() };
        if (!SystemParametersInfoW(SPI_GETNONCLIENTMETRICS, (uint)n.cbSize, ref n, 0)) throw new InvalidOperationException("读取系统字体失败：" + Marshal.GetLastWin32Error());
        return n;
    }
    static Native.LOGFONT GetIcon()
    {
        var lf = new Native.LOGFONT();
        if (!SystemParametersInfoW(SPI_GETICONTITLELOGFONT, (uint)Marshal.SizeOf<Native.LOGFONT>(), ref lf, 0)) throw new InvalidOperationException("读取图标字体失败：" + Marshal.GetLastWin32Error());
        return lf;
    }
    static void Set(NONCLIENTMETRICS n, Native.LOGFONT icon)
    {
        if (!SystemParametersInfoW(SPI_SETNONCLIENTMETRICS, (uint)n.cbSize, ref n, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE)) throw new InvalidOperationException("写入系统字体失败：" + Marshal.GetLastWin32Error());
        if (!SystemParametersInfoW(SPI_SETICONTITLELOGFONT, (uint)Marshal.SizeOf<Native.LOGFONT>(), ref icon, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE)) throw new InvalidOperationException("写入图标字体失败：" + Marshal.GetLastWin32Error());
    }

    /// <summary>What Explorer's panes use right now (the icon-title font; menus/dialogs normally share it).</summary>
    public static UiFontSettings Read()
    {
        var lf = GetIcon();
        return new(lf.lfFaceName, PxToPt(lf.lfHeight), lf.lfWeight == 0 ? 400 : lf.lfWeight);
    }

    public static void Apply(UiFontSettings s)
    {
        EnsureBackup();
        var n = GetNcm();
        var icon = GetIcon();
        int h = -PtToPx(s.SizePt);
        void Put(ref Native.LOGFONT lf) { lf.lfFaceName = s.Face; lf.lfHeight = h; lf.lfWeight = s.Weight; lf.lfWidth = 0; }
        Put(ref n.lfCaptionFont); Put(ref n.lfSmCaptionFont); Put(ref n.lfMenuFont); Put(ref n.lfStatusFont); Put(ref n.lfMessageFont);
        Put(ref icon);
        Set(n, icon);
    }

    sealed class BackupDto
    {
        public Dictionary<string, FontDto> Fonts { get; set; } = new();
        public int[] Metrics { get; set; } = [];
        public DateTime TakenAt { get; set; }
    }
    sealed class FontDto { public string Face { get; set; } = ""; public int Height { get; set; } public int Weight { get; set; } public int Width { get; set; } public byte Italic { get; set; } public byte CharSet { get; set; } public byte Quality { get; set; } public byte PitchAndFamily { get; set; } }

    static FontDto ToDto(Native.LOGFONT lf) => new() { Face = lf.lfFaceName, Height = lf.lfHeight, Weight = lf.lfWeight, Width = lf.lfWidth, Italic = lf.lfItalic, CharSet = lf.lfCharSet, Quality = lf.lfQuality, PitchAndFamily = lf.lfPitchAndFamily };
    static void FromDto(FontDto d, ref Native.LOGFONT lf) { lf.lfFaceName = d.Face; lf.lfHeight = d.Height; lf.lfWeight = d.Weight; lf.lfWidth = d.Width; lf.lfItalic = d.Italic; lf.lfCharSet = d.CharSet; lf.lfQuality = d.Quality; lf.lfPitchAndFamily = d.PitchAndFamily; }

    /// <summary>Snapshot the fonts once, before the first change, so "restore" means "as before FontCrisp".</summary>
    public static void EnsureBackup()
    {
        if (HasBackup) return;
        var n = GetNcm();
        var b = new BackupDto { TakenAt = DateTime.Now, Metrics = [n.iBorderWidth, n.iScrollWidth, n.iScrollHeight, n.iCaptionWidth, n.iCaptionHeight, n.iSmCaptionWidth, n.iSmCaptionHeight, n.iMenuWidth, n.iMenuHeight, n.iPaddedBorderWidth] };
        b.Fonts["Caption"] = ToDto(n.lfCaptionFont); b.Fonts["SmCaption"] = ToDto(n.lfSmCaptionFont); b.Fonts["Menu"] = ToDto(n.lfMenuFont);
        b.Fonts["Status"] = ToDto(n.lfStatusFont); b.Fonts["Message"] = ToDto(n.lfMessageFont); b.Fonts["Icon"] = ToDto(GetIcon());
        Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
        File.WriteAllText(BackupPath, JsonSerializer.Serialize(b, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void Restore()
    {
        var b = JsonSerializer.Deserialize<BackupDto>(File.ReadAllText(BackupPath)) ?? throw new InvalidOperationException("备份文件无法解析");
        var n = GetNcm();   // keep the live metrics (border widths etc.), only the fonts go back
        var icon = GetIcon();
        FromDto(b.Fonts["Caption"], ref n.lfCaptionFont); FromDto(b.Fonts["SmCaption"], ref n.lfSmCaptionFont); FromDto(b.Fonts["Menu"], ref n.lfMenuFont);
        FromDto(b.Fonts["Status"], ref n.lfStatusFont); FromDto(b.Fonts["Message"], ref n.lfMessageFont); FromDto(b.Fonts["Icon"], ref icon);
        Set(n, icon);
        File.Delete(BackupPath);
    }
}
