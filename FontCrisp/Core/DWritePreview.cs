using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using Vortice.WIC;

namespace FontCrisp.Core;

/// <summary>
/// Renders sample text through Direct2D + DirectWrite with custom rendering params, i.e. what DirectWrite / WPF apps
/// would produce once the Avalon.Graphics values are applied. Also reads the parameters currently in force for a monitor.
/// </summary>
public sealed class DWritePreview : IDisposable
{
    readonly IWICImagingFactory _wic = new IWICImagingFactory();
    readonly ID2D1Factory _d2d = D2D1.D2D1CreateFactory<ID2D1Factory>(Vortice.Direct2D1.FactoryType.SingleThreaded);
    readonly IDWriteFactory2 _dw = DWrite.DWriteCreateFactory<IDWriteFactory2>(Vortice.DirectWrite.FactoryType.Shared);

    /// <summary>The effective parameters DirectWrite would hand any app on this monitor (registry values or built-in defaults).</summary>
    public DwSettings ReadMonitor(nint hMonitor)
    {
        using var p = _dw.CreateMonitorRenderingParams(hMonitor);
        using var p2 = p.QueryInterface<IDWriteRenderingParams2>();
        return new DwSettings(
            (int)Math.Round(p.ClearTypeLevel * 100),
            (int)Math.Round(p.EnhancedContrast * 100),
            (int)Math.Round(p2.GrayscaleEnhancedContrast * 100),
            (int)Math.Round(p.Gamma * 1000),
            p.PixelGeometry switch { PixelGeometry.Rgb => 1, PixelGeometry.Bgr => 2, _ => 0 });
    }

    /// <param name="aa">ClearType / Grayscale / Aliased, i.e. what the target app asks DirectWrite for.</param>
    /// <param name="mode">Default for modern apps; GdiClassic emulates GDI's own rasteriser (used to preview GDI settings).</param>
    public byte[] Render(int w, int h, float dpi, string face, float sizeDip, string text,
        Vortice.Direct2D1.TextAntialiasMode aa, DwSettings s, RenderingMode mode, bool dark, int weight = 400)
    {
        using var bmp = _wic.CreateBitmap((uint)w, (uint)h, Vortice.WIC.PixelFormat.Format32bppBGR, BitmapCreateCacheOption.CacheOnDemand);
        var props = new RenderTargetProperties(RenderTargetType.Software,
            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Ignore), dpi, dpi, RenderTargetUsage.None, FeatureLevel.Default);
        using var rt = _d2d.CreateWicBitmapRenderTarget(bmp, props);
        using var rp = _dw.CreateCustomRenderingParams(
            Math.Clamp(s.Gamma, 1000, 2200) / 1000f, s.EnhancedContrast / 100f, s.GrayscaleContrast / 100f, Math.Clamp(s.ClearTypeLevel, 0, 100) / 100f,
            s.PixelStructure switch { 1 => PixelGeometry.Rgb, 2 => PixelGeometry.Bgr, _ => PixelGeometry.Flat },
            mode, GridFitMode.Default);
        rt.TextRenderingParams = rp;
        rt.TextAntialiasMode = aa;
        using var tf = _dw.CreateTextFormat(face, (FontWeight)weight, Vortice.DirectWrite.FontStyle.Normal, FontStretch.Normal, sizeDip);
        using var brush = rt.CreateSolidColorBrush(dark ? new Color4(0.9f, 0.9f, 0.9f, 1) : new Color4(0.1f, 0.1f, 0.1f, 1));
        float scale = dpi / 96f;
        rt.BeginDraw();
        rt.Clear(dark ? new Color4(0.122f, 0.122f, 0.122f, 1) : new Color4(1, 1, 1, 1));
        rt.DrawText(text, tf, new Rect(8, 6, w / scale - 16, h / scale - 6), brush, DrawTextOptions.None, MeasuringMode.Natural);
        rt.EndDraw();

        var buf = new byte[w * h * 4];
        bmp.CopyPixels((uint)(w * 4), buf);
        return buf;
    }

    public void Dispose() { _dw.Dispose(); _d2d.Dispose(); _wic.Dispose(); }
}
