// The window's light and dark palettes, read straight from the XAML. These
// run anywhere: they need the files, not Windows.

using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace IdMaker.Tests;

public partial class ThemeTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static string AppFolder => Path.Combine(Repo.Root, "windows", "IdMaker.App");

    [GeneratedRegex(@"^\{StaticResource Palette\.(\w+)\}$")] private static partial Regex PaletteRef();
    [GeneratedRegex(@"(?:Static|Dynamic)Resource (Color\.\w+)")] private static partial Regex XamlColorRef();
    [GeneratedRegex(@"""(Color\.\w+)""")] private static partial Regex CodeColorRef();

    /// <summary>Every Color.* brush in one palette file, resolved to its #RRGGBB.</summary>
    private static Dictionary<string, string> Palette(string mode)
    {
        var doc = XDocument.Load(Path.Combine(AppFolder, $"Colors.{mode}.xaml"));
        var raw = doc.Root!.Elements().Where(e => e.Name.LocalName == "Color")
            .ToDictionary(e => (string)e.Attribute(X + "Key")!, e => e.Value.Trim());
        var brushes = new Dictionary<string, string>();
        foreach (var b in doc.Root.Elements().Where(e => e.Name.LocalName == "SolidColorBrush"))
        {
            var key = (string)b.Attribute(X + "Key")!;
            var value = (string)b.Attribute("Color")!;
            var m = PaletteRef().Match(value);
            brushes[key] = m.Success ? raw[$"Palette.{m.Groups[1].Value}"] : value;
        }
        return brushes;
    }

    private static double Luminance(string hex)
    {
        static double Channel(string h, int at)
        {
            var v = int.Parse(h.Substring(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(hex, 1) + 0.7152 * Channel(hex, 3) + 0.0722 * Channel(hex, 5);
    }

    private static double Contrast(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [Fact]
    public void Light_and_dark_define_exactly_the_same_colours()
    {
        var light = Palette("Light");
        var dark = Palette("Dark");
        Assert.True(light.Count > 15);
        Assert.Equal(light.Keys.OrderBy(k => k), dark.Keys.OrderBy(k => k));
        Assert.All(light.Values.Concat(dark.Values), v => Assert.Matches("^#[0-9A-Fa-f]{6}$", v));
        // the two modes are not the same colours under two names
        Assert.NotEqual(light["Color.Surface"], dark["Color.Surface"]);
        Assert.NotEqual(light["Color.Text"], dark["Color.Text"]);
    }

    // Text must reach 4.5:1 against what it sits on (WCAG AA); the edges of
    // things you can use, and the focus ring, 3:1 (WCAG 1.4.11).
    private static readonly (string Fore, string Back, double Need)[] Pairs =
    {
        ("Text", "Bg", 4.5), ("Text", "Surface", 4.5), ("Text", "Field", 4.5), ("Text", "Hover", 4.5), ("Text", "Active", 4.5),
        ("TextMuted", "Bg", 4.5), ("TextMuted", "Surface", 4.5), ("TextMuted", "Field", 4.5),
        ("Danger", "Bg", 4.5), ("Danger", "Surface", 4.5), ("Warning", "Bg", 4.5), ("Warning", "Surface", 4.5),
        ("OnAccent", "AccentSolid", 4.5), ("Success", "Surface", 4.5), ("OnAccent", "AccentSolidHover", 4.5),
        ("OnStrong", "Strong", 4.5), ("OnStrong", "StrongHover", 4.5),
        ("StageInk", "Stage", 4.5), ("StageMuted", "Stage", 4.5), ("StageInk", "StageRaised", 4.5), ("StageMuted", "StageRaised", 4.5),
        ("TrayInk", "Tray", 4.5), ("TrayMuted", "Tray", 4.5), ("TrayInk", "TrayRaised", 4.5), ("TrayMuted", "TrayRaised", 4.5),
        ("ControlBorder", "Surface", 3), ("ControlBorder", "Field", 3), ("ControlBorder", "Stage", 3),
        ("Strong", "Surface", 3), ("Strong", "Stage", 3),
        ("Focus", "Bg", 3), ("Focus", "Surface", 3), ("Focus", "Field", 3), ("Focus", "Stage", 3),
        ("ScrollThumb", "Surface", 3), ("AccentSolid", "Tray", 3),
        // the note an "i" opens, on its pale gold wash, and the warning mark on the form's inset
        ("Text", "NoteGold", 4.5), ("Warning", "Bg", 4.5),
        // the Appearance switch sits on the darker tone
        ("TextMuted", "Active", 4.5), ("ControlBorder", "Active", 3),
        // the black header band of a tool, and the black file list in Batch
        ("TrayMuted", "Tray", 3), ("TrayInk", "TrayRaised", 4.5),
        // gold words (labels, table headings, links) and words on a switch's gold thumb
        ("Kicker", "Bg", 4.5), ("Kicker", "Surface", 4.5), ("Link", "Bg", 4.5), ("Link", "Surface", 4.5), ("OnAccent", "AccentSolid", 4.5),
    };

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Everything_reads_in_both_modes(string mode)
    {
        var c = Palette(mode);
        var failures = Pairs
            .Select(p => (p.Fore, p.Back, p.Need, Ratio: Contrast(c["Color." + p.Fore], c["Color." + p.Back])))
            .Where(p => p.Ratio < p.Need)
            .Select(p => $"{p.Fore} on {p.Back}: {p.Ratio:0.00}:1, needs {p.Need}:1")
            .ToList();
        Assert.True(failures.Count == 0, $"{mode} mode:\n" + string.Join("\n", failures));
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void The_see_through_list_in_batch_still_reads(string mode)
    {
        // The gold is drawn at the opacity MainWindow.xaml gives it, over the panel:
        // what the eye gets is the mix, so that is what the words are measured against
        var theme = File.ReadAllText(Path.Combine(AppFolder, "Theme.xaml"));
        var style = GoldWash(theme);
        var fill = Regex.Match(style, @"""Background"" Value=""\{DynamicResource (Color\.\w+)\}""");
        var alpha = Regex.Match(style, @"""Opacity"" Value=""([0-9.]+)""");
        Assert.True(fill.Success && alpha.Success, "Panel.GoldWash's fill and its opacity were not found");
        var opacity = double.Parse(alpha.Groups[1].Value, CultureInfo.InvariantCulture);
        var c = Palette(mode);
        static int Ch(string hex, int at) => int.Parse(hex.Substring(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var wash = c[fill.Groups[1].Value];
        var under = c["Color.Surface"];
        var mixed = "#" + string.Concat(new[] { 1, 3, 5 }.Select(at => ((int)Math.Round(Ch(wash, at) * opacity + Ch(under, at) * (1 - opacity))).ToString("X2", CultureInfo.InvariantCulture)));
        Assert.True(Contrast(c["Color.Text"], mixed) >= 4.5, $"{mode}: Text on {mixed}");
        Assert.True(Contrast(c["Color.TextMuted"], mixed) >= 4.5, $"{mode}: TextMuted on {mixed}: {Contrast(c["Color.TextMuted"], mixed):0.00}:1");
        // a wash, not a fill: kept faint enough that it never reads as the gold button
        Assert.InRange(opacity, 0.05, 0.25);
    }

    // the Panel.GoldWash style, as written in Theme.xaml
    private static string GoldWash(string theme)
    {
        var at = theme.IndexOf("x:Key=\"Panel.GoldWash\"", StringComparison.Ordinal);
        Assert.True(at >= 0, "Panel.GoldWash is not in Theme.xaml");
        return theme[at..theme.IndexOf("</Style>", at, StringComparison.Ordinal)];
    }

    [Fact]
    public void Nothing_holds_on_to_a_colour_when_the_mode_changes()
    {
        // A StaticResource is looked up once and kept: a control using one
        // would stay light when Windows turns dark. Only DynamicResource follows.
        foreach (var file in new[] { "Theme.xaml", "MainWindow.xaml", "App.xaml" })
        {
            var text = File.ReadAllText(Path.Combine(AppFolder, file));
            Assert.DoesNotContain("StaticResource Color.", text);
        }
        // and code never takes a brush by value: it names it, with Theme.Paint
        foreach (var cs in Directory.GetFiles(AppFolder, "*.cs"))
            Assert.DoesNotContain("Theme.Brush(", File.ReadAllText(cs));
    }

    [Fact]
    public void Every_colour_the_window_asks_for_exists()
    {
        var known = Palette("Light").Keys.ToHashSet();
        var asked = new SortedSet<string>();
        foreach (var file in Directory.GetFiles(AppFolder, "*.xaml").Where(f => !Path.GetFileName(f).StartsWith("Colors.", StringComparison.Ordinal)))
            foreach (Match m in XamlColorRef().Matches(File.ReadAllText(file))) asked.Add(m.Groups[1].Value);
        foreach (var file in Directory.GetFiles(AppFolder, "*.cs"))
            foreach (Match m in CodeColorRef().Matches(File.ReadAllText(file))) asked.Add(m.Groups[1].Value);
        Assert.True(asked.Count > 15, "the scan found too few colour names to be reading the right files");
        Assert.Empty(asked.Where(k => !known.Contains(k)));
    }

    [Fact]
    public void The_colours_dictionary_comes_first_so_the_theme_manager_can_swap_it()
    {
        var app = XDocument.Load(Path.Combine(AppFolder, "App.xaml"));
        var sources = app.Descendants().Where(e => e.Name.LocalName == "ResourceDictionary" && e.Attribute("Source") is not null)
            .Select(e => (string)e.Attribute("Source")!).ToList();
        Assert.Equal(new[] { "Colors.Light.xaml", "Theme.xaml" }, sources);
    }

    [Fact]
    public void Gold_is_kept_for_the_forward_button_and_the_current_step()
    {
        // Gold as a fill means "this is the way on": Button.Primary and the rule
        // over the step you are on. Beside them, on purpose (company design system):
        // the gold wash under a tool's list (Panel.GoldWash) and the thumb that
        // slides to a switch's choice (Switch.Thumb). Gold in words (Color.Kicker,
        // Color.Link) and gold-soft outlines are other names, checked for contrast.
        // A hover, a ticked box or a chosen chip filling with gold would make it mean nothing.
        var theme = File.ReadAllText(Path.Combine(AppFolder, "Theme.xaml"));
        var primary = theme[theme.IndexOf("x:Key=\"Button.Primary\"", StringComparison.Ordinal)..theme.IndexOf("x:Key=\"Button.Ghost\"", StringComparison.Ordinal)];
        var wash = GoldWash(theme);
        var thumbAt = theme.IndexOf("x:Key=\"Switch.Thumb\"", StringComparison.Ordinal);
        Assert.True(thumbAt >= 0, "Switch.Thumb is not in Theme.xaml");
        var thumb = theme[thumbAt..theme.IndexOf("</Style>", thumbAt, StringComparison.Ordinal)];
        var rest = theme.Replace(primary, "").Replace(wash, "").Replace(thumb, "");
        Assert.Contains("Color.AccentSolid", primary);
        Assert.Contains("Color.AccentSolid", wash);
        Assert.Contains("Color.AccentSolid", thumb);
        Assert.DoesNotContain("Color.Accent", rest);
        Assert.DoesNotContain("Color.Accent", File.ReadAllText(Path.Combine(AppFolder, "MainWindow.xaml")));

        // The one exception, chosen on purpose: the pale gold wash behind the
        // note an "i" (or a warning mark) opens. It is a box to read, never
        // something to press, and it is used by that one style and nowhere else.
        var note = theme[theme.IndexOf("x:Key=\"ToolTip.Note\"", StringComparison.Ordinal)..theme.IndexOf("x:Key=\"Button.Warn\"", StringComparison.Ordinal)];
        Assert.Contains("Color.NoteGold", note);
        Assert.DoesNotContain("Color.NoteGold", theme.Replace(note, ""));
        Assert.DoesNotContain("Color.NoteGold", File.ReadAllText(Path.Combine(AppFolder, "MainWindow.xaml")));
        Assert.All(Directory.GetFiles(AppFolder, "*.cs"), f => Assert.DoesNotContain("\"Color.NoteGold", File.ReadAllText(f)));

        var inCode = Directory.GetFiles(AppFolder, "*.cs")
            .SelectMany(f => File.ReadAllLines(f).Where(l => l.Contains("\"Color.Accent")).Select(l => $"{Path.GetFileName(f)}: {l.Trim()}"))
            .Where(l => !l.StartsWith("MapEditor.cs", StringComparison.Ordinal))      // the map's handles are drawn on the card, not the window
            .ToList();
        Assert.Single(inCode);
        Assert.Contains("current ? \"Color.AccentSolid\"", inCode[0]);
    }
}
