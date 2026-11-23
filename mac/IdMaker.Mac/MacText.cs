using CoreGraphics;
using CoreText;
using IdMaker.Core;

namespace IdMaker.Mac;

/// <summary>
/// Measures and outlines text with the real card fonts, through Core Text.
/// The Mac counterpart of the Windows app's WpfTextMeasurer, and held to the
/// same numbers: the widths and heights the web app produced
/// (`web-reference.json`), which every card already printed was laid out from.
/// </summary>
public sealed class MacTextMeasurer : ITextMeasurer
{
    private readonly Dictionary<CardFont, CGFont> _faces;
    private readonly Dictionary<(CardFont, double), CTFont> _sized = new();
    private readonly Dictionary<(CardFont, double, string), double> _widths = new();
    private readonly Dictionary<(CardFont, double, string), (double Ascent, double Descent)> _ink = new();

    private MacTextMeasurer(Dictionary<CardFont, CGFont> faces) { _faces = faces; }

    /// <summary>Load the three card fonts from a folder. Each is read from its own file, so its weight is exactly the file's.</summary>
    public static MacTextMeasurer Load(string fontsDirectory)
    {
        CGFont One(string file)
        {
            var path = Path.Combine(fontsDirectory, file);
            if (!File.Exists(path)) throw new FileNotFoundException($"The card font {file} is missing from {fontsDirectory}.", path);
            using var provider = new CGDataProvider(path);
            return CGFont.CreateFromProvider(provider) ?? throw new InvalidOperationException($"The Mac could not read the card font {file}.");
        }
        return new MacTextMeasurer(new Dictionary<CardFont, CGFont>
        {
            [CardFont.Name] = One("Poppins-Bold.ttf"),
            [CardFont.Title] = One("Montserrat-ExtraBold.ttf"),
            [CardFont.Number] = One("Arimo-Regular.ttf"),
        });
    }

    private CTFont Sized(CardFont font, double px)
    {
        if (!_sized.TryGetValue((font, px), out var sized)) _sized[(font, px)] = sized = new CTFont(_faces[font], (nfloat)px, CGAffineTransform.MakeIdentity());
        return sized;
    }

    private CTLine Line(CardFont font, double px, string text) =>
        new(new NSAttributedString(text, new CTStringAttributes { Font = Sized(font, px) }));

    // One word (no spaces in it), measured as the Mac shapes it.
    private double WordWidth(CardFont font, double px, string word)
    {
        var key = (font, px, word);
        if (_widths.TryGetValue(key, out var width)) return width;
        using var line = Line(font, px, word);
        return _widths[key] = line.GetTypographicBounds();
    }

    /// <summary>
    /// A line as the words it is made of and where each starts. Text is set a
    /// word at a time with a plain space between words, as the web app's
    /// canvas set it and as the Windows app does: letters are tucked together
    /// inside a word, never across a space.
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
        double width = (words.Length - 1) * space;
        foreach (var word in words) if (word.Length > 0) width += WordWidth(font, px, word);
        return width;
    }

    /// <summary>
    /// The outline of a word with its baseline start at (x, baseline), in a
    /// space where y grows downwards, as the card is drawn. Outlines land
    /// exactly where they are put, so a card is the same on every Mac.
    /// </summary>
    public CGPath Outline(CardFont font, double px, string word, double x, double baseline)
    {
        var outline = new CGPath();
        using var line = Line(font, px, word);
        foreach (var run in line.GetGlyphRuns())
        {
            var glyphs = run.GetGlyphs();
            var positions = run.GetPositions();
            var face = run.GetAttributes()?.Font ?? Sized(font, px);
            for (var i = 0; i < glyphs.Length; i++)
            {
                // Core Text's y grows upwards: flip each glyph about its baseline.
                var place = new CGAffineTransform(1, 0, 0, -1, (nfloat)(x + positions[i].X), (nfloat)(baseline - positions[i].Y));
                using var glyph = face.GetPathForGlyph(glyphs[i], ref place);
                if (glyph is not null) outline.AddPath(glyph);
            }
        }
        return outline;
    }

    // How far the ink of `sample` reaches above and below the baseline,
    // rounded up to whole pixels as the web app's canvas reported it.
    private (double Ascent, double Descent) Ink(CardFont font, double px, string sample)
    {
        var key = (font, px, sample);
        if (_ink.TryGetValue(key, out var hit)) return hit;
        using var outline = Outline(font, px, sample, 0, 0);
        var bounds = outline.IsEmpty ? CGRect.Empty : outline.PathBoundingBox;
        var ink = bounds.IsEmpty
            ? (0.0, 0.0)
            : (Math.Ceiling(-bounds.Top - 1e-6), Math.Max(0, Math.Ceiling(bounds.Bottom - 1e-6)));
        return _ink[key] = ink;
    }

    public double CapHeight(CardFont font, double px) => Ink(font, px, "H").Ascent;
    public double Descent(CardFont font, double px, bool uppercase) => Ink(font, px, uppercase ? "Q," : "gjpqy").Descent;
}
