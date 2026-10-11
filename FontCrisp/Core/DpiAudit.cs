using System.Text;
using Microsoft.Win32;

namespace FontCrisp.Core;

public sealed class WindowEntry
{
    public nint Hwnd { get; init; }
    public string Process { get; init; } = "";
    public string? ExePath { get; init; }
    public string Title { get; init; } = "";
    public string Awareness { get; init; } = "";
    public uint Dpi { get; init; }
    public string Verdict { get; init; } = "";
    public string Layer { get; init; } = "";
    /// <summary>Stretched as a bitmap by Windows → text is blurry; this is the row the user should act on.</summary>
    public bool IsBlurry { get; init; }
    public bool IsFixed { get; init; }
}

/// <summary>
/// Finds top-level windows whose process is not DPI aware: at 125 %/150 % scaling Windows stretches those as bitmaps,
/// which is the single most common cause of "blurry text". Fixes go through the same AppCompatFlags\Layers values the
/// Explorer "Compatibility → High DPI settings" dialog writes.
/// </summary>
public static class DpiAudit
{
    const string LayersKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    static readonly string[] DpiTokens = ["HIGHDPIAWARE", "DPIUNAWARE", "GDIDPISCALING", "PERPROCESSSYSTEMDPIFORCEON", "PERPROCESSSYSTEMDPIFORCEOFF"];

    public const string ModeAware = "HIGHDPIAWARE";               // "Application" in the dialog: app draws at native DPI (small but sharp)
    public const string ModeGdiScaling = "GDIDPISCALING DPIUNAWARE"; // "System (Enhanced)": GDI text re-rendered sharp at the target DPI

    public static List<WindowEntry> Scan()
    {
        var list = new List<WindowEntry>();
        uint sysDpi = Native.GetDpiForSystem();
        uint self = (uint)Environment.ProcessId;
        Native.EnumWindows((h, _) =>
        {
            if (!Native.IsWindowVisible(h) || Native.GetWindowTextLengthW(h) == 0 || Native.GetAncestor(h, 3 /*GA_ROOTOWNER*/) != h) return true;
            Native.GetWindowThreadProcessId(h, out uint pid);
            if (pid == self) return true;
            var sb = new StringBuilder(256);
            Native.GetWindowTextW(h, sb, 256);
            int aw = Native.GetAwarenessFromDpiAwarenessContext(Native.GetWindowDpiAwarenessContext(h));
            string? exe = Native.GetProcessPath(pid);
            string layer = exe is null ? "" : GetLayer(exe) ?? "";
            bool fixedByLayer = layer.Contains("GDIDPISCALING", StringComparison.OrdinalIgnoreCase) || layer.Contains("HIGHDPIAWARE", StringComparison.OrdinalIgnoreCase);
            bool blurry = aw == 0 && sysDpi != 96;
            string verdict = fixedByLayer ? "已修复，重新启动该程序后生效"
                : aw switch
                {
                    0 => blurry ? "发虚：被 Windows 整体放大" : "正常（100% 缩放下不会放大）",
                    1 => "正常（拖到另一块不同缩放的屏幕时会模糊）",
                    _ => "正常",
                };
            list.Add(new WindowEntry
            {
                Hwnd = h,
                Process = exe is null ? $"pid {pid}" : System.IO.Path.GetFileName(exe),
                ExePath = exe,
                Title = sb.ToString(),
                Awareness = aw switch { 0 => "不感知", 1 => "系统级", _ => "逐屏 (v2)" },
                Dpi = Native.GetDpiForWindow(h),
                Verdict = verdict,
                Layer = layer,
                IsBlurry = blurry && !fixedByLayer,
                IsFixed = fixedByLayer,
            });
            return true;
        }, 0);
        // blurry first, then already-fixed, then the rest
        return list.OrderBy(e => e.IsBlurry ? 0 : e.IsFixed ? 1 : 2).ThenBy(e => e.Process).ToList();
    }

    public static string? GetLayer(string exe)
    {
        using var k = Registry.CurrentUser.OpenSubKey(LayersKey);
        return k?.GetValue(exe) as string;
    }

    /// <summary>Replace the DPI-related tokens of the exe's compat layer with <paramref name="mode"/> (null = remove them), keeping any others (e.g. RUNASADMIN).</summary>
    public static void SetLayer(string exe, string? mode)
    {
        using var k = Registry.CurrentUser.CreateSubKey(LayersKey);
        var tokens = ((k.GetValue(exe) as string) ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t != "~" && !DpiTokens.Contains(t, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (mode != null) tokens.AddRange(mode.Split(' '));
        if (tokens.Count == 0) k.DeleteValue(exe, false);
        else k.SetValue(exe, "~ " + string.Join(' ', tokens), RegistryValueKind.String);
    }

    /// <summary>Settings → Display → "Fix scaling for apps". Lets Windows re-render DPI-unaware apps when they move between monitors.</summary>
    public static bool AutoFixEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            return k?.GetValue("EnablePerProcessSystemDPI") is int v && v != 0;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop");
            k.SetValue("EnablePerProcessSystemDPI", value ? 1 : 0, RegistryValueKind.DWord);
        }
    }
}
