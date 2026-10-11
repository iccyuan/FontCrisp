using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FontCrisp.Core;

namespace FontCrisp;

public partial class App : Application
{
    public static bool IsDark { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.ToString(), "FontCrisp 出错", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // ---- headless modes (used by tools/ and for scripting) ----
        if (e.Args.Length >= 2 && e.Args[0] == "--preset")
        {
            // apply a preset without UI: --preset 0 (锐利) | 1 (Windows 默认) | 2 (柔和)
            var p = Preset.All[int.Parse(e.Args[1])];
            SystemText.Apply(p.Gdi, p.Dw);
            Shutdown(0);
            return;
        }
        if (e.Args.Length >= 4 && e.Args[0] == "--uifont")
        {
            // shell UI font without UI: --uifont "HarmonyOS Sans SC Medium" 9 500
            UiFont.Apply(new UiFontSettings(e.Args[1], int.Parse(e.Args[2]), int.Parse(e.Args[3])));
            Shutdown(0);
            return;
        }
        if (e.Args.Length >= 1 && e.Args[0] == "--uifont-restore")
        {
            if (UiFont.HasBackup) UiFont.Restore();
            Shutdown(0);
            return;
        }
        if (e.Args.Length >= 2 && e.Args[0] == "--render-test")
        {
            RenderTest(e.Args[1]);
            Shutdown(0);
            return;
        }
        if (e.Args.Length >= 2 && e.Args[0] == "--dump")
        {
            // what DirectWrite actually hands out for the primary monitor vs. the raw registry
            using var dw = new DWritePreview();
            var m = dw.ReadMonitor(Core.Native.MonitorFromWindow(0, 1 /*MONITOR_DEFAULTTOPRIMARY*/));
            var lines = new List<string> { $"dwrite: {m}", $"gdi: {SystemText.ReadGdi()}" };
            foreach (var d in Core.Native.ActiveDisplayNames())
                lines.Add($"registry {d}: " + string.Join(", ", SystemText.ReadDwRaw(d).Select(kv => $"{kv.Key}={kv.Value?.ToString() ?? "absent"}")));
            File.WriteAllLines(e.Args[1], lines);
            Shutdown(0);
            return;
        }

        IsDark = Acrylic.SystemIsDark;
        if (IsDark) ApplyDarkPalette();
        if (!Acrylic.SystemBackdropSupported)
            Resources["WindowBgBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(IsDark ? "#FF1B1F28" : "#FFF3F4F6"));
        new MainWindow().Show();
    }

    /// <summary>Render the DirectWrite preview for every preset into PNGs so a script can diff them.</summary>
    static void RenderTest(string dir)
    {
        Directory.CreateDirectory(dir);
        using var dw = new DWritePreview();
        const string text = "字体锐化 FontCrisp 微软雅黑 The quick brown fox 0123456789";
        for (int i = 0; i < Preset.All.Length; i++)
        {
            foreach (var (mode, ct) in new[] { ("ct", true), ("gray", false) })
            {
                int w = 900, h = 60;
                var px = dw.Render(w, h, 144, "Microsoft YaHei UI", 13.5f, text,
                    ct ? Vortice.Direct2D1.TextAntialiasMode.Cleartype : Vortice.Direct2D1.TextAntialiasMode.Grayscale,
                    Preset.All[i].Dw, Vortice.DirectWrite.RenderingMode.Default, false);
                var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
                bmp.WritePixels(new Int32Rect(0, 0, w, h), px, w * 4, 0);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using var fs = File.Create(Path.Combine(dir, $"preset{i}-{mode}.png"));
                enc.Save(fs);
            }
        }
    }

    /// <summary>Dark glass: translucent white surfaces over the dark Mica backdrop.</summary>
    void ApplyDarkPalette()
    {
        void Set(string key, string color) => Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        Set("CardBrush", "#2EFFFFFF");
        Set("CardBorderBrush", "#26FFFFFF");
        Set("DividerBrush", "#26FFFFFF");
        Set("TextBrush", "#F3F4F6");
        Set("MutedBrush", "#C3CAD6");
        Set("SubtleTextBrush", "#A6AFBD");
        Set("AccentBrush", "#5B9BFF");
        Set("AccentHoverBrush", "#7CB0FF");
        Set("AccentSoftBrush", "#663B82F6");
        Set("HoverBrush", "#26FFFFFF");
        Set("PressedBrush", "#40FFFFFF");
        Set("InputBrush", "#1FFFFFFF");
        Set("InputBorderBrush", "#33FFFFFF");
        Set("SwitchOffBrush", "#4DFFFFFF");
        Set("PopupBrush", "#F7232936");
        Set("ScrollThumbBrush", "#59FFFFFF");
        Set("WarnBrush", "#FBBF24");
    }
}
