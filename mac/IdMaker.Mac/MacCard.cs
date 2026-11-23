// Drawing the card on a Mac, with Core Graphics.
//
// The Mac counterpart of the Windows app's CardRenderer, layer for layer:
// artwork, photo cut to its window, the band and gold line back on top,
// name, title and number as outlines, signature, QR. The card is drawn into
// plain pixels at any pixels-per-millimetre, so the preview and the PDF are
// the same drawing, and it is held to the same reference card as Windows'
// (`test/golden-front.png`).

using System.Runtime.CompilerServices;
using CoreGraphics;
using IdMaker.Core;
using ImageIO;

namespace IdMaker.Mac;

/// <summary>The two sides of the card artwork, decoded.</summary>
public sealed record CardArt(CGImage Front, CGImage Back);

/// <summary>Pictures in and out of plain RGBA, with the Mac's own codecs.</summary>
public static class MacPictures
{
    internal static nfloat F(double value) => (nfloat)value;

    internal static readonly CGColorSpace Srgb = CGColorSpace.CreateSrgb() ?? throw new InvalidOperationException("This Mac has no sRGB colour space.");

    /// <summary>Open a picture file as the Mac decodes it. Null when it is not a picture the Mac can read.</summary>
    public static CGImage? Load(string path)
    {
        using var source = CGImageSource.FromUrl(NSUrl.FromFilename(path));
        return source is null || source.ImageCount < 1 ? null : source.CreateImage(0, new CGImageOptions());
    }

    /// <summary>Straight RGBA as a picture the Mac can draw.</summary>
    public static CGImage ToImage(RgbaImage image)
    {
        var data = new byte[image.Data.Length];
        for (var i = 0; i < data.Length; i += 4)
        {
            int a = image.Data[i + 3];
            data[i] = (byte)(image.Data[i] * a / 255);
            data[i + 1] = (byte)(image.Data[i + 1] * a / 255);
            data[i + 2] = (byte)(image.Data[i + 2] * a / 255);
            data[i + 3] = (byte)a;
        }
        using var provider = new CGDataProvider(data);
        return new CGImage(image.Width, image.Height, 8, 32, image.Width * 4, Srgb, CGBitmapFlags.PremultipliedLast, provider, null, true, CGColorRenderingIntent.Default);
    }

    /// <summary>
    /// A drawing surface of this many pixels whose y grows downwards, as the
    /// card's millimetres do, and the bytes it draws into (premultiplied RGBA).
    /// </summary>
    internal static (CGBitmapContext Context, byte[] Pixels) Surface(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        var context = new CGBitmapContext(pixels, width, height, 8, width * 4, Srgb, CGImageAlphaInfo.PremultipliedLast);
        context.InterpolationQuality = CGInterpolationQuality.High;
        context.TranslateCTM(0, height);
        context.ScaleCTM(1, -1);
        return (context, pixels);
    }

    /// <summary>A picture, the right way up, in a surface whose y grows downwards.</summary>
    internal static void Draw(CGContext context, CGImage image, CGRect where)
    {
        context.SaveState();
        context.TranslateCTM(where.X, where.Y + where.Height);
        context.ScaleCTM(1, -1);
        context.DrawImage(new CGRect(0, 0, where.Width, where.Height), image);
        context.RestoreState();
    }

    /// <summary>Premultiplied pixels back to straight RGBA.</summary>
    internal static RgbaImage ToRgba(byte[] premultiplied, int width, int height)
    {
        var data = new byte[premultiplied.Length];
        for (var i = 0; i < data.Length; i += 4)
        {
            int a = premultiplied[i + 3];
            if (a == 0) continue;
            data[i] = (byte)Math.Min(255, premultiplied[i] * 255 / a);
            data[i + 1] = (byte)Math.Min(255, premultiplied[i + 1] * 255 / a);
            data[i + 2] = (byte)Math.Min(255, premultiplied[i + 2] * 255 / a);
            data[i + 3] = (byte)a;
        }
        return new RgbaImage(width, height, data);
    }

    /// <summary>Any decoded picture as straight RGBA.</summary>
    public static RgbaImage ToRgba(CGImage image)
    {
        int width = (int)image.Width, height = (int)image.Height;
        var (context, pixels) = Surface(width, height);
        using (context) Draw(context, image, new CGRect(0, 0, width, height));
        return ToRgba(pixels, width, height);
    }

    // 12,000 pixels on the longer side: several times the largest phone camera, and what bounds the memory a picture can take.
    private const int MaxSidePixels = 12_000;

    /// <summary>
    /// Open a picture file as straight RGBA, honouring the way a phone says
    /// it was held, or say why not.
    /// </summary>
    public static (RgbaImage? Image, string Error) Decode(string path)
    {
        const string cannot = "This file couldn’t be opened as a picture. Try saving it again as PNG or JPG.";
        using var source = CGImageSource.FromUrl(NSUrl.FromFilename(path));
        if (source is null || source.ImageCount < 1) return (null, cannot);
        // Asked for as a "thumbnail" only because that is the request that turns the picture the way it was held.
        using var image = source.CreateThumbnail(0, new CGImageThumbnailOptions
        {
            CreateThumbnailFromImageAlways = true,
            CreateThumbnailWithTransform = true,
            MaxPixelSize = MaxSidePixels,
        });
        return image is null ? (null, cannot) : (ToRgba(image), "");
    }

    /// <summary>A picture at another size, as artwork is stored at the printing size.</summary>
    public static CGImage Resize(CGImage image, int width, int height)
    {
        var (context, pixels) = Surface(width, height);
        using (context) Draw(context, image, new CGRect(0, 0, width, height));
        return ToImage(ToRgba(pixels, width, height));
    }

    public static void SavePng(RgbaImage image, string path)
    {
        using var picture = ToImage(image);
        using var destination = CGImageDestination.Create(NSUrl.FromFilename(path), "public.png", 1)
            ?? throw new IOException($"The picture could not be saved to {path}.");
        destination.AddImage(picture);
        if (!destination.Close()) throw new IOException($"The picture could not be saved to {path}.");
    }
}

public sealed class MacCardRenderer
{
    private readonly MacTextMeasurer _measurer;
    // a photo is converted once and kept for as long as its pixels are
    private readonly ConditionalWeakTable<RgbaImage, CGImage> _photos = new();

    public MacCardRenderer(MacTextMeasurer measurer) { _measurer = measurer; }

    private static nfloat F(double value) => MacPictures.F(value);

    private static CGColor Solid(string hex)
    {
        // "#RRGGBB", as every colour in CardConfig is written.
        double Part(int at) => Convert.ToInt32(hex.Substring(at, 2), 16) / 255.0;
        return new CGColor(F(Part(1)), F(Part(3)), F(Part(5)), 1);
    }

    private static CGPath Polygons(IEnumerable<IReadOnlyList<PointMm>> polygons, double pxPerMm)
    {
        var path = new CGPath();
        foreach (var polygon in polygons)
        {
            if (polygon.Count < 3) continue;
            path.MoveToPoint(F(polygon[0].X * pxPerMm), F(polygon[0].Y * pxPerMm));
            for (var i = 1; i < polygon.Count; i++) path.AddLineToPoint(F(polygon[i].X * pxPerMm), F(polygon[i].Y * pxPerMm));
            path.CloseSubpath();
        }
        return path;
    }

    private static RgbaImage Render(int width, int height, Action<CGContext> draw)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var (context, pixels) = MacPictures.Surface(width, height);
        using (context) draw(context);
        return MacPictures.ToRgba(pixels, width, height);
    }

    /* ---- the two sides -------------------------------------------- */

    public RgbaImage RenderFront(CardArt art, CardGeometry geo, CardData? data, double pxPerMm)
    {
        var (w, h) = CardConfig.CardSizePx(pxPerMm);
        return Render(w, h, context => DrawFront(context, art, geo, data, pxPerMm, w, h));
    }

    public RgbaImage RenderBack(CardArt art, double pxPerMm, bool rotate180)
    {
        var (w, h) = CardConfig.CardSizePx(pxPerMm);
        return Render(w, h, context =>
        {
            if (rotate180)
            {
                context.TranslateCTM(w, h);
                context.RotateCTM(F(Math.PI));
            }
            MacPictures.Draw(context, art.Back, new CGRect(0, 0, w, h));
        });
    }

    // Core says what is on the front and in what order (CardDrawing.Front).
    // This draws each of those things with Core Graphics, and decides nothing else.
    private void DrawFront(CGContext context, CardArt art, CardGeometry geo, CardData? data, double ppm, int w, int h)
    {
        var full = new CGRect(0, 0, w, h);
        foreach (var op in CardDrawing.Front(_measurer, geo, data, ppm))
        {
            switch (op)
            {
                case ArtworkOp { Clip: null }:
                {
                    MacPictures.Draw(context, art.Front, full);
                    break;
                }
                case ArtworkOp { Clip: { } clip }:
                {
                    context.SaveState();
                    context.AddPath(Polygons(clip, ppm));
                    context.Clip();
                    MacPictures.Draw(context, art.Front, full);
                    context.RestoreState();
                    break;
                }
                case PhotoOp photo:
                {
                    context.SaveState();
                    context.AddPath(Polygons(new[] { photo.Clip }, ppm));
                    context.Clip();
                    var picture = _photos.GetValue(photo.Image, MacPictures.ToImage);
                    var where = new CGRect(photo.X, photo.Y, photo.W, photo.H);
                    if (photo.Fades)
                    {
                        // Drawn on its own layer, then kept only where a white-to-clear gradient is.
                        context.BeginTransparencyLayer();
                        MacPictures.Draw(context, picture, where);
                        context.SetBlendMode(CGBlendMode.DestinationIn);
                        using var gradient = new CGGradient(MacPictures.Srgb, new[] { new CGColor(1, 1, 1, 1), new CGColor(1, 1, 1, 0) }, new nfloat[] { 0, 1 });
                        context.DrawLinearGradient(gradient, new CGPoint(0, photo.FadeFrom), new CGPoint(0, photo.FadeTo),
                            CGGradientDrawingOptions.DrawsBeforeStartLocation | CGGradientDrawingOptions.DrawsAfterEndLocation);
                        context.EndTransparencyLayer();
                    }
                    else
                    {
                        MacPictures.Draw(context, picture, where);
                    }
                    context.RestoreState();
                    break;
                }
                case WordOp word:
                {
                    // Filled as outlines, not drawn as text: outlines land exactly
                    // where they are put, the same on every computer.
                    context.SetFillColor(Solid(word.Color));
                    using var outline = _measurer.Outline(word.Font, word.Px, word.Word, word.X, word.Baseline);
                    context.AddPath(outline);
                    context.FillPath();
                    break;
                }
                case InkOp ink:
                {
                    DrawInk(context, ink.Segments, Solid(ink.Color), ink.Ox, ink.Oy, ink.Scale, ink.WidthScale);
                    break;
                }
                case ScanOp scan:
                {
                    using var white = TintedScan(scan.Scan, scan.Color);
                    MacPictures.Draw(context, white, new CGRect(scan.X, scan.Y, scan.W, scan.H));
                    break;
                }
                case QrOp qr:
                {
                    DrawQr(context, qr, ppm);
                    break;
                }
            }
        }
    }

    /* ---- signature ------------------------------------------------ */

    /// <summary>Draw strokes: x' = ox + x * scale. Shared by the card and the pad on screen.</summary>
    public static void DrawInk(CGContext context, IReadOnlyList<InkSegment> segments, CGColor color, double ox, double oy, double scale, double widthScale)
    {
        context.SetFillColor(color);
        context.SetStrokeColor(color);
        context.SetLineCap(CGLineCap.Round);
        context.SetLineJoin(CGLineJoin.Round);
        foreach (var s in segments)
        {
            if (s.Dot)
            {
                var r = s.Width * widthScale / 2;
                context.FillEllipseInRect(new CGRect(ox + s.X1 * scale - r, oy + s.Y1 * scale - r, r * 2, r * 2));
                continue;
            }
            context.SetLineWidth(F(s.Width * widthScale));
            context.MoveTo(F(ox + s.X1 * scale), F(oy + s.Y1 * scale));
            context.AddCurveToPoint(
                F(ox + s.C1X * scale), F(oy + s.C1Y * scale),
                F(ox + s.C2X * scale), F(oy + s.C2Y * scale),
                F(ox + s.X2 * scale), F(oy + s.Y2 * scale));
            context.StrokePath();
        }
    }

    /// <summary>A scan's ink as a picture in one colour.</summary>
    public static CGImage TintedScan(ScannedSignature scan, string hex)
    {
        int Part(int at) => Convert.ToInt32(hex.Substring(at, 2), 16);
        var data = new byte[scan.Width * scan.Height * 4];
        for (var i = 0; i < scan.Alpha.Length; i++)
        {
            data[i * 4] = (byte)Part(1);
            data[i * 4 + 1] = (byte)Part(3);
            data[i * 4 + 2] = (byte)Part(5);
            data[i * 4 + 3] = scan.Alpha[i];
        }
        return MacPictures.ToImage(new RgbaImage(scan.Width, scan.Height, data));
    }

    /* ---- QR -------------------------------------------------------- */

    // Core has snapped the code to whole print pixels and found the runs of dark modules (CardDrawing).
    private static void DrawQr(CGContext context, QrOp qr, double ppm)
    {
        context.SetShouldAntialias(false);
        context.SetFillColor(Solid(qr.Background));
        context.FillRect(new CGRect(qr.X * ppm, qr.Y * ppm, qr.Side * ppm, qr.Side * ppm));

        // one path for the whole code: neighbouring modules share an edge exactly, so no hairlines between them
        var code = new CGPath();
        foreach (var run in qr.Runs)
        {
            code.AddRect(new CGRect((qr.Left + run.Start * qr.ModuleMm) * ppm, (qr.Top + run.Row * qr.ModuleMm) * ppm, (run.End + 1 - run.Start) * qr.ModuleMm * ppm, qr.ModuleMm * ppm));
        }
        context.SetFillColor(Solid(qr.Color));
        context.AddPath(code);
        context.FillPath();
        context.SetShouldAntialias(true);
    }

    /* ---- sheets ---------------------------------------------------- */

    /// <summary>
    /// One sheet of paper. `front` and `back` draw a card side for an entry;
    /// each is asked for once per slot and let go straight after, so a sheet
    /// of twenty-one cards never holds twenty-one 300 DPI pictures.
    /// </summary>
    public RgbaImage RenderSheet(SheetPlan plan, SheetPage page, double ppm, Func<int, RgbaImage> front, Func<int, RgbaImage> back)
    {
        var w = (int)Math.Round(plan.PageWidthMm * ppm, MidpointRounding.AwayFromZero);
        var h = (int)Math.Round(plan.PageHeightMm * ppm, MidpointRounding.AwayFromZero);
        return Render(w, h, context =>
        {
            context.SetFillColor(new CGColor(1, 1, 1, 1));
            context.FillRect(new CGRect(0, 0, w, h));
            foreach (var slot in page.Slots)
            {
                using var card = MacPictures.ToImage(slot.Back ? back(slot.Entry) : front(slot.Entry));
                context.SaveState();
                context.TranslateCTM(F(slot.X * ppm), F(slot.Y * ppm));
                if (plan.Turned)
                {
                    // the card is drawn upright, then turned into a landscape slot
                    context.TranslateCTM(F(plan.CardW * ppm), 0);
                    context.RotateCTM(F(Math.PI / 2));
                }
                MacPictures.Draw(context, card, new CGRect(0, 0, CardConfig.CardWidthMm * ppm, CardConfig.CardHeightMm * ppm));
                context.RestoreState();
                context.SetStrokeColor(new CGColor(0, 0, 0, 1));
                context.SetLineWidth(F(Math.Max(1, 0.1 * ppm)));
                foreach (var (x1, y1, x2, y2) in Sheets.CropMarks(slot.X, slot.Y, plan.CardW, plan.CardH))
                {
                    context.MoveTo(F(x1 * ppm), F(y1 * ppm));
                    context.AddLineToPoint(F(x2 * ppm), F(y2 * ppm));
                    context.StrokePath();
                }
            }
        });
    }

    /// <summary>A finished page as the opaque RGB the PDF stores, on white.</summary>
    public static PdfPage ToPdfPage(RgbaImage page, double widthMm, double heightMm)
    {
        var rgb = new byte[page.Width * page.Height * 3];
        for (int i = 0, j = 0; i < page.Data.Length; i += 4, j += 3)
        {
            // straight colour over white: colour * alpha + (1 - alpha) * 255
            int a = page.Data[i + 3];
            rgb[j] = (byte)((page.Data[i] * a + 255 * (255 - a)) / 255);
            rgb[j + 1] = (byte)((page.Data[i + 1] * a + 255 * (255 - a)) / 255);
            rgb[j + 2] = (byte)((page.Data[i + 2] * a + 255 * (255 - a)) / 255);
        }
        return new PdfPage(page.Width, page.Height, rgb, widthMm, heightMm);
    }

    /// <summary>The pages of a print as PDF page thunks: each is drawn at 300 DPI when the PDF writer asks for it.</summary>
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
