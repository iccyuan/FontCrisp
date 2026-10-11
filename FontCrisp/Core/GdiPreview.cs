using System.Runtime.InteropServices;
using static FontCrisp.Core.Native;

namespace FontCrisp.Core;

/// <summary>Renders sample text with real GDI (DrawText on a 32-bit DIB), so it shows exactly what GDI apps get right now.</summary>
public static class GdiPreview
{
    public static unsafe byte[] Render(int w, int h, double scale, string face, double sizeDip, string text, bool dark)
    {
        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32,
            }
        };
        nint hdc = CreateCompatibleDC(0);
        nint dib = CreateDIBSection(hdc, ref bmi, 0, out nint bits, 0, 0);
        nint oldBmp = SelectObject(hdc, dib);
        new Span<uint>((void*)bits, w * h).Fill(dark ? 0xFF1F1F1Fu : 0xFFFFFFFFu);

        var lf = new LOGFONT
        {
            lfHeight = -(int)Math.Round(sizeDip * scale), lfWeight = 400, lfCharSet = 1 /*DEFAULT_CHARSET*/,
            lfQuality = 0 /*DEFAULT_QUALITY: follow the system smoothing setting, like most apps*/, lfFaceName = face,
        };
        nint font = CreateFontIndirectW(ref lf);
        nint oldFont = SelectObject(hdc, font);
        SetBkMode(hdc, 1 /*TRANSPARENT*/);
        SetTextColor(hdc, dark ? 0x00E6E6E6u : 0x001A1A1Au);
        int pad = (int)(8 * scale);
        var rc = new RECT { Left = pad, Top = (int)(6 * scale), Right = w - pad, Bottom = h };
        DrawTextW(hdc, text, -1, ref rc, 0x0010 /*DT_WORDBREAK*/ | 0x0800 /*DT_NOPREFIX*/);
        GdiFlush();

        var buf = new byte[w * h * 4];
        Marshal.Copy(bits, buf, 0, buf.Length);
        SelectObject(hdc, oldFont); DeleteObject(font);
        SelectObject(hdc, oldBmp); DeleteObject(dib);
        DeleteDC(hdc);
        return buf;
    }
}
