using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace FontCrisp.Core;

/// <summary>GDI font smoothing (the classic ClearType switches, applied through SystemParametersInfo).</summary>
public sealed record GdiSettings(bool Smoothing, bool ClearType, bool Rgb, int Contrast)
{
    public static readonly GdiSettings WindowsDefault = new(true, true, true, 1400);
}

/// <summary>
/// DirectWrite / WPF rendering parameters. Stored under HKCU\Software\Microsoft\Avalon.Graphics\DISPLAYn
/// (ClearTypeLevel 0-100, EnhancedContrastLevel ×100, GrayscaleEnhancedContrastLevel ×100, Gamma ×1000, PixelStructure 0/1/2).
/// </summary>
public sealed record DwSettings(int ClearTypeLevel, int EnhancedContrast, int GrayscaleContrast, int Gamma, int PixelStructure)
{
    public static readonly DwSettings WindowsDefault = new(100, 50, 100, 2200, 1);
}

public sealed record Preset(string Name, string Hint, GdiSettings Gdi, DwSettings Dw)
{
    public static readonly Preset[] All =
    [
        new("锐利", "ClearType 全开、对比增强拉满、低 Gamma：笔画黑实、边缘硬",
            new GdiSettings(true, true, true, 1000), new DwSettings(100, 400, 400, 1800, 1)),
        new("Windows 默认", "系统出厂值：对比增强 0.5，Gamma 2.2",
            GdiSettings.WindowsDefault, DwSettings.WindowsDefault),
        new("柔和", "降低 ClearType 彩边与对比，笔画更轻",
            new GdiSettings(true, true, true, 2000), new DwSettings(50, 30, 80, 2200, 1)),
    ];
}

/// <summary>Reads and writes the system-wide text rendering settings, with a one-time backup for undo.</summary>
public static class SystemText
{
    const string AvalonKey = @"Software\Microsoft\Avalon.Graphics";
    // "GammaLevel" is what DirectWrite actually reads (verified: changing it moves IDWriteRenderingParams.Gamma at once);
    // "Gamma" is the WPF-era name. Both are written so WPF and DirectWrite agree.
    static readonly string[] DwValueNames = ["ClearTypeLevel", "EnhancedContrastLevel", "GrayscaleEnhancedContrastLevel", "GammaLevel", "Gamma", "PixelStructure"];

    public static string BackupPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FontCrisp", "backup.json");
    public static bool HasBackup => File.Exists(BackupPath);

    public static GdiSettings ReadGdi() => new(
        Native.GetSpi(Native.SPI_GETFONTSMOOTHING) != 0,
        Native.GetSpi(Native.SPI_GETFONTSMOOTHINGTYPE) == 2,
        Native.GetSpi(Native.SPI_GETFONTSMOOTHINGORIENTATION) == 1,
        (int)Native.GetSpi(Native.SPI_GETFONTSMOOTHINGCONTRAST));

    public static void WriteGdi(GdiSettings g)
    {
        Native.SetSpiFlag(Native.SPI_SETFONTSMOOTHING, g.Smoothing);
        Native.SetSpiValue(Native.SPI_SETFONTSMOOTHINGTYPE, g.ClearType ? 2u : 1u);
        Native.SetSpiValue(Native.SPI_SETFONTSMOOTHINGCONTRAST, (uint)Math.Clamp(g.Contrast, 1000, 2200));
        Native.SetSpiValue(Native.SPI_SETFONTSMOOTHINGORIENTATION, g.Rgb ? 1u : 0u);
    }

    /// <summary>Raw registry values for one display sub-key; null entries mean "absent" (DirectWrite then uses its built-in default).</summary>
    public static Dictionary<string, int?> ReadDwRaw(string display)
    {
        var d = new Dictionary<string, int?>();
        using var k = Registry.CurrentUser.OpenSubKey($@"{AvalonKey}\{display}");
        foreach (var n in DwValueNames) d[n] = k?.GetValue(n) is int v ? v : null;
        return d;
    }

    public static void WriteDw(DwSettings s, IEnumerable<string> displays)
    {
        foreach (var disp in displays)
        {
            using var k = Registry.CurrentUser.CreateSubKey($@"{AvalonKey}\{disp}");
            k.SetValue("ClearTypeLevel", Math.Clamp(s.ClearTypeLevel, 0, 100), RegistryValueKind.DWord);
            k.SetValue("EnhancedContrastLevel", Math.Clamp(s.EnhancedContrast, 0, 1000), RegistryValueKind.DWord);
            k.SetValue("GrayscaleEnhancedContrastLevel", Math.Clamp(s.GrayscaleContrast, 0, 1000), RegistryValueKind.DWord);
            k.SetValue("GammaLevel", Math.Clamp(s.Gamma, 1000, 2200), RegistryValueKind.DWord);
            k.SetValue("Gamma", Math.Clamp(s.Gamma, 1000, 2200), RegistryValueKind.DWord);
            k.SetValue("PixelStructure", Math.Clamp(s.PixelStructure, 0, 2), RegistryValueKind.DWord);
        }
    }

    // ---- backup / restore ----
    sealed class Backup
    {
        public GdiSettings? Gdi { get; set; }
        public Dictionary<string, Dictionary<string, int?>> Displays { get; set; } = new();
        public DateTime TakenAt { get; set; }
    }

    /// <summary>Snapshot the current state once; later applies never overwrite it, so "restore" always means "as before FontCrisp".</summary>
    public static void EnsureBackup(IEnumerable<string> displays)
    {
        if (HasBackup) return;
        var b = new Backup { Gdi = ReadGdi(), TakenAt = DateTime.Now };
        foreach (var d in displays) b.Displays[d] = ReadDwRaw(d);
        Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
        File.WriteAllText(BackupPath, JsonSerializer.Serialize(b, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void Restore()
    {
        var b = JsonSerializer.Deserialize<Backup>(File.ReadAllText(BackupPath)) ?? throw new InvalidOperationException("备份文件无法解析");
        if (b.Gdi != null) WriteGdi(b.Gdi);
        foreach (var (disp, vals) in b.Displays)
        {
            using var k = Registry.CurrentUser.CreateSubKey($@"{AvalonKey}\{disp}");
            // a name we write but the backup never recorded (older backup) did not exist before → delete it
            foreach (var name in DwValueNames)
            {
                if (vals.TryGetValue(name, out var v) && v is int i) k.SetValue(name, i, RegistryValueKind.DWord);
                else k.DeleteValue(name, false);
            }
        }
        Native.BroadcastSettingChange("Windows");
        File.Delete(BackupPath);
    }

    public static void Apply(GdiSettings gdi, DwSettings dw)
    {
        var displays = Native.ActiveDisplayNames();
        EnsureBackup(displays);
        WriteGdi(gdi);
        WriteDw(dw, displays);
        Native.BroadcastSettingChange("Windows");
    }
}
