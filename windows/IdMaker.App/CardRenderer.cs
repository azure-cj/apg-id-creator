// Drawing the card.
//
// Front and back are drawn into a bitmap at any pixels-per-millimetre. The
// preview asks for screen resolution and the PDF for 300 DPI, so what is on
// screen is what prints.
//
// Front, bottom to top:
//   1. the front artwork
//   2. the photo, cut to the photo window
//   3. the artwork again, cut to the band and the gold line, which puts
//      them back IN FRONT of the person's shoulders
//   4. name, job title, employee number
//   5. signature (white)
//   6. QR code

using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IdMaker.Core;

namespace IdMaker.App;

/// <summary>The two sides of the card artwork, decoded.</summary>
public sealed record CardArt(BitmapSource Front, BitmapSource Back);

/// <summary>Measures text with the real card fonts.</summary>
public sealed class WpfTextMeasurer : ITextMeasurer
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");
    private readonly Dictionary<CardFont, Typeface> _faces;
    private readonly Dictionary<(CardFont, double, string), double> _widths = new();
    private readonly Dictionary<(CardFont, double, string), (double Ascent, double Descent)> _ink = new();

    public WpfTextMeasurer(Dictionary<CardFont, Typeface> faces) { _faces = faces; }

    /// <summary>Load the three card fonts from the Fonts folder beside the program.</summary>
    public static WpfTextMeasurer Load(string fontsDirectory)
    {
        // Every typeface in the folder, with the file it came from. Asking
        // for a face by the file it lives in means its family and weight are
        // exactly the file's own, and Windows never fakes a bold out of a
        // near miss. (Asking Fonts.GetTypefaces for one file's address
        // answers with the whole folder, so the file has to be matched here.)
        var folder = new Uri(Path.GetFullPath(fontsDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
        var inFolder = new List<(string File, Typeface Face)>();
        foreach (var face in Fonts.GetTypefaces(folder))
        {
            if (face.TryGetGlyphTypeface(out var glyphs)) inFolder.Add((Path.GetFileName(glyphs.FontUri.LocalPath), face));
        }
        Typeface One(string file)
        {
            var path = Path.Combine(fontsDirectory, file);
            if (!File.Exists(path)) throw new FileNotFoundException($"The card font {file} is missing from {fontsDirectory}.", path);
            foreach (var (name, face) in inFolder)
            {
                if (string.Equals(name, file, StringComparison.OrdinalIgnoreCase)) return face;
            }
            throw new InvalidOperationException($"Windows could not read the card font {file}.");
        }
        return new WpfTextMeasurer(new Dictionary<CardFont, Typeface>
        {
            [CardFont.Name] = One("Poppins-Bold.ttf"),
            [CardFont.Title] = One("Montserrat-ExtraBold.ttf"),
            [CardFont.Number] = One("Arimo-Regular.ttf"),
        });
    }

    public FormattedText Format(CardFont font, double px, string text, Brush brush) =>
        new(text, EnUs, FlowDirection.LeftToRight, _faces[font], px, brush, null, TextFormattingMode.Ideal, 1.0);

    // One word (no spaces in it), measured as Windows shapes it.
    private double WordWidth(CardFont font, double px, string word)
    {
        var key = (font, px, word);
        if (!_widths.TryGetValue(key, out var w)) _widths[key] = w = Format(font, px, word, Brushes.Black).WidthIncludingTrailingWhitespace;
        return w;
    }

    /// <summary>
    /// A line as the words it is made of and where each starts.
    ///
    /// Text is set a word at a time, with a plain space between words.
    /// That is how the web app's canvas set it: letters are tucked together
    /// inside a word, but never across a space. Setting the whole line in
    /// one go lets Windows tuck "V." into the space after it as well, which
    /// made the employee-number line a little shorter than on every card
    /// already printed. Word by word, the widths agree with the web app's
    /// to a thousandth of a pixel in all three fonts.
    /// </summary>
    public IEnumerable<(string Word, double X)> Words(CardFont font, double px, string text)
    {
        var space = WordWidth(font, px, " ");
        double x = 0;
        var first = true;
        foreach (var word in text.Split(' '))
        {
            if (!first) x += space;
            first = false;
            if (word.Length == 0) continue;
            yield return (word, x);
            x += WordWidth(font, px, word);
        }
    }

    public double Width(CardFont font, double px, string text)
    {
        var space = WordWidth(font, px, " ");
        var words = text.Split(' ');
        double w = (words.Length - 1) * space;
        foreach (var word in words) if (word.Length > 0) w += WordWidth(font, px, word);
        return w;
    }

    // How far the ink of `sample` reaches above and below the baseline.
    //
    // Rounded up to whole pixels. The web app's canvas reported these
    // bounds that way, and the cards already printed were laid out from
    // those numbers, so rounding the same way keeps a name on the same
    // lines at the same height.
    private (double Ascent, double Descent) Ink(CardFont font, double px, string sample)
    {
        var key = (font, px, sample);
        if (_ink.TryGetValue(key, out var hit)) return hit;
        var ft = Format(font, px, sample, Brushes.Black);
        var bounds = ft.BuildGeometry(new Point(0, 0)).Bounds;
        var ink = bounds.IsEmpty
            ? (0.0, 0.0)
            : (Math.Ceiling(ft.Baseline - bounds.Top - 1e-6), Math.Max(0, Math.Ceiling(bounds.Bottom - ft.Baseline - 1e-6)));
        return _ink[key] = ink;
    }

    public double CapHeight(CardFont font, double px) => Ink(font, px, "H").Ascent;
    public double Descent(CardFont font, double px, bool uppercase) => Ink(font, px, uppercase ? "Q," : "gjpqy").Descent;
}

public sealed class CardRenderer
{
    private readonly WpfTextMeasurer _measurer;
    // a photo is converted once and kept for as long as its pixels are
    private readonly ConditionalWeakTable<RgbaImage, BitmapSource> _photos = new();
    private readonly ConditionalWeakTable<ScannedSignature, BitmapSource> _scans = new();

    public CardRenderer(WpfTextMeasurer measurer) { _measurer = measurer; }

    /* ---- pixels in and out ---------------------------------------- */

    /// <summary>Straight RGBA to the premultiplied BGRA Windows draws with.</summary>
    public static BitmapSource ToBitmap(RgbaImage img)
    {
        var bgra = new byte[img.Data.Length];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            int a = img.Data[i + 3];
            bgra[i] = (byte)(img.Data[i + 2] * a / 255);
            bgra[i + 1] = (byte)(img.Data[i + 1] * a / 255);
            bgra[i + 2] = (byte)(img.Data[i] * a / 255);
            bgra[i + 3] = (byte)a;
        }
        var bmp = BitmapSource.Create(img.Width, img.Height, 96, 96, PixelFormats.Pbgra32, null, bgra, img.Width * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Any decoded picture as straight RGBA.</summary>
    public static RgbaImage ToRgba(BitmapSource source)
    {
        BitmapSource bgra = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var data = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(data, bgra.PixelWidth * 4, 0);
        for (var i = 0; i < data.Length; i += 4) (data[i], data[i + 2]) = (data[i + 2], data[i]);
        return new RgbaImage(bgra.PixelWidth, bgra.PixelHeight, data);
    }

    /// <summary>A finished page as the opaque RGB the PDF stores, on white.</summary>
    public static PdfPage ToPdfPage(BitmapSource page, double widthMm, double heightMm)
    {
        int w = page.PixelWidth, h = page.PixelHeight;
        BitmapSource pbgra = page.Format == PixelFormats.Pbgra32 ? page : new FormatConvertedBitmap(page, PixelFormats.Pbgra32, null, 0);
        var data = new byte[w * h * 4];
        pbgra.CopyPixels(data, w * 4, 0);
        var rgb = new byte[w * h * 3];
        for (int i = 0, j = 0; i < data.Length; i += 4, j += 3)
        {
            // premultiplied over white: colour + (1 - alpha) * 255
            var rest = 255 - data[i + 3];
            rgb[j] = (byte)Math.Min(255, data[i + 2] + rest);
            rgb[j + 1] = (byte)Math.Min(255, data[i + 1] + rest);
            rgb[j + 2] = (byte)Math.Min(255, data[i] + rest);
        }
        return new PdfPage(w, h, rgb, widthMm, heightMm);
    }

    private static BitmapSource Render(int w, int h, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen()) draw(dc);
        var target = new RenderTargetBitmap(Math.Max(1, w), Math.Max(1, h), 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private static Brush Solid(string hex)
    {
        // SAFETY: ConvertFromString returns a boxed Color for any "#RRGGBB" string, and every colour here is one from CardConfig.
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static Geometry Polygons(IEnumerable<IReadOnlyList<PointMm>> polygons, double pxPerMm)
    {
        var g = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var ctx = g.Open())
        {
            foreach (var poly in polygons)
            {
                if (poly.Count < 3) continue;
                ctx.BeginFigure(new Point(poly[0].X * pxPerMm, poly[0].Y * pxPerMm), true, true);
                for (var i = 1; i < poly.Count; i++) ctx.LineTo(new Point(poly[i].X * pxPerMm, poly[i].Y * pxPerMm), true, false);
            }
        }
        g.Freeze();
        return g;
    }

    /* ---- the two sides -------------------------------------------- */

    public BitmapSource RenderFront(CardArt art, CardGeometry geo, CardData? data, double pxPerMm, bool showGuides = false)
    {
        var (w, h) = CardConfig.CardSizePx(pxPerMm);
        return Render(w, h, dc => DrawFront(dc, art, geo, data, pxPerMm, w, h, showGuides));
    }

    public BitmapSource RenderBack(CardArt art, double pxPerMm, bool rotate180)
    {
        var (w, h) = CardConfig.CardSizePx(pxPerMm);
        return Render(w, h, dc =>
        {
            if (rotate180) dc.PushTransform(new RotateTransform(180, w / 2.0, h / 2.0));
            dc.DrawImage(art.Back, new Rect(0, 0, w, h));
            if (rotate180) dc.Pop();
        });
    }

    // Core says what is on the front and in what order (CardDrawing.Front).
    // This draws each of those things with WPF, and decides nothing else.
    private void DrawFront(DrawingContext dc, CardArt art, CardGeometry geo, CardData? data, double ppm, int w, int h, bool showGuides)
    {
        var full = new Rect(0, 0, w, h);

        // the maintainer's overlay (Ctrl+Shift+G): over the artwork and the photo, under everything else
        var guidesDue = showGuides;
        void Guides()
        {
            guidesDue = false;
            var thick = Math.Max(1, ppm * 0.12);
            dc.DrawGeometry(null, new Pen(Solid("#D0167A"), thick), Polygons(new[] { geo.PhotoClip }, ppm));
            if (geo.FrontClip.Count > 0) dc.DrawGeometry(null, new Pen(Solid("#00B3FF"), thick), Polygons(geo.FrontClip, ppm));
        }

        foreach (var op in CardDrawing.Front(_measurer, geo, data, ppm))
        {
            if (guidesDue && op is not (ArtworkOp or PhotoOp)) Guides();
            switch (op)
            {
                case ArtworkOp { Clip: null }:
                {
                    dc.DrawImage(art.Front, full);
                    break;
                }
                case ArtworkOp { Clip: { } clip }:
                {
                    dc.PushClip(Polygons(clip, ppm));
                    dc.DrawImage(art.Front, full);
                    dc.Pop();
                    break;
                }
                case PhotoOp photo:
                {
                    dc.PushClip(Polygons(new[] { photo.Clip }, ppm));
                    if (photo.Fades)
                    {
                        // kept only where a white-to-clear gradient is
                        var mask = new LinearGradientBrush(Colors.White, Colors.Transparent, new Point(0, photo.FadeFrom), new Point(0, photo.FadeTo))
                        {
                            MappingMode = BrushMappingMode.Absolute,
                        };
                        mask.Freeze();
                        dc.PushOpacityMask(mask);
                    }
                    dc.DrawImage(_photos.GetValue(photo.Image, ToBitmap), new Rect(photo.X, photo.Y, photo.W, photo.H));
                    if (photo.Fades) dc.Pop();
                    dc.Pop();
                    break;
                }
                case WordOp word:
                {
                    var brush = Solid(word.Color);
                    var ft = _measurer.Format(word.Font, word.Px, word.Word, brush);
                    // Filled as outlines rather than drawn as text. Text drawn as
                    // text is nudged onto whole pixels and smoothed according to
                    // the computer's display settings, so the same card would
                    // come out slightly differently from one PC to the next.
                    // Outlines land exactly where they are put, the same everywhere.
                    dc.DrawGeometry(brush, null, ft.BuildGeometry(new Point(word.X, word.Baseline - ft.Baseline)));
                    break;
                }
                case InkOp ink:
                {
                    DrawInk(dc, ink.Segments, Solid(ink.Color), ink.Ox, ink.Oy, ink.Scale, ink.WidthScale);
                    break;
                }
                case ScanOp scan:
                {
                    // SAFETY: as in Solid(): a "#RRGGBB" string from the card map always converts to a Color.
                    var white = _scans.GetValue(scan.Scan, s => TintedScan(s, (Color)ColorConverter.ConvertFromString(scan.Color)));
                    dc.DrawImage(white, new Rect(scan.X, scan.Y, scan.W, scan.H));
                    break;
                }
                case QrOp qr:
                {
                    DrawQr(dc, qr, ppm);
                    break;
                }
            }
        }
        if (guidesDue) Guides();
    }

    /* ---- signature ------------------------------------------------ */

    /// <summary>Draw strokes: x' = ox + x * scale. Shared by the card and the pad on screen.</summary>
    public static void DrawInk(DrawingContext dc, IReadOnlyList<InkSegment> segments, Brush brush, double ox, double oy, double scale, double widthScale)
    {
        foreach (var s in segments)
        {
            if (s.Dot)
            {
                var r = s.Width * widthScale / 2;
                dc.DrawEllipse(brush, null, new Point(ox + s.X1 * scale, oy + s.Y1 * scale), r, r);
                continue;
            }
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(ox + s.X1 * scale, oy + s.Y1 * scale), false, false);
                ctx.BezierTo(
                    new Point(ox + s.C1X * scale, oy + s.C1Y * scale),
                    new Point(ox + s.C2X * scale, oy + s.C2Y * scale),
                    new Point(ox + s.X2 * scale, oy + s.Y2 * scale), true, true);
            }
            g.Freeze();
            var pen = new Pen(brush, s.Width * widthScale) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, g);
        }
    }

    /// <summary>A scan's ink as a picture in one colour.</summary>
    public static BitmapSource TintedScan(ScannedSignature scan, Color color)
    {
        var data = new byte[scan.Width * scan.Height * 4];
        for (var i = 0; i < scan.Alpha.Length; i++)
        {
            int a = scan.Alpha[i];
            data[i * 4] = (byte)(color.B * a / 255);
            data[i * 4 + 1] = (byte)(color.G * a / 255);
            data[i * 4 + 2] = (byte)(color.R * a / 255);
            data[i * 4 + 3] = (byte)a;
        }
        var bmp = BitmapSource.Create(scan.Width, scan.Height, 96, 96, PixelFormats.Pbgra32, null, data, scan.Width * 4);
        bmp.Freeze();
        return bmp;
    }

    /* ---- QR -------------------------------------------------------- */

    // Core has snapped the code to whole print pixels and found the runs of dark modules (CardDrawing).
    private static void DrawQr(DrawingContext dc, QrOp qr, double ppm)
    {
        dc.DrawRectangle(Solid(qr.Background), null, new Rect(qr.X * ppm, qr.Y * ppm, qr.Side * ppm, qr.Side * ppm));

        // one figure for the whole code: neighbouring modules share an edge exactly, so no hairlines between them
        var g = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var ctx = g.Open())
        {
            foreach (var run in qr.Runs)
            {
                double px = (qr.Left + run.Start * qr.ModuleMm) * ppm, py = (qr.Top + run.Row * qr.ModuleMm) * ppm;
                double qx = (qr.Left + (run.End + 1) * qr.ModuleMm) * ppm, qy = (qr.Top + (run.Row + 1) * qr.ModuleMm) * ppm;
                ctx.BeginFigure(new Point(px, py), true, true);
                ctx.LineTo(new Point(qx, py), false, false);
                ctx.LineTo(new Point(qx, qy), false, false);
                ctx.LineTo(new Point(px, qy), false, false);
            }
        }
        g.Freeze();
        dc.DrawGeometry(Solid(qr.Color), null, g);
    }

    /* ---- sheets ---------------------------------------------------- */

    /// <summary>
    /// One sheet of paper. `front` and `back` draw a card side for an entry;
    /// each is asked for once per slot and let go straight after, so a sheet
    /// of twenty-one cards never holds twenty-one 300 DPI bitmaps.
    /// </summary>
    public BitmapSource RenderSheet(SheetPlan plan, SheetPage page, double ppm, Func<int, BitmapSource> front, Func<int, BitmapSource> back)
    {
        var w = (int)Math.Round(plan.PageWidthMm * ppm, MidpointRounding.AwayFromZero);
        var h = (int)Math.Round(plan.PageHeightMm * ppm, MidpointRounding.AwayFromZero);
        return Render(w, h, dc =>
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
            var marks = new Pen(Brushes.Black, Math.Max(1, 0.1 * ppm));
            marks.Freeze();
            foreach (var slot in page.Slots)
            {
                var card = slot.Back ? back(slot.Entry) : front(slot.Entry);
                dc.PushTransform(new TranslateTransform(slot.X * ppm, slot.Y * ppm));
                if (plan.Turned)
                {
                    // the card is drawn upright, then turned into a landscape slot
                    dc.PushTransform(new TranslateTransform(plan.CardW * ppm, 0));
                    dc.PushTransform(new RotateTransform(90));
                }
                dc.DrawImage(card, new Rect(0, 0, CardConfig.CardWidthMm * ppm, CardConfig.CardHeightMm * ppm));
                if (plan.Turned) { dc.Pop(); dc.Pop(); }
                dc.Pop();
                foreach (var (x1, y1, x2, y2) in Sheets.CropMarks(slot.X, slot.Y, plan.CardW, plan.CardH))
                    dc.DrawLine(marks, new Point(x1 * ppm, y1 * ppm), new Point(x2 * ppm, y2 * ppm));
            }
        });
    }

    /// <summary>
    /// The pages of a print as PDF page thunks: each is rendered at 300 DPI
    /// when the PDF writer asks for it.
    /// </summary>
    public List<Func<PdfPage>> SheetPdfPages(CardArt art, CardGeometry geo, SheetPlan plan, IReadOnlyList<CardData> entries,
        IReadOnlyList<SheetPage> pages, bool backUpsideDown)
    {
        var ppm = CardConfig.PrintPxPerMm;
        var result = new List<Func<PdfPage>>();
        foreach (var page in pages)
        {
            result.Add(() => ToPdfPage(
                RenderSheet(plan, page, ppm, i => RenderFront(art, geo, entries[i], ppm), _ => RenderBack(art, ppm, backUpsideDown)),
                plan.PageWidthMm, plan.PageHeightMm));
        }
        return result;
    }

    /// <summary>One card per page, front then back, each page the size of the card.</summary>
    public List<Func<PdfPage>> CardPdfPages(CardArt art, CardGeometry geo, IReadOnlyList<CardData> entries, bool rotateBack)
    {
        var ppm = CardConfig.PrintPxPerMm;
        var result = new List<Func<PdfPage>>();
        foreach (var data in entries)
        {
            result.Add(() => ToPdfPage(RenderFront(art, geo, data, ppm), CardConfig.CardWidthMm, CardConfig.CardHeightMm));
            result.Add(() => ToPdfPage(RenderBack(art, ppm, rotateBack), CardConfig.CardWidthMm, CardConfig.CardHeightMm));
        }
        return result;
    }
}

/// <summary>Opening and saving picture files with Windows' own codecs.</summary>
public static class ImageFiles
{
    // 120 megapixels: several times the largest phone camera, and about half a gigabyte unpacked
    private const long MaxPixels = 120_000_000;

    /// <summary>What went wrong the last time a picture would not open, for whoever is diagnosing it.</summary>
    internal static string LastFault { get; private set; } = "";

    /// <summary>Open a picture, honouring the way a phone says it was held. The pixels, or why not.</summary>
    public static (RgbaImage? Image, string Error) Decode(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            // Not cached on load: the size is read from the header first, so a
            // small file that unpacks into an enormous picture is turned away
            // before any of it is unpacked.
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
            BitmapSource frame = decoder.Frames[0];
            if ((long)frame.PixelWidth * frame.PixelHeight > MaxPixels)
            {
                return (null, $"This picture is {frame.PixelWidth} × {frame.PixelHeight} pixels, which is more than this app can open. Save a smaller copy and use that.");
            }
            // only a camera's JPEG carries a "which way up" note; asking a PNG for one is an error, not an empty answer
            var turn = decoder is JpegBitmapDecoder ? Orientation(frame) : Rotation.Rotate0;
            if (turn != Rotation.Rotate0)
            {
                var rotated = new TransformedBitmap(frame, new RotateTransform(turn switch { Rotation.Rotate90 => 90, Rotation.Rotate180 => 180, _ => 270 }));
                frame = rotated;
            }
            return (CardRenderer.ToRgba(frame), "");
        }
        catch (Exception e) when (e is NotSupportedException or FileFormatException or IOException or UnauthorizedAccessException
                                  or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException or OverflowException)
        {
            LastFault = $"{e.GetType().Name}: {e.Message}";
            var webp = path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
            return (null, webp
                ? "This WebP picture couldn’t be opened on this computer. Save it as PNG or JPG and try again."
                : "This file couldn’t be opened as a picture. Try saving it again as PNG or JPG.");
        }
    }

    // A phone stores a portrait photo sideways and notes which way up it
    // goes (EXIF tag 274). Ignoring the note lays the person on their side.
    private static Rotation Orientation(BitmapSource frame)
    {
        try
        {
            if (frame.Metadata is not BitmapMetadata meta) return Rotation.Rotate0;
            var value = meta.GetQuery("/app1/ifd/{ushort=274}") ?? meta.GetQuery("/ifd/{ushort=274}");
            return value is ushort o
                ? o switch { 3 => Rotation.Rotate180, 6 => Rotation.Rotate90, 8 => Rotation.Rotate270, _ => Rotation.Rotate0 }
                : Rotation.Rotate0;
        }
        catch (Exception e) when (e is NotSupportedException or InvalidOperationException or ArgumentException or IOException
                                  or System.Runtime.InteropServices.COMException)
        {
            return Rotation.Rotate0;         // no orientation that can be read is no orientation
        }
    }

    public static BitmapSource Load(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    public static void SavePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>A picture drawn at exactly this many pixels, for artwork stored at the printing size.</summary>
    public static BitmapSource Resize(BitmapSource source, int width, int height)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen()) dc.DrawImage(source, new Rect(0, 0, width, height));
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }
}
