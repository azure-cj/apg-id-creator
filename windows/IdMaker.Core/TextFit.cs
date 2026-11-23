// Text fitting.
//
// Given a text box, find the largest point size that lets the text fit,
// wrapping onto up to MaxLines balanced lines. The fit is always measured
// at PRINT resolution, so the preview and the PDF break lines identically.

namespace IdMaker.Core;

/// <summary>Vertical measurements of one line, in pixels.</summary>
public readonly record struct LineMetrics(double CapHeight, double Descent, double LineHeight);

/// <summary>
/// Measures text in the card's fonts. The app answers with the real fonts;
/// tests answer with arithmetic.
/// </summary>
public interface ITextMeasurer
{
    /// <summary>Advance width of the text at this pixel size.</summary>
    double Width(CardFont font, double px, string text);

    /// <summary>Height of a capital above the baseline, in pixels.</summary>
    double CapHeight(CardFont font, double px);

    /// <summary>
    /// How far the lowest descender reaches below the baseline, in pixels.
    /// Upper-case text only descends as far as "Q," does.
    /// </summary>
    double Descent(CardFont font, double px, bool uppercase);
}

public sealed record FittedText(IReadOnlyList<string> Lines, double SizePt, bool Fits);

/// <summary>Where a fitted block lands: the x every line is anchored to, and each baseline.</summary>
public sealed record TextPlacement(double AnchorX, IReadOnlyList<double> Baselines, double Px);

public sealed record CardTextLayout(FittedText Name, FittedText Title, FittedText EmployeeNo);

public static class TextFit
{
    private const double SizeStepPt = 0.25;

    public static LineMetrics Metrics(ITextMeasurer m, TextSpec spec, double px) =>
        new(m.CapHeight(spec.Font, px), m.Descent(spec.Font, px, spec.Uppercase), px * spec.LineHeight);

    // Height of an n-line block: first cap height + (n-1) line advances + last descender.
    private static double BlockHeight(LineMetrics m, int n) => m.CapHeight + (n - 1) * m.LineHeight + m.Descent;

    /// <summary>
    /// Split the words into exactly n lines so the widest line is as narrow
    /// as possible (keeps "Christopher / Joseph V. Aureo" rather than a long
    /// first line and a one-word second line).
    /// </summary>
    private static (string[] Lines, double Width)? BalancedLines(Func<string, double> widthOf, string[] words, int n)
    {
        if (words.Length < n) return null;
        var cache = new Dictionary<string, double>();
        double Width(string s) => cache.TryGetValue(s, out var w) ? w : cache[s] = widthOf(s);

        (string[] Lines, double Width)? best = null;
        var lines = new string[n];

        void Walk(int start, int left, double widest)
        {
            if (best is { } b && widest >= b.Width) return;          // prune
            if (left == 1)
            {
                var line = string.Join(' ', words, start, words.Length - start);
                var w = Math.Max(widest, Width(line));
                if (best is null || w < best.Value.Width)
                {
                    lines[n - 1] = line;
                    best = ((string[])lines.Clone(), w);
                }
                return;
            }
            for (var end = start + 1; end <= words.Length - (left - 1); end++)
            {
                var line = string.Join(' ', words, start, end - start);
                lines[n - left] = line;
                Walk(end, left - 1, Math.Max(widest, Width(line)));
            }
        }

        Walk(0, n, 0);
        return best;
    }

    /// <summary>Fit text into a box at print resolution.</summary>
    public static FittedText Fit(ITextMeasurer m, TextSpec spec, string? text) =>
        Fit(m, spec, text, CardConfig.PrintPxPerMm);

    public static FittedText Fit(ITextMeasurer m, TextSpec spec, string? text, double pxPerMm)
    {
        var s = Naming.CollapseSpaces(text);
        if (spec.Uppercase) s = Naming.Upper(s);
        if (s.Length == 0) return new FittedText(Array.Empty<string>(), spec.MaxSizePt, true);

        var boxW = spec.Box.W * pxPerMm;
        var boxH = spec.Box.H * pxPerMm;
        var words = s.Split(' ');

        for (var pt = spec.MaxSizePt; pt >= spec.MinSizePt - 1e-9; pt -= SizeStepPt)
        {
            var px = pt * CardConfig.PtToMm * pxPerMm;
            var lm = Metrics(m, spec, px);
            double W(string t) => m.Width(spec.Font, px, t);
            if (W(s) <= boxW && BlockHeight(lm, 1) <= boxH) return new FittedText(new[] { s }, pt, true);
            for (var n = 2; n <= spec.MaxLines && BlockHeight(lm, n) <= boxH; n++)
            {
                var b = BalancedLines(W, words, n);
                if (b is { } hit && hit.Width <= boxW) return new FittedText(hit.Lines, pt, true);
            }
        }

        // Nothing fits: draw at the minimum size anyway so the problem is
        // visible on the card, and say so, so the form can complain.
        {
            var px = spec.MinSizePt * CardConfig.PtToMm * pxPerMm;
            var lm = Metrics(m, spec, px);
            double W(string t) => m.Width(spec.Font, px, t);
            IReadOnlyList<string> lines = new[] { s };
            for (var n = spec.MaxLines; n >= 2; n--)
            {
                var b = BlockHeight(lm, n) <= boxH ? BalancedLines(W, words, n) : null;
                if (b is { } hit) { lines = hit.Lines; break; }
            }
            return new FittedText(lines, spec.MinSizePt, false);
        }
    }

    /// <summary>The three text boxes of one card, fitted.</summary>
    public static CardTextLayout Layout(ITextMeasurer m, CardGeometry geo, string name, string title, int year, int? seq) =>
        new(Fit(m, geo.Name, name), Fit(m, geo.Title, title), Fit(m, geo.EmployeeNo, Naming.EmployeeNoLine(year, seq)));

    /// <summary>Where to draw a fitted block at any resolution.</summary>
    public static TextPlacement Place(ITextMeasurer m, TextSpec spec, FittedText fit, double pxPerMm)
    {
        var px = fit.SizePt * CardConfig.PtToMm * pxPerMm;
        var lm = Metrics(m, spec, px);
        var n = fit.Lines.Count;
        var box = spec.Box;

        var first = spec.VAlign switch
        {
            VAlign.Top => box.Y * pxPerMm + lm.CapHeight,
            VAlign.Bottom => box.Bottom * pxPerMm - lm.Descent - (n - 1) * lm.LineHeight,
            _ => box.Bottom * pxPerMm - (n - 1) * lm.LineHeight,
        };
        var anchor = spec.Align switch
        {
            HAlign.Center => (box.X + box.W / 2) * pxPerMm,
            HAlign.Right => box.Right * pxPerMm,
            _ => box.X * pxPerMm,
        };
        var baselines = new double[n];
        for (var i = 0; i < n; i++) baselines[i] = first + i * lm.LineHeight;
        return new TextPlacement(anchor, baselines, px);
    }
}
