// The print file: a PDF whose every page is one full-bleed 300 DPI image.
//
// Written by hand (no library): PDF 1.4, images stored losslessly as
// /FlateDecode RGB. Pages are pulled one at a time, so a large batch is
// never all in memory at once.

using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace IdMaker.Core;

/// <summary>One page: opaque RGB pixels, and the physical size they are printed at.</summary>
public sealed record PdfPage(int Width, int Height, byte[] Rgb, double WidthMm, double HeightMm);

public static class PdfWriter
{
    private static string Num(double v) => v.ToString("0.0000", CultureInfo.InvariantCulture);

    // Text as UTF-16BE hex, so names with ñ, é and the rest survive.
    private static string Text(string s)
    {
        var sb = new StringBuilder("<FEFF");
        foreach (var unit in s) sb.Append(((int)unit).ToString("x4", CultureInfo.InvariantCulture));
        return sb.Append('>').ToString();
    }

    private static string Date(DateTime d) => "D:" + d.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

    private static byte[] Deflate(byte[] bytes)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(bytes, 0, bytes.Length);
        return ms.ToArray();
    }

    /// <summary>
    /// Write the document. `pages` are rendered on demand, in order;
    /// onProgress is told (done, total) after each.
    /// </summary>
    public static void Write(Stream output, IReadOnlyList<Func<PdfPage>> pages, string title, DateTime created,
        Action<int, int>? onProgress = null)
    {
        long length = 0;
        var offsets = new long[4 + 3 * pages.Count];

        void Push(byte[] bytes) { output.Write(bytes, 0, bytes.Length); length += bytes.Length; }
        void PushText(string s) => Push(Encoding.ASCII.GetBytes(s));
        void Obj(int num, string body, byte[]? stream = null)
        {
            offsets[num] = length;
            PushText($"{num.ToString(CultureInfo.InvariantCulture)} 0 obj\n{body}\n");
            if (stream is not null) { PushText("stream\n"); Push(stream); PushText("\nendstream\n"); }
            PushText("endobj\n");
        }

        // object numbers
        //   1 catalog, 2 pages, 3 info
        //   per page i: 4+3i page, 5+3i image, 6+3i content
        PushText("%PDF-1.4\n");
        Push(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' });     // marks the file as binary
        var kids = string.Join(' ', Enumerable.Range(0, pages.Count).Select(i => $"{4 + 3 * i} 0 R"));
        Obj(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Obj(2, $"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>");
        Obj(3, $"<< /Title {Text(title)} /Creator ({CardConfig.AppName}) /Producer ({CardConfig.AppName} {CardConfig.AppVersion}) /CreationDate ({Date(created)}) >>");

        for (var i = 0; i < pages.Count; i++)
        {
            var p = pages[i]();
            if (p.Rgb.Length != p.Width * p.Height * 3) throw new ArgumentException("A page's pixels do not match its size.");
            int page = 4 + 3 * i, image = 5 + 3 * i, content = 6 + 3 * i;
            string w = Num(p.WidthMm * CardConfig.MmToPt), h = Num(p.HeightMm * CardConfig.MmToPt);
            var flate = Deflate(p.Rgb);
            Obj(page, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {w} {h}] /Resources << /XObject << /Im1 {image} 0 R >> >> /Contents {content} 0 R >>");
            Obj(image, $"<< /Type /XObject /Subtype /Image /Width {p.Width} /Height {p.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length {flate.Length} >>", flate);
            var draw = Encoding.ASCII.GetBytes($"q {w} 0 0 {h} 0 0 cm /Im1 Do Q");
            Obj(content, $"<< /Length {draw.Length} >>", draw);
            onProgress?.Invoke(i + 1, pages.Count);
        }

        var count = offsets.Length;
        var xref = length;
        var table = new StringBuilder($"xref\n0 {count}\n0000000000 65535 f \n");
        for (var n = 1; n < count; n++) table.Append(offsets[n].ToString("0000000000", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        PushText(table.ToString());
        PushText($"trailer\n<< /Size {count} /Root 1 0 R /Info 3 0 R >>\nstartxref\n{xref.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");
    }
}
