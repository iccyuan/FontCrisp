using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FontCrisp.Core;

namespace FontCrisp;

public partial class MainWindow : Window
{
    const string Face = "Microsoft YaHei UI";
    const double SampleSize = 13.5; // DIP, same as PopBar's UI font

    readonly DWritePreview _dw = new();
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(120) };
    readonly DispatcherTimer _flicker = new() { Interval = TimeSpan.FromMilliseconds(650) };
    bool _ready, _flip;
    double _scale = 1;
    nint _monitor;
    // what is in force right now (left preview column); refreshed after apply / restore
    GdiSettings _curGdi = GdiSettings.WindowsDefault;
    DwSettings _curDw = DwSettings.WindowsDefault;
    UiFontSettings _curUi = new("Microsoft YaHei UI", 9, 400);
    static readonly int[] UiWeights = [400, 500, 600, 700];

    public MainWindow()
    {
        InitializeComponent();
        _debounce.Tick += (_, _) => { _debounce.Stop(); RenderPreviews(); };
        _flicker.Tick += (_, _) => { _flip = !_flip; RenderPreviews(); };
        Loaded += OnLoaded;
        SourceInitialized += (_, _) => Acrylic.Apply(this, App.IsDark);
        SizeChanged += (_, _) => Kick();
        Closed += (_, _) => _dw.Dispose();
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        DarkBg.IsChecked = App.IsDark;
        _scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        _monitor = Native.MonitorFromWindow(new WindowInteropHelper(this).Handle, 2 /*MONITOR_DEFAULTTONEAREST*/);
        LoadFromSystem();
        RefreshDiag();
        RefreshWindows();
        UpdateTerminalStatus();
        RestoreBtn.IsEnabled = SystemText.HasBackup;
        LoadUiFont();
        _ready = true;
        RenderPreviews();
    }

    // ---- settings <-> UI ----
    GdiSettings UiGdi => new(GdiOn.IsChecked == true, GdiType.SelectedIndex == 1, GdiRgb.SelectedIndex == 0, (int)GdiContrast.Value);
    DwSettings UiDw => new((int)DwCt.Value, (int)DwEc.Value, (int)DwGec.Value, (int)DwGamma.Value, Math.Max(0, DwPixel.SelectedIndex));

    void ReadCurrent() { _curGdi = SystemText.ReadGdi(); _curDw = _dw.ReadMonitor(_monitor); }
    void LoadFromSystem() { ReadCurrent(); SetUi(_curGdi, _curDw); }

    void SetUi(GdiSettings g, DwSettings d)
    {
        bool was = _ready; _ready = false;
        GdiOn.IsChecked = g.Smoothing;
        GdiType.SelectedIndex = g.ClearType ? 1 : 0;
        GdiRgb.SelectedIndex = g.Rgb ? 0 : 1;
        GdiContrast.Value = Math.Clamp(g.Contrast, 1000, 2200);
        DwCt.Value = d.ClearTypeLevel;
        DwEc.Value = d.EnhancedContrast;
        DwGec.Value = d.GrayscaleContrast;
        DwGamma.Value = Math.Clamp(d.Gamma, 1000, 2200);
        DwPixel.SelectedIndex = Math.Clamp(d.PixelStructure, 0, 2);
        _ready = was;
        UpdateValueLabels();
        Kick();
    }

    void UpdateValueLabels()
    {
        DwEcVal.Text = (DwEc.Value / 100).ToString("0.00");
        DwGecVal.Text = (DwGec.Value / 100).ToString("0.00");
        DwGammaVal.Text = (DwGamma.Value / 1000).ToString("0.00");
    }

    void Param_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        UpdateValueLabels();
        Kick();
    }

    void Kick() { _debounce.Stop(); _debounce.Start(); }

    void Flicker_Click(object sender, RoutedEventArgs e)
    {
        _flip = false;
        if (Flicker.IsChecked == true) _flicker.Start(); else _flicker.Stop();
        Kick();
    }

    // ---- preview: left column = what is in force now, right column = the sliders ----
    const Vortice.Direct2D1.TextAntialiasMode ClearType = Vortice.Direct2D1.TextAntialiasMode.Cleartype;
    const Vortice.Direct2D1.TextAntialiasMode Grayscale = Vortice.Direct2D1.TextAntialiasMode.Grayscale;

    /// <summary>GDI's rasteriser has no DirectWrite API, so the "after" pane emulates it with DWrite's GDI-classic mode.</summary>
    static Vortice.Direct2D1.TextAntialiasMode GdiAa(GdiSettings g) =>
        !g.Smoothing ? Vortice.Direct2D1.TextAntialiasMode.Aliased : g.ClearType ? ClearType : Grayscale;
    static DwSettings GdiAsDw(GdiSettings g) => new(100, 0, 100, g.Contrast, g.Rgb ? 1 : 2);

    void RenderPreviews()
    {
        if (!_ready) return;
        int zoom = Math.Max(1, ZoomBox.SelectedIndex + 1);
        bool dark = DarkBg.IsChecked == true;
        string text = SampleText.Text;
        double availDip = Math.Max(640, PreviewHost.ActualWidth - 2);
        int w = (int)((availDip - 12) / 2 * _scale / zoom);
        int h = (int)(Math.Ceiling(SampleSize * 1.55 * 3 + 14) * _scale);
        float dpi = (float)(96 * _scale);
        float size = (float)SampleSize;

        bool flicker = Flicker.IsChecked == true;
        bool showB = !flicker || _flip;              // flicker mode: the right pane alternates A / B
        var dwB = showB ? UiDw : _curDw;
        var gdiB = showB ? UiGdi : _curGdi;
        RightHead.Text = flicker ? (showB ? "◉ 调整后" : "○ 当前系统")
            : UiDw == _curDw && UiGdi == _curGdi ? "调整后（与当前相同）" : "调整后（下面的滑块）";

        var dft = Vortice.DirectWrite.RenderingMode.Default;
        var gdi = Vortice.DirectWrite.RenderingMode.GdiClassic;
        Show(ImgDwCtA, _dw.Render(w, h, dpi, Face, size, text, ClearType, _curDw, dft, dark), w, h, zoom);
        Show(ImgDwCtB, _dw.Render(w, h, dpi, Face, size, text, ClearType, dwB, dft, dark), w, h, zoom);
        Show(ImgDwGrayA, _dw.Render(w, h, dpi, Face, size, text, Grayscale, _curDw, dft, dark), w, h, zoom);
        Show(ImgDwGrayB, _dw.Render(w, h, dpi, Face, size, text, Grayscale, dwB, dft, dark), w, h, zoom);
        Show(ImgGdiA, GdiPreview.Render(w, h, _scale, Face, SampleSize, text, dark), w, h, zoom);
        Show(ImgGdiB, _dw.Render(w, h, dpi, Face, size, text, GdiAa(gdiB), GdiAsDw(gdiB), gdi, dark), w, h, zoom);
        RenderUiPreview(w, zoom, dark);
    }

    // ---- shell UI font (Explorer panes, dialogs, menus) ----
    UiFontSettings UiSelection => new(UiFace.SelectedItem as string ?? _curUi.Face, 9 + Math.Max(0, UiSize.SelectedIndex), UiWeights[Math.Clamp(UiWeight.SelectedIndex, 0, 3)]);

    void LoadUiFont()
    {
        _curUi = UiFont.Read();
        // GDI family names (English where the font has one) – that is what LOGFONT.lfFaceName wants
        var en = System.Windows.Markup.XmlLanguage.GetLanguage("en-us");
        var names = Fonts.SystemFontFamilies
            .Select(f => f.FamilyNames.TryGetValue(en, out var n) ? n : f.FamilyNames.Values.FirstOrDefault() ?? f.Source)
            .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        if (!names.Contains(_curUi.Face, StringComparer.OrdinalIgnoreCase)) names.Insert(0, _curUi.Face);
        UiFace.ItemsSource = names;
        SetUiFontUi(_curUi);
        UiCurHead.Text = $"当前系统：{_curUi}";
        UiRestoreBtn.IsEnabled = UiFont.HasBackup;
    }

    void SetUiFontUi(UiFontSettings s)
    {
        bool was = _ready; _ready = false;
        UiFace.SelectedItem = ((IEnumerable<string>)UiFace.ItemsSource).FirstOrDefault(n => string.Equals(n, s.Face, StringComparison.OrdinalIgnoreCase)) ?? s.Face;
        UiSize.SelectedIndex = Math.Clamp(s.SizePt - 9, 0, 3);
        UiWeight.SelectedIndex = s.Weight switch { < 450 => 0, < 550 => 1, < 650 => 2, _ => 3 };
        _ready = was;
        Kick();
    }

    void RenderUiPreview(int w, int zoom, bool dark)
    {
        const string sample = "桌面  下载  文档  图片  OS (C:)  此电脑\nDevelop  localization  2026/10/11 15:12  文件夹";
        var sel = UiSelection;
        float dpi = (float)(96 * _scale);
        int h = (int)(Math.Ceiling(Math.Max(sel.SizePt, _curUi.SizePt) * 96.0 / 72 * 1.5 * 2 + 14) * _scale);
        var gdi = Vortice.DirectWrite.RenderingMode.GdiClassic;   // Explorer's DirectUI text is GDI-compatible, hinted
        Show(ImgUiA, _dw.Render(w, h, dpi, _curUi.Face, _curUi.SizePt * 96f / 72, sample, ClearType, _curDw, gdi, dark, _curUi.Weight), w, h, zoom);
        Show(ImgUiB, _dw.Render(w, h, dpi, sel.Face, sel.SizePt * 96f / 72, sample, ClearType, _curDw, gdi, dark, sel.Weight), w, h, zoom);
        UiNewHead.Text = sel == _curUi ? "选中的字体（与当前相同）" : $"选中的字体：{sel}";
    }

    void UiFont_Changed(object sender, SelectionChangedEventArgs e) => Kick();

    void UiRecommend_Click(object sender, RoutedEventArgs e)
    {
        var names = (IEnumerable<string>)UiFace.ItemsSource;
        var face = UiFont.PreferredFaces.FirstOrDefault(p => names.Contains(p, StringComparer.OrdinalIgnoreCase));
        if (face is null)
        {
            UiStatus.Text = "没装 HarmonyOS Sans / MiSans / 思源黑体这类无 hinting 的中文字体，退而求其次用雅黑 UI 10pt 中等。";
            SetUiFontUi(new("Microsoft YaHei UI", 10, 500));
            return;
        }
        SetUiFontUi(new(face, 9, 500));
        UiStatus.Text = $"推荐 {face} 9pt：无 hinting、中等字重，资源管理器导航栏的笔画从 1 像素变 2 像素，浓度与 PopBar 一致。";
    }

    void UiApply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            UiFont.Apply(UiSelection);
            _curUi = UiFont.Read();
            UiCurHead.Text = $"当前系统：{_curUi}";
            UiRestoreBtn.IsEnabled = UiFont.HasBackup;
            UiStatus.Text = "已应用。新开的资源管理器窗口立即生效，已打开的窗口请关掉重开；任务栏 / 桌面要重启 explorer.exe，其他程序重启后生效。";
        }
        catch (Exception ex) { UiStatus.Text = "应用失败：" + ex.Message; }
        Kick();
    }

    void UiRestore_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            UiFont.Restore();
            _curUi = UiFont.Read();
            SetUiFontUi(_curUi);
            UiCurHead.Text = $"当前系统：{_curUi}";
            UiStatus.Text = "已恢复为使用 FontCrisp 之前的界面字体（窗口重开后生效）。";
        }
        catch (Exception ex) { UiStatus.Text = "恢复失败：" + ex.Message; }
        UiRestoreBtn.IsEnabled = UiFont.HasBackup;
        Kick();
    }

    /// <summary>Show a device-pixel buffer 1:1 (times zoom) regardless of the window's DPI scale.</summary>
    void Show(Image img, byte[] pixels, int w, int h, int zoom)
    {
        var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
        bmp.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
        img.Source = bmp;
        img.LayoutTransform = new ScaleTransform(zoom / _scale, zoom / _scale);
    }

    // ---- presets / apply ----
    void Preset_Click(object sender, RoutedEventArgs e)
    {
        var p = Preset.All[int.Parse((string)((Button)sender).Tag)];
        SetUi(p.Gdi, p.Dw);
        Status.Text = $"已载入预设「{p.Name}」：{p.Hint}。按「应用到系统」生效。";
    }

    void Reload_Click(object sender, RoutedEventArgs e) { LoadFromSystem(); Status.Text = "已读取当前系统值。"; }

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SystemText.Apply(UiGdi, UiDw);
            ReadCurrent();
            RestoreBtn.IsEnabled = SystemText.HasBackup;
            Status.Text = "已应用。GDI 程序立即生效；DirectWrite/WPF 程序（PopBar、资源管理器、设置等）需重新启动，或注销后重新登录。";
        }
        catch (Exception ex) { Status.Text = "应用失败：" + ex.Message; }
        RefreshDiag();
        Kick();
    }

    void Restore_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SystemText.Restore();
            LoadFromSystem();
            Status.Text = "已恢复到使用 FontCrisp 之前的设置（DirectWrite 程序重启后生效）。";
        }
        catch (Exception ex) { Status.Text = "恢复失败：" + ex.Message; }
        RestoreBtn.IsEnabled = SystemText.HasBackup;
        RefreshDiag();
        Kick();
    }

    // ---- diagnosis ----
    void RefreshDiag()
    {
        uint sysDpi = Native.GetDpiForSystem();
        nint dc = Native.GetDC(0);
        int wpx = Native.GetDeviceCaps(dc, 8 /*HORZRES*/), hpx = Native.GetDeviceCaps(dc, 10 /*VERTRES*/), wmm = Native.GetDeviceCaps(dc, 4 /*HORZSIZE*/);
        Native.ReleaseDC(0, dc);
        double ppi = wmm > 0 ? wpx / (wmm / 25.4) : 0;
        var g = SystemText.ReadGdi();
        var d = _dw.ReadMonitor(_monitor);
        string ct = !g.Smoothing ? "关" : g.ClearType ? "开（彩色次像素）" : "开（标准灰度）";
        DiagText.Text =
            $"屏幕 {wpx}×{hpx}，缩放 {sysDpi * 100 / 96}%，约 {ppi:0} PPI。" +
            $"当前生效：GDI 平滑 {ct}，{(g.Rgb ? "RGB" : "BGR")}，对比度 {g.Contrast}；" +
            $"DirectWrite ClearType {d.ClearTypeLevel}%，彩色对比 {d.EnhancedContrast / 100.0:0.00}，灰度对比 {d.GrayscaleContrast / 100.0:0.00}，Gamma {d.Gamma / 1000.0:0.00}。";
        AutoFix.IsChecked = DpiAudit.AutoFixEnabled;
    }

    void RefreshWindows()
    {
        var all = DpiAudit.Scan();
        var shown = ShowAll.IsChecked == true ? all : all.Where(e => e.IsBlurry || e.IsFixed).ToList();
        WinList.ItemsSource = shown;
        bool none = shown.Count == 0;
        NoBlurry.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
        WinList.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
    }
    void Refresh_Click(object sender, RoutedEventArgs e) { RefreshDiag(); RefreshWindows(); UpdateTerminalStatus(); }

    void AutoFix_Click(object sender, RoutedEventArgs e)
    {
        DpiAudit.AutoFixEnabled = AutoFix.IsChecked == true;
        Native.BroadcastSettingChange("Windows");
        Status.Text = "「修复应用缩放」已" + (AutoFix.IsChecked == true ? "开启" : "关闭") + "，对之后启动的程序生效。";
    }

    void FixGdi_Click(object sender, RoutedEventArgs e) => SetLayer(sender, DpiAudit.ModeGdiScaling, "已修复：关掉这个程序再打开就清晰了");
    void FixAware_Click(object sender, RoutedEventArgs e) => SetLayer(sender, DpiAudit.ModeAware, "已改为不放大该程序：重开后文字清晰、界面变小");
    void FixClear_Click(object sender, RoutedEventArgs e) => SetLayer(sender, null, "已撤销，重开该程序后恢复原样");

    void SetLayer(object sender, string? mode, string what)
    {
        if (((Button)sender).Tag is not WindowEntry w || w.ExePath is null)
        {
            MessageBox.Show("拿不到这个程序的 exe 路径（多半是以管理员身份运行的）。\n请在它的 exe 上右键 › 属性 › 兼容性 › 更改高 DPI 设置，手动勾选「系统(增强)」。", "FontCrisp", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DpiAudit.SetLayer(w.ExePath, mode);
        MessageBox.Show($"{w.Process}\n{what}。", "FontCrisp", MessageBoxButton.OK, MessageBoxImage.Information);
        RefreshWindows();
    }

    // ---- terminal ----
    void UpdateTerminalStatus()
    {
        var m = TerminalFix.CurrentMode();
        TermStatus.Text = m is null ? "未安装" : $"当前 antialiasingMode = {m}";
    }

    void TermClearType_Click(object sender, RoutedEventArgs e)
    {
        int n = TerminalFix.Apply("cleartype");
        UpdateTerminalStatus();
        Status.Text = n == 0 ? "Windows Terminal 设置未改动（未安装或找不到 profiles.defaults）。" : $"已把 {n} 个 Terminal 设置文件切到 ClearType。";
    }

    void TermGray_Click(object sender, RoutedEventArgs e)
    {
        int n = TerminalFix.Apply("grayscale");
        UpdateTerminalStatus();
        Status.Text = n == 0 ? "Windows Terminal 设置未改动。" : $"已把 {n} 个 Terminal 设置文件恢复为灰度。";
    }
}
