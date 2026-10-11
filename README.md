# FontCrisp

让 Windows 文字像 PopBar 一样黑实清晰的小工具（C# WPF，.NET 10，单 exe）。

## 它解决什么

Windows 11 里文字"发虚"通常是三件事叠加：

1. **灰度抗锯齿**：设置、资源管理器、开始菜单、Windows Terminal、Edge/Chrome 都用灰度抗锯齿而不是 ClearType，
   笔画偏淡、边缘发软。PopBar 显得"实"，是因为它强制 ClearType 彩色次像素 + 高对比参数。
2. **渲染参数**：DirectWrite/WPF 的 ClearType 强度、对比增强、灰度对比增强、Gamma（`HKCU\Software\Microsoft\Avalon.Graphics\DISPLAYn`），
   以及 GDI 的 ClearType 对比度（`SystemParametersInfo`）。这些值决定所有程序的笔画浓淡。
3. **DPI 位图拉伸**：在 125%/150% 缩放下，不感知 DPI 的旧程序整窗被位图放大，文字必然模糊。

界面：Mica 毛玻璃（WindowChrome 玻璃边框 + DwmSetWindowAttribute 系统背景）、半透明卡片、与 PopBar 同款的开关/滑块/下拉样式，
跟随系统浅色/深色模式（`Themes/Glass.xaml`，深色调色板在 `App.xaml.cs`）。半透明卡片上靠 `RenderOptions.ClearTypeHint=Enabled` 保住真 ClearType 文字。

FontCrisp 把能从系统层面做的都做了：

- **所见即所得预览**：用真实 DirectWrite（Direct2D 自定义渲染参数，ClearType 与灰度各一份）和真实 GDI（DrawText 到 DIB）渲染样张，
  按设备像素 1:1 显示，可放大 2×/3×。
- **界面字体**：资源管理器导航栏 / 文件列表、Win32 对话框、菜单用的是系统「图标 / 消息」字体（默认微软雅黑 UI 9pt）。
  雅黑在这个字号下被 hinting 压成 1 像素细线，这才是资源管理器"又细又淡"的真正原因（用 1:1 截图对比 PopBar 证实）；
  换成无 hinting 的中等字重字体（HarmonyOS Sans SC Medium / MiSans Medium / 思源黑体 Medium）笔画变 2 像素，浓度与 PopBar 一致。
  走 `SPI_SETNONCLIENTMETRICS` + `SPI_SETICONTITLELOGFONT`，HKCU 级、无需注销，新开窗口立即生效；首次改动自动备份，可一键恢复。
- **参数调校 + 预设**：「锐利」「Windows 默认」「柔和」，一键应用到系统；首次应用自动备份，可一键恢复到用 FontCrisp 之前。
- **发虚程序诊断**：列出所有顶层窗口的 DPI 感知级别，标出被位图拉伸的程序，按钮直接写兼容项
  （`AppCompatFlags\Layers`：GDI 缩放 = 系统(增强)，按程序自绘 = 应用程序），以及系统「修复应用缩放」开关。
- **Windows Terminal 切 ClearType**：改 `profiles.defaults.antialiasingMode`，Terminal 热重载。

## 做不到的（刻意不做）

WinUI 界面（设置/资源管理器/开始菜单）只有灰度；Chromium 程序（Edge/Chrome/VS Code）用自带 Skia 渲染。
要在这些程序里强制彩色次像素，必须向每个进程注入 DLL 钩住 DirectWrite（MacType 的做法），稳定性与安全性都不可控，本工具不做。

## 构建与测试

```powershell
$env:HTTPS_PROXY='http://127.0.0.1:7890'   # NuGet 需要代理
dotnet build FontCrisp/FontCrisp.csproj -c Release
pwsh tools/test-ui.ps1 -Keep              # 启动并截图到 %TEMP%\fontcrisp-test
```

依赖：Vortice.Direct2D1（仅用于 DirectWrite 预览）。所有改动只写 HKCU，不需要管理员。

命令行：`FontCrisp.exe --preset 0|1|2`（无界面直接应用预设）、`--uifont "<字体>" <pt> <字重>` / `--uifont-restore`（界面字体）、`--dump <file>`（DirectWrite 实际读到的参数 vs 注册表）、`--render-test <dir>`（各预设预览渲染成 PNG）。

注册表坑：DirectWrite 读的 gamma 键名是 **GammaLevel**（实验证实，改它 `IDWriteRenderingParams.Gamma` 立刻变），`Gamma` 只是 WPF 时代的名字；
`EnhancedContrastLevel`、`GrayscaleEnhancedContrastLevel`、`ClearTypeLevel`、`PixelStructure` 则是实时读取的。FontCrisp 两种 gamma 键都写。

## 文件

- `Core/Native.cs` — SPI / GDI / DPI 感知 / 显示设备 P/Invoke
- `Core/Settings.cs` — GDI 与 DirectWrite 参数读写、备份恢复、预设
- `Core/UiFont.cs` — 系统界面字体（NONCLIENTMETRICS + 图标字体）读写与备份恢复
- `Core/GdiPreview.cs`、`Core/DWritePreview.cs` — 两套真实渲染预览
- `Core/DpiAudit.cs` — 窗口 DPI 感知扫描与兼容项写入
- `Core/TerminalFix.cs` — Terminal settings.json 文本改写（保留注释）
- `Core/Acrylic.cs` — Mica 背景 / 深色标题栏；`Themes/Glass.xaml` — 毛玻璃主题与控件模板
