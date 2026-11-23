// Signatures: what one is, and how it is turned into ink.
//
// A signature is either drawn — strokes of points in the pad's own units —
// or scanned, a trimmed mask holding the ink as alpha. Nothing about the
// card is baked in, so the same signature can be fitted into the box on the
// card at any resolution. Both print in white.
//
// A point may carry pen pressure (0..1) when it came from a tablet. Then the
// line's width follows how hard the pen was pressed, which is how a pen on
// paper behaves. A mouse or a finger has no pressure, and for those the
// width follows speed instead: slow is thick, fast is thin.

namespace IdMaker.Core;

/// <summary>One sample of the pen. Pressure is 0..1, or negative when the device does not report it.</summary>
public readonly record struct InkPoint(double X, double Y, double Pressure = -1)
{
    public bool HasPressure => Pressure >= 0;
}

public abstract record Signature
{
    public abstract bool HasInk { get; }
}

public sealed record DrawnSignature(IReadOnlyList<IReadOnlyList<InkPoint>> Strokes) : Signature
{
    public override bool HasInk => Strokes.Any(s => s.Count > 0);
}

/// <summary>A scan: Width x Height alpha values, trimmed to the ink.</summary>
public sealed record ScannedSignature(int Width, int Height, byte[] Alpha, string FileName) : Signature
{
    public override bool HasInk => Width > 0 && Height > 0;
}

/// <summary>One piece of ink ready to draw, in the signature's own units.</summary>
public readonly record struct InkSegment(
    double X1, double Y1, double C1X, double C1Y, double C2X, double C2Y, double X2, double Y2, double Width, bool Dot);

/// <summary>How a signature's own units map into a box: x' = Left + (x - MinX) * Scale.</summary>
public readonly record struct InkFit(double Left, double Top, double MinX, double MinY, double Scale);

public static class SignatureInk
{
    public const double PadAspect = 2.6;          // the pad's width / height

    // Drop near-duplicate points (under half a unit apart) and run two
    // passes of a 1-2-1 smoothing kernel, so hand jitter does not print as wobble.
    public static List<InkPoint> Smooth(IReadOnlyList<InkPoint> points)
    {
        var kept = new List<InkPoint>();
        foreach (var p in points)
        {
            if (kept.Count == 0 || Dist(p, kept[^1]) >= 0.5) kept.Add(p);
            // a harder press on the same spot is still information
            else if (p.Pressure > kept[^1].Pressure) kept[^1] = kept[^1] with { Pressure = p.Pressure };
        }
        if (kept.Count < 3) return kept;
        var pts = kept;
        for (var pass = 0; pass < 2; pass++)
        {
            var next = new List<InkPoint>(pts.Count);
            for (var i = 0; i < pts.Count; i++)
            {
                if (i == 0 || i == pts.Count - 1) { next.Add(pts[i]); continue; }
                InkPoint a = pts[i - 1], p = pts[i], b = pts[i + 1];
                var pressure = a.HasPressure && p.HasPressure && b.HasPressure ? (a.Pressure + 2 * p.Pressure + b.Pressure) / 4 : p.Pressure;
                next.Add(new InkPoint((a.X + 2 * p.X + b.X) / 4, (a.Y + 2 * p.Y + b.Y) / 4, pressure));
            }
            pts = next;
        }
        return pts;
    }

    private static double Dist(InkPoint a, InkPoint b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // Median distance between consecutive raw points: a feel for how fast
    // this person signs, used to turn pen speed into stroke width.
    private static double MedianStep(DrawnSignature sig)
    {
        var d = new List<double>();
        foreach (var s in sig.Strokes) for (var i = 1; i < s.Count; i++) d.Add(Dist(s[i], s[i - 1]));
        if (d.Count == 0) return 1;
        d.Sort();
        return Math.Max(0.5, d[d.Count / 2]);
    }

    // Per-point width, low-pass filtered so it never jumps.
    private static double[] Widths(List<InkPoint> points, double median, double minW, double maxW)
    {
        var result = new double[points.Count];
        var w = maxW * 0.85;
        var byPressure = points.All(p => p.HasPressure);
        for (var i = 0; i < points.Count; i++)
        {
            double target;
            if (byPressure)
            {
                // a light touch still leaves a line; the curve keeps ordinary writing pressure in the middle of the range
                target = minW + (maxW - minW) * Math.Pow(Math.Clamp(points[i].Pressure, 0, 1), 0.7);
            }
            else
            {
                InkPoint a = points[Math.Max(0, i - 1)], b = points[Math.Min(points.Count - 1, i + 1)];
                var speed = Dist(a, b) / (i == 0 || i == points.Count - 1 ? 1 : 2);
                var t = Math.Min(1, speed / (median * 2.2));
                target = maxW - (maxW - minW) * t;
            }
            w = w * 0.75 + target * 0.25;
            result[i] = w;
        }
        return result;
    }

    /// <summary>
    /// The strokes as Catmull-Rom style curves, each with its own width.
    /// Widths run from minW to maxW, in the same units as the points.
    /// </summary>
    public static List<InkSegment> Segments(DrawnSignature sig, double minW, double maxW)
    {
        var result = new List<InkSegment>();
        var median = MedianStep(sig);
        foreach (var raw in sig.Strokes)
        {
            var p = Smooth(raw);
            if (p.Count == 0) continue;
            if (p.Count == 1)                 // a dot
            {
                result.Add(new InkSegment(p[0].X, p[0].Y, 0, 0, 0, 0, p[0].X, p[0].Y, maxW, true));
                continue;
            }
            var widths = Widths(p, median, minW, maxW);
            for (var i = 0; i < p.Count - 1; i++)
            {
                InkPoint p0 = p[Math.Max(0, i - 1)], p1 = p[i], p2 = p[i + 1], p3 = p[Math.Min(p.Count - 1, i + 2)];
                result.Add(new InkSegment(
                    p1.X, p1.Y,
                    p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6,
                    p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6,
                    p2.X, p2.Y,
                    (widths[i] + widths[i + 1]) / 2, false));
            }
        }
        return result;
    }

    /// <summary>The ink's bounding box: (minX, minY, width, height), each at least one unit.</summary>
    public static (double MinX, double MinY, double W, double H) Bounds(DrawnSignature sig)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var s in sig.Strokes)
        {
            foreach (var p in s)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
        }
        if (minX > maxX) return (0, 0, 1, 1);
        return (minX, minY, Math.Max(maxX - minX, 1), Math.Max(maxY - minY, 1));
    }

    /// <summary>Fit ink of this size into the signature box: no stretching, centred, thick strokes kept inside.</summary>
    public static InkFit FitInto(SignatureSpec spec, double minX, double minY, double inkW, double inkH)
    {
        var pad = spec.StrokeMm / 2;
        double availW = spec.Box.W - 2 * pad, availH = spec.Box.H - 2 * pad;
        var scale = Math.Min(availW / inkW, availH / inkH);
        return new InkFit(
            spec.Box.X + pad + (availW - inkW * scale) / 2,
            spec.Box.Y + pad + (availH - inkH * scale) / 2,
            minX, minY, scale);
    }

    /// <summary>Scale strokes captured on a surface of one size onto the pad's own units, keeping their shape.</summary>
    public static DrawnSignature Rescale(DrawnSignature sig, double factor) =>
        new(sig.Strokes.Select(s => (IReadOnlyList<InkPoint>)s.Select(p => p with { X = p.X * factor, Y = p.Y * factor }).ToList()).ToList());

    /* ---- scanned signatures -------------------------------------- */

    private const int ScanMaxPx = 1600;

    // Otsu's method: the luminance threshold that best separates ink from paper.
    private static int OtsuThreshold(int[] hist, int total)
    {
        double sum = 0;
        for (var i = 0; i < 256; i++) sum += (double)i * hist[i];
        double sumB = 0, wB = 0, bestVar = -1;
        var best = 0;
        for (var t = 0; t < 256; t++)
        {
            wB += hist[t];
            if (wB == 0) continue;
            var wF = total - wB;
            if (wF == 0) break;
            sumB += (double)t * hist[t];
            double mB = sumB / wB, mF = (sum - sumB) / wF;
            var between = wB * wF * (mB - mF) * (mB - mF);
            if (between > bestVar) { bestVar = between; best = t; }
        }
        return best;
    }

    /// <summary>
    /// Turn a photo or scan of a signature on paper into an ink mask.
    /// Returns the signature, or the reason it could not be read.
    /// </summary>
    public static (ScannedSignature? Signature, string Error) FromImage(RgbaImage decoded, string fileName)
    {
        var k = Math.Min(1.0, (double)ScanMaxPx / Math.Max(decoded.Width, decoded.Height));
        var img = k < 1
            ? decoded.Shrink(Math.Max(1, (int)Math.Round(decoded.Width * k)), Math.Max(1, (int)Math.Round(decoded.Height * k)))
            : decoded;
        int w = img.Width, h = img.Height;

        var lum = new byte[w * h];
        var hist = new int[256];
        for (int i = 0, j = 0; j < lum.Length; i += 4, j++)
        {
            // flatten any transparency onto paper white
            var a = img.Data[i + 3] / 255.0;
            double r = img.Data[i] * a + 255 * (1 - a), g = img.Data[i + 1] * a + 255 * (1 - a), b = img.Data[i + 2] * a + 255 * (1 - a);
            var v = (int)((r * 299 + g * 587 + b * 114) / 1000);
            lum[j] = (byte)v;
            hist[v]++;
        }
        var t = OtsuThreshold(hist, w * h);
        var soft = Math.Max(8, t * 0.35);          // ink fades out over this range

        int minX = w, minY = h, maxX = -1, maxY = -1, solid = 0;
        var alpha = new byte[w * h];
        for (int y = 0, j = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++, j++)
            {
                var a = Math.Clamp((t + soft - lum[j]) / soft, 0, 1);
                if (a <= 0.06) continue;
                alpha[j] = (byte)Math.Round(a * 255);
                if (a > 0.5) solid++;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        var coverage = (double)solid / (w * h);
        if (maxX < 0 || coverage < 0.0004) return (null, "No signature found in this picture. Sign in dark ink on white paper and photograph it straight on.");
        if (coverage > 0.5) return (null, "This picture is too dark overall. Crop it to the signature on white paper, in even light, and try again.");
        if (maxX - minX < 20 || maxY - minY < 8) return (null, "The signature is too small in this picture. Crop closer to it and try again.");

        int mw = maxX - minX + 1, mh = maxY - minY + 1;
        var mask = new byte[mw * mh];
        for (var y = 0; y < mh; y++) Array.Copy(alpha, (y + minY) * w + minX, mask, y * mw, mw);
        return (new ScannedSignature(mw, mh, mask, fileName), "");
    }
}
