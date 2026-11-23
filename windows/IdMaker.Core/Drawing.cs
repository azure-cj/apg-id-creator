// The front of a card as an ordered list of things to draw.
//
// Both renderers (WPF's and Core Graphics') used to carry the same recipe:
// artwork, the photo inside its window, the artwork's band again over the
// photo, three lines of text set word by word, the signature, the QR code.
// The recipe is here once. A renderer goes down the list and draws each
// operation with its own toolkit; it decides nothing about what goes where
// or in what order.
//
// Units: the card's millimetres wherever the renderers always multiplied by
// the resolution themselves (clips, the QR code), and pixels of the picture
// being drawn where the position only exists in pixels (text is fitted and
// its ink rounded to whole pixels by TextFit.Place; ink strokes and
// pictures are placed with the same expressions the renderers used). The
// arithmetic is the renderers' own, moved, so the printed card does not
// move: IdMaker.App.Tests and the Mac self-test hold it to
// test/golden-front.png.

namespace IdMaker.Core;

/// <summary>One thing to draw on the front of the card.</summary>
public abstract record DrawOp;

/// <summary>The front artwork over the whole card; inside Clip (millimetres) when there is one.</summary>
public sealed record ArtworkOp(IReadOnlyList<IReadOnlyList<PointMm>>? Clip) : DrawOp;

/// <summary>
/// The photo in its rectangle (pixels), cut to Clip (millimetres). When
/// Fades, it goes from solid at FadeFrom to nothing at FadeTo (pixels down
/// the picture): a photo too closely cropped to reach the bottom of its
/// window is faded out rather than cut off square.
/// </summary>
public sealed record PhotoOp(
    RgbaImage Image, double X, double Y, double W, double H, IReadOnlyList<PointMm> Clip, bool Fades, double FadeFrom, double FadeTo) : DrawOp;

/// <summary>One word, filled as outlines, its baseline starting at (X, Baseline) in pixels.</summary>
public sealed record WordOp(CardFont Font, double Px, string Word, double X, double Baseline, string Color) : DrawOp;

/// <summary>A drawn signature: each stroke at x' = Ox + x * Scale, its width times WidthScale (pixels).</summary>
public sealed record InkOp(IReadOnlyList<InkSegment> Segments, string Color, double Ox, double Oy, double Scale, double WidthScale) : DrawOp;

/// <summary>A scanned signature's ink in one colour, in its rectangle (pixels).</summary>
public sealed record ScanOp(ScannedSignature Scan, string Color, double X, double Y, double W, double H) : DrawOp;

/// <summary>A run of dark modules along one row of the QR code: columns Start to End.</summary>
public readonly record struct QrRun(int Row, int Start, int End);

/// <summary>
/// The QR code, in millimetres: a square of Background at (X, Y), and over
/// it the dark runs, each module ModuleMm across, the code's corner at
/// (Left, Top). Everything is already snapped to whole print pixels.
/// </summary>
public sealed record QrOp(
    string Background, double X, double Y, double Side, string Color, double Left, double Top, double ModuleMm, IReadOnlyList<QrRun> Runs) : DrawOp;

public static class CardDrawing
{
    private static readonly object QrGate = new();
    private static string _qrKey = "";
    private static QrCode? _qr;

    // the code is the same on every card until the map changes what it says: encoded once and kept
    private static QrCode Code(QrSpec spec)
    {
        var key = spec.Content + "|" + spec.ErrorCorrection;
        lock (QrGate)
        {
            if (_qr is null || _qrKey != key) { _qr = QrCode.Encode(spec.Content, spec.ErrorCorrection); _qrKey = key; }
            return _qr;
        }
    }

    /// <summary>
    /// A line as the words it is made of and where each starts. Text is set
    /// a word at a time with a plain space between words, as the web app's
    /// canvas set it: letters are tucked together inside a word, never
    /// across a space.
    /// </summary>
    public static IEnumerable<(string Word, double X)> Words(ITextMeasurer m, CardFont font, double px, string text)
    {
        var space = m.Width(font, px, " ");
        double x = 0;
        var first = true;
        foreach (var word in text.Split(' '))
        {
            if (!first) x += space;
            first = false;
            if (word.Length == 0) continue;
            yield return (word, x);
            x += m.Width(font, px, word);
        }
    }

    /// <summary>
    /// Everything on the front of the card, in the order it is drawn. With
    /// no data it is the artwork alone.
    /// </summary>
    /// <param name="ppm">Pixels per millimetre of the picture being drawn.</param>
    public static IReadOnlyList<DrawOp> Front(ITextMeasurer m, CardGeometry geo, CardData? data, double ppm)
    {
        var ops = new List<DrawOp> { new ArtworkOp(null) };

        if (data is { Photo: not null, Placement: not null })
        {
            var p = data.Placement;
            var fades = p.ShortBy > 0;
            var fade = Math.Min(CardConfig.Photo.FadeMm, p.H / 3);
            var bottom = (p.Y + p.H) * ppm;
            ops.Add(new PhotoOp(data.Photo, p.X * ppm, p.Y * ppm, p.W * ppm, p.H * ppm, geo.PhotoClip, fades, bottom - fade * ppm, bottom));
        }

        // the band and the gold line, back on top of the photo
        if (geo.FrontClip.Count > 0) ops.Add(new ArtworkOp(geo.FrontClip));

        if (data is null) return ops;

        var layout = TextFit.Layout(m, geo, data.Name, data.Title, data.Year, data.Seq);
        Text(ops, m, geo.Name, layout.Name, ppm);
        Text(ops, m, geo.Title, layout.Title, ppm);
        Text(ops, m, geo.EmployeeNo, layout.EmployeeNo, ppm);

        if (data.Signature is { HasInk: true }) Sign(ops, data.Signature, geo.Signature, ppm);
        ops.Add(Qr(geo.Qr));
        return ops;
    }

    private static void Text(List<DrawOp> ops, ITextMeasurer m, TextSpec spec, FittedText fit, double ppm)
    {
        if (fit.Lines.Count == 0) return;
        var place = TextFit.Place(m, spec, fit, ppm);
        for (var i = 0; i < fit.Lines.Count; i++)
        {
            var width = m.Width(spec.Font, place.Px, fit.Lines[i]);
            var x = spec.Align switch
            {
                HAlign.Center => place.AnchorX - width / 2,
                HAlign.Right => place.AnchorX - width,
                _ => place.AnchorX,
            };
            foreach (var (word, offset) in Words(m, spec.Font, place.Px, fit.Lines[i]))
                ops.Add(new WordOp(spec.Font, place.Px, word, x + offset, place.Baselines[i], spec.Color));
        }
    }

    private static void Sign(List<DrawOp> ops, Signature signature, SignatureSpec spec, double ppm)
    {
        if (signature is ScannedSignature scan)
        {
            var fit = SignatureInk.FitInto(spec, 0, 0, scan.Width, scan.Height);
            ops.Add(new ScanOp(scan, spec.Color, fit.Left * ppm, fit.Top * ppm, scan.Width * fit.Scale * ppm, scan.Height * fit.Scale * ppm));
            return;
        }
        if (signature is not DrawnSignature drawn) return;
        var (minX, minY, inkW, inkH) = SignatureInk.Bounds(drawn);
        var f = SignatureInk.FitInto(spec, minX, minY, inkW, inkH);
        // Widths are asked for in pad units that come out as StrokeMm on the
        // card: a width of v mm is v / Scale pad units.
        var segments = SignatureInk.Segments(drawn, spec.MinStrokeMm / f.Scale, spec.StrokeMm / f.Scale);
        var scale = f.Scale * ppm;
        ops.Add(new InkOp(segments, spec.Color, (f.Left - minX * f.Scale) * ppm, (f.Top - minY * f.Scale) * ppm, scale, scale));
    }

    private static QrOp Qr(QrSpec spec)
    {
        var code = Code(spec);
        var n = code.Size;

        // Everything is snapped to whole PRINT pixels, so the code stays
        // crisp at 300 DPI whatever resolution it is being drawn at.
        var printPpm = CardConfig.PrintPxPerMm;
        double Snap(double mm) => Math.Floor(mm * printPpm + 0.5) / printPpm;
        double x0 = Snap(spec.X), y0 = Snap(spec.Y), side = Snap(spec.Size);

        // module size: whole print pixels, leaving a two-module quiet zone each side
        var moduleMm = Math.Max(1, Math.Floor(side * printPpm / (n + 4))) / printPpm;
        var codeMm = n * moduleMm;
        double left = Snap(x0 + (side - codeMm) / 2), top = Snap(y0 + (side - codeMm) / 2);

        var runs = new List<QrRun>();
        for (var r = 0; r < n; r++)
        {
            for (var c = 0; c < n; c++)
            {
                if (!code.IsDark(r, c)) continue;
                // a run of dark modules along the row is one rectangle
                var end = c;
                while (end + 1 < n && code.IsDark(r, end + 1)) end++;
                runs.Add(new QrRun(r, c, end));
                c = end;
            }
        }
        return new QrOp(spec.Background, x0, y0, side, spec.Color, left, top, moduleMm, runs);
    }
}
