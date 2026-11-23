// Card configuration: every measurement the card is drawn from.
//
// Millimetres from the card's top-left corner unless a name says otherwise.
// Change numbers here; the drawing code reads them and never hard-codes a
// position.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace IdMaker.Core;

/// <summary>An axis-aligned box in millimetres.</summary>
public readonly record struct RectMm(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
}

public readonly record struct PointMm(double X, double Y);

public enum HAlign { Left, Center, Right }

/// <summary>
/// Bottom: the lowest descender of the last line sits on the box bottom and
/// extra lines grow upward. Top: the cap height of the first line sits on
/// the box top. Baseline: the baseline sits on the box bottom.
/// </summary>
public enum VAlign { Top, Bottom, Baseline }

/// <summary>The three faces drawn on the card.</summary>
public enum CardFont { Name, Title, Number }

/// <summary>One text box: where it is, and the limits the fitter works inside.</summary>
public sealed record TextSpec(
    RectMm Box, CardFont Font, string Color, HAlign Align, VAlign VAlign,
    double MaxSizePt, double MinSizePt, int MaxLines, double LineHeight, bool Uppercase);

public sealed record PhotoSpec(
    double HeadTop, double HeadCenterX, double HeadWidth, double ReachY,
    double MinHeadPx, double MaxFileMb, double MaxEnlarge, double FadeMm,
    double NudgeStepMm, double ZoomMin, double ZoomMax, double ZoomStep);

public sealed record QrSpec(double X, double Y, double Size, string Content, char ErrorCorrection, string Background, string Color);

public sealed record SignatureSpec(RectMm Box, string Color, double StrokeMm, double MinStrokeMm);

public sealed record Paper(string Key, string Label, double WidthMm, double HeightMm);

public enum MiddleNameStyle { Initial, Full, None }

public static class CardConfig
{
    public const string AppName = "Alpha Premier ID Maker";
    public const string AppVersion = "2.0.4";          // stamped into every PDF's metadata

    // Values the app's other files have to spell for themselves, because they
    // cannot read this one: the project files, the markup, the installer, the
    // packaging scripts. Each has its home here, and HomeTests reads those
    // files and fails when one of them says something else.
    public const string Publisher = "Alpha Premier Group of Companies";
    /// <summary>The program's file name without its ending, on both platforms.</summary>
    public const string ProgramName = "AlphaPremierIdMaker";
    /// <summary>Where the Windows app writes what stopped it, in its data folder; the installer's checks look for it.</summary>
    public const string LastErrorFile = "last-error.txt";
    /// <summary>The company's gold, as the windows use it. Not a colour on the card: those are in the card map.</summary>
    public const string BrandGold = "#D4AF37";
    /// <summary>Ink as it is shown on screen while signing. The printed signature takes its colour from the card map.</summary>
    public const string ScreenInk = "#1D1C1A";

    // The pictures the app is built with, by file name: in assets/ in the
    // repository, and in Assets beside the program or inside the Mac bundle.
    public const string FrontArtFile = "front-template.png";
    public const string BackArtFile = "back.png";
    public const string LogoFile = "logo.png";

    // CR80 (ISO/IEC 7810 ID-1): 85.60 x 53.98 mm
    public const double CardWidthMm = 54;
    public const double CardHeightMm = 85.6;
    public const double PrintDpi = 300;                 // the PDF is rendered at this resolution

    public const double PtToMm = 25.4 / 72;
    public const double MmToPt = 72 / 25.4;
    public static double PrintPxPerMm => PrintDpi / 25.4;

    public static (int W, int H) CardSizePx(double pxPerMm) =>
        ((int)Math.Round(CardWidthMm * pxPerMm, MidpointRounding.AwayFromZero),
         (int)Math.Round(CardHeightMm * pxPerMm, MidpointRounding.AwayFromZero));

    // Parts of the front artwork that sit IN FRONT of the photo: the dark
    // band at the bottom and the thin gold line above it. Traced from
    // front-template.png.
    public static readonly PointMm[][] FrontOutline =
    {
        new PointMm[] { new(36.7, 57.04), new(54, 57.04), new(54, 85.6), new(0, 85.6), new(0, 65.016), new(28.79, 65.016) },
        new PointMm[] { new(28.45, 59.124), new(35.56, 59.124), new(35.56, 59.427), new(28.58, 59.427), new(23.03, 65.016), new(22.61, 65.016) },
    };

    // Where the photo may appear. It steps around the logo at the top left
    // and stops at the thin black edge on the right.
    public static readonly PointMm[] PhotoArea =
    {
        new(19.5, 10.071), new(53.5, 10.071), new(53.5, 67.473), new(17, 67.473), new(17, 17.624), new(19.5, 17.624),
    };

    public static readonly TextSpec NameText = new(
        new RectMm(34.4, 59.719, 18, 5.237), CardFont.Name, "#E8CD77", HAlign.Center, VAlign.Bottom,
        MaxSizePt: 6, MinSizePt: 4.5, MaxLines: 3, LineHeight: 1, Uppercase: false);

    public static readonly TextSpec TitleText = new(
        new RectMm(34.4, 65.459, 18, 3.223), CardFont.Title, "#FFFFFF", HAlign.Center, VAlign.Top,
        MaxSizePt: 4.5, MinSizePt: 3.5, MaxLines: 2, LineHeight: 1.1, Uppercase: true);

    public static readonly TextSpec EmployeeNoText = new(
        new RectMm(28.6, 78.047, 23, 1.511), CardFont.Number, "#FFFFFF", HAlign.Right, VAlign.Baseline,
        MaxSizePt: 4, MinSizePt: 3.5, MaxLines: 1, LineHeight: 1, Uppercase: false);

    // How the separate name fields are combined for the card. Placeholders
    // {first} {middle} {last} {suffix}; runs of spaces collapse, so a missing
    // part leaves no gap. Surname-first would be "{last}, {first} {middle} {suffix}".
    public const string NameCardFormat = "{first} {middle} {last} {suffix}";
    public static readonly MiddleNameStyle MiddleName = MiddleNameStyle.Initial;
    public static readonly bool AutoCapitaliseNames = true;
    public const int MaxNameLength = 40;
    public const int MaxSuffixLength = 10;
    public const int MaxTitleLength = 60;

    // Job titles offered as chips. This is the whole list HR sees; a title
    // typed under "Other…" is remembered on that computer and joins it.
    public static readonly string[] Titles = { "IT Associate", "Marketing Associate", "HR Associate" };
    public const string TitleMergeJoiner = "/";      // between the differing parts of merged titles
    public const string TitlePairJoiner = " / ";     // between two titles that share nothing
    public const int MaxRememberedTitles = 20;

    public const string EmployeePrefix = "APG";
    public const int EmployeeDigits = 3;             // 122 -> "122", 7 -> "007"
    public const string EmployeeLabel = "EMPLOYEE NO: {prefix} - {year} - {seq}";
    public const string EmployeePlaceholder = "___";
    public const int YearsBack = 2;                  // the year selector offers current-2 … current+1
    public const int YearsAhead = 1;

    public static readonly Paper[] Papers =
    {
        new("A4", "A4 (210 × 297 mm)", 210, 297),
        new("Letter", "Letter (216 × 279 mm)", 216, 279),
        new("Legal", "Legal (216 × 356 mm)", 216, 356),
        new("A3", "A3 (297 × 420 mm)", 297, 420),
    };
    public const string DefaultPaper = "A4";
    public const double SheetMarginMm = 6;           // most office printers cannot print nearer the edge
    public const double SheetGapMm = 4;              // space between cards, room for the cutter
    public const double CropMarkMm = 3;
    public const double CropMarkOffsetMm = 1;

    // The shared register: a list in the company's Firebase project, the same
    // project APG Keystone keeps its records in. Which project that is (its
    // name and its web API key) is not written in the source: the release
    // build puts both in from the repository secrets FIREBASE_PROJECT_ID and
    // FIREBASE_API_KEY (see register/README.md), as assembly metadata on
    // IdMaker.Core.
    //
    // A build without them (a developer's, CI's for a pull request) has no
    // shared register: it keeps its own list.
    //
    // Neither value opens the register. Signing in does (an account the CEO
    // made in Keystone), and the project's security rules decide what a
    // sign-in may do. No password is ever written here or built in.
    public static readonly RegisterProject? Register = BuiltInRegister();

    private static RegisterProject? BuiltInRegister()
    {
        var built = typeof(CardConfig).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .OfType<System.Reflection.AssemblyMetadataAttribute>()
            .ToDictionary(a => a.Key, a => a.Value?.Trim() ?? "");
        var project = built.GetValueOrDefault("FirebaseProjectId") ?? "";
        var key = built.GetValueOrDefault("FirebaseApiKey") ?? "";
        return project.Length > 0 && key.Length > 0 ? new RegisterProject(project, key) : null;
    }

    public static readonly TimeSpan RegisterTimeout = TimeSpan.FromSeconds(20);

    public static readonly QrSpec Qr = new(2, 66.567, 8.8, "https://alphapremiergroup.com/", 'M', "#FFFFFF", "#000000");

    // Placement of the cut-out. Measured from the sample card: the top of
    // the head sits at 15.5 mm, the head is centred at 36.6 mm and is 15 mm
    // wide. The body must reach at least ReachY so it continues behind the
    // dark band.
    public static readonly PhotoSpec Photo = new(
        HeadTop: 15.509, HeadCenterX: 36.6, HeadWidth: 15, ReachY: 66.466,
        MinHeadPx: 180, MaxFileMb: 15,
        // How much the automatic placement may enlarge a photo beyond the
        // size that makes the head HeadWidth wide. Uncapped, a
        // head-and-shoulders crop came out at 25 mm against the 15 asked for.
        MaxEnlarge: 1.15,
        FadeMm: 7,
        NudgeStepMm: 0.5, ZoomMin: 0.5, ZoomMax: 1.5, ZoomStep: 0.05);

    // The signature is trimmed to its ink, fitted inside the box without
    // stretching, centred and drawn in white.
    public static readonly SignatureSpec Signature = new(new RectMm(34.9, 69.084, 17, 5.64), "#FFFFFF", 0.38, 0.16);

    // The prompt HR pastes into Gemini / ChatGPT to turn any phone photo
    // into a green-screen portrait the app can cut out itself. Keep
    // photo-prompt.txt at the top of the repository in step with it.
    public const string PhotoPrompt =
        "Edit this photo for a company ID card.\n\n" +
        "1. Keep the person exactly as they are: same face, features, skin tone, hair, glasses and expression. Do not beautify, slim, age or change their identity.\n" +
        "2. Show them from the chest up, facing the camera, centred, with the whole head and a little space above the hair.\n" +
        "3. Keep their clothes as they are.\n" +
        "4. Replace the background with one solid, flat, pure green colour (#00FF00). No shadows, gradients, patterns or checkerboard.\n" +
        "5. Portrait orientation, at least 1200 pixels tall. Save as PNG.";

    public static Paper PaperOrDefault(string? key) =>
        Papers.FirstOrDefault(p => p.Key == key) ?? Papers.First(p => p.Key == DefaultPaper);
}

/// <summary>The parts of a name as entered on the form; any may be empty.</summary>
public sealed record NameParts(string Last = "", string First = "", string Middle = "", string Suffix = "");

/// <summary>Words, names and numbers: the small rules every screen shares.</summary>
public static partial class Naming
{
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@"\s+,")] private static partial Regex SpaceComma();
    [GeneratedRegex(@"^,\s*")] private static partial Regex LeadingComma();
    [GeneratedRegex(@"\p{Ll}+")] private static partial Regex LowerRun();
    // Suffixes that are roman numerals: "iii" is III, never "Iii".
    [GeneratedRegex(@"^(i{1,3}|iv|vi{0,3}|ix|xi{0,3})$", RegexOptions.IgnoreCase)] private static partial Regex RomanSuffix();
    [GeneratedRegex(@"^(jr|sr|ii|iii|iv|v)\.?$", RegexOptions.IgnoreCase)] private static partial Regex NameSuffix();

    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    public static string CollapseSpaces(string? s) => Spaces().Replace(s ?? "", " ").Trim();

    public static string Upper(string s) => s.ToUpper(EnUs);
    public static bool SameText(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>"EMPLOYEE NO: APG - 2026 - 122" (seq null -> "___").</summary>
    public static string EmployeeNoLine(int year, int? seq) =>
        CardConfig.EmployeeLabel
            .Replace("{prefix}", CardConfig.EmployeePrefix)
            .Replace("{year}", year.ToString(CultureInfo.InvariantCulture))
            .Replace("{seq}", seq is null ? CardConfig.EmployeePlaceholder : PadSeq(seq.Value));

    /// <summary>"APG-2026-122".</summary>
    public static string EmployeeId(int year, int seq) => $"{CardConfig.EmployeePrefix}-{year.ToString(CultureInfo.InvariantCulture)}-{PadSeq(seq)}";

    private static string PadSeq(int seq) => seq.ToString(CultureInfo.InvariantCulture).PadLeft(CardConfig.EmployeeDigits, '0');

    /// <summary>"Villanueva" -> "V." (first letter of the first word, plus a period).</summary>
    public static string MiddleInitial(string middle)
    {
        var word = CollapseSpaces(middle).Split(' ')[0];
        if (word.Length == 0) return "";
        // a whole text element, so a letter outside the BMP is not cut in half
        var first = StringInfo.GetNextTextElement(word, 0);
        return Upper(first) + ".";
    }

    /// <summary>The name as it is printed on the card, from its parts.</summary>
    public static string ComposeName(NameParts parts)
    {
        var middle = CardConfig.MiddleName switch
        {
            MiddleNameStyle.None => "",
            MiddleNameStyle.Full => CollapseSpaces(parts.Middle),
            _ => MiddleInitial(parts.Middle),
        };
        var filled = CardConfig.NameCardFormat
            .Replace("{first}", CollapseSpaces(parts.First))
            .Replace("{middle}", middle)
            .Replace("{last}", CollapseSpaces(parts.Last))
            .Replace("{suffix}", CollapseSpaces(parts.Suffix));
        // tidy up what the missing parts left behind: double spaces, a stray
        // leading comma, a space before a comma
        var s = LeadingComma().Replace(SpaceComma().Replace(CollapseSpaces(filled), ","), "");
        return s.EndsWith(',') ? s[..^1] : s;
    }

    /// <summary>
    /// One job title from one or two roles.
    ///   ("IT Associate", "Admin Associate") -> "IT/Admin Associate"
    ///   ("IT Associate", "Driver")          -> "IT Associate / Driver"
    ///   ("IT Associate", "")                -> "IT Associate"
    /// </summary>
    public static string ComposeTitle(string? first, string? second)
    {
        var a = CollapseSpaces(first);
        var b = CollapseSpaces(second);
        if (a.Length == 0 || b.Length == 0) return a.Length == 0 ? b : a;
        if (SameText(a, b)) return a;

        var aw = a.Split(' ');
        var bw = b.Split(' ');
        var shared = 0;                      // words both titles end with
        while (shared < aw.Length - 1 && shared < bw.Length - 1
               && SameText(aw[^(1 + shared)], bw[^(1 + shared)]))
        {
            shared++;
        }
        if (shared == 0) return a + CardConfig.TitlePairJoiner + b;
        var tail = string.Join(' ', aw[^shared..]);
        var headA = string.Join(' ', aw[..^shared]);
        var headB = string.Join(' ', bw[..^shared]);
        return $"{headA}{CardConfig.TitleMergeJoiner}{headB} {tail}";
    }

    /// <summary>Letters only, accents flattened. "Dela Cruz-Santos" -> "DelaCruzSantos".</summary>
    public static string FileSafeName(string? s)
    {
        var sb = new StringBuilder();
        foreach (var ch in CollapseSpaces(s).Normalize(NormalizationForm.FormD))
        {
            if (ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z')) sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Capitalise a name typed in lower case, for the operator who did not
    /// reach for the shift key.
    ///
    /// It only fires when the field holds no capital at all. That one rule
    /// is what makes it safe on an ID card: a name whose own spelling is not
    /// Title Case — dela Cruz, van Dijk, MacArthur, III — already carries a
    /// capital somewhere, so it is left exactly as typed. Type one capital
    /// anywhere in the field and that field is yours.
    /// </summary>
    public static string AutoCapitalise(string? value, bool suffix = false)
    {
        var v = value ?? "";
        if (!CardConfig.AutoCapitaliseNames) return v;
        if (v != v.ToLowerInvariant()) return v;                 // a capital: hands off
        return LowerRun().Replace(v, m =>
        {
            var word = m.Value;
            if (suffix && RomanSuffix().IsMatch(word)) return word.ToUpperInvariant();
            var first = StringInfo.GetNextTextElement(word, 0);
            return first.ToUpperInvariant() + word[first.Length..];
        });
    }

    /// <summary>
    /// Guess the surname from a whole name, for CSV rows with no `last`
    /// column: "Christopher Joseph V. Aureo" -> "Aureo", "Juan dela Cruz Jr." -> "Cruz".
    /// </summary>
    public static string LastNameForFile(string fullName)
    {
        var parts = CollapseSpaces(fullName).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (parts.Count > 1 && NameSuffix().IsMatch(parts[^1])) parts.RemoveAt(parts.Count - 1);
        var last = parts.Count > 0 ? parts[^1] : "";
        return FileSafeName(last.Split('-')[^1]);
    }

    /// <summary>"APG-2026-122_Aureo.pdf", from the surname as typed.</summary>
    public static string PdfFileName(int year, int seq, string? lastName)
    {
        var last = FileSafeName(lastName);
        return EmployeeId(year, seq) + (last.Length > 0 ? "_" + last : "") + ".pdf";
    }

    /// <summary>The same, from a whole name whose surname has to be guessed.</summary>
    public static string PdfFileNameFromWholeName(int year, int seq, string wholeName)
    {
        var last = LastNameForFile(wholeName);
        return EmployeeId(year, seq) + (last.Length > 0 ? "_" + last : "") + ".pdf";
    }
}
