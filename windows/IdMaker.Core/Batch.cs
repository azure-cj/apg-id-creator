// Batch issuing from a CSV.
//
// HR picks a CSV, the photo files and (optionally) scanned signature files.
// Each row is checked exactly like a single card; the result is ONE PDF plus
// a report of what was skipped.
//
// CSV columns (header row required, order free, extra columns ignored):
//   last, first, title, seq     required
//   title2                      a second role, merged with title
//   middle, suffix              optional parts of the name
//   name                        accepted instead of last/first for older
//                               lists: printed as given, surname guessed
//                               for the file name
//   year                        optional, defaults to this year
//   photo                       file name of the photo
//   signature                   file name of a scanned signature (optional)
//   replacement                 yes/true to allow an already-issued number
//   kind                        intern or employee; employee when left out
//                               (a list from before the column existed)
//
// Rows are never half-issued: a row that fails is skipped and reported, and
// only the rows that made it into the PDF are written to the register.

using System.Globalization;
using System.Text.RegularExpressions;

namespace IdMaker.Core;

public sealed record BatchRowResult
{
    public int Line { get; init; }
    public string Label { get; init; } = "";
    public bool Ok { get; init; }
    public string Problem { get; init; } = "";
    public string Id { get; init; } = "";
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public bool NoSignature { get; init; }
    public CardData? Data { get; init; }
    public RegisterEntry? Entry { get; init; }
}

public sealed record BatchOutcome(IReadOnlyList<BatchRowResult> Results, string Error)
{
    public IReadOnlyList<BatchRowResult> Good => Results.Where(r => r.Ok).ToList();
}

public static partial class Batch
{
    // The three ways a batch can be laid out, by the word each window keeps the choice under.
    // The Windows markup carries the same words as the tags of its list.
    public const string LayoutDuplex = "duplex";
    public const string LayoutPairs = "pairs";
    public const string LayoutCards = "cards";

    public const int MaxRows = 200;

    public const string CsvTemplate =
        "last,first,middle,suffix,title,title2,seq,year,photo,signature,replacement,kind\r\n"
        + "Aureo,\"Christopher Joseph\",Villanueva,,\"IT Associate\",,122,2026,aureo.png,aureo-sign.jpg,,employee\r\n"
        + "Reyes,Ana,Santos,,\"HR Associate\",\"Admin Associate\",123,2026,reyes.png,,,employee\r\n"
        + "\"dela Cruz\",Juan,Santos,Jr.,\"Marketing Associate\",,124,2026,cruz.png,,,intern\r\n";

    [GeneratedRegex(@"^(yes|y|true|1)$", RegexOptions.IgnoreCase)] private static partial Regex Yes();

    /// <summary>The CSV as one dictionary per person, keyed by lower-case column name. "_line" is the line in the file.</summary>
    public static (List<Dictionary<string, string>>? Rows, string Error) ReadRows(string csvText)
    {
        var rows = Csv.Parse(csvText);
        if (rows.Count == 0) return (null, "This CSV is empty.");
        var headers = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        foreach (var need in new[] { "title", "seq" })
        {
            if (!headers.Contains(need)) return (null, $"The CSV needs a “{need}” column. Found: {string.Join(", ", headers)}.");
        }
        if (!headers.Contains("name") && !(headers.Contains("last") && headers.Contains("first")))
            return (null, $"The CSV needs “last” and “first” columns (or a single “name” column). Found: {string.Join(", ", headers)}.");

        var result = new List<Dictionary<string, string>>();
        for (var i = 1; i < rows.Count; i++)
        {
            var o = new Dictionary<string, string> { ["_line"] = (i + 1).ToString(CultureInfo.InvariantCulture) };
            for (var c = 0; c < headers.Count; c++) o[headers[c]] = c < rows[i].Count ? rows[i][c].Trim() : "";
            result.Add(o);
        }
        if (result.Count == 0) return (null, "The CSV has a header row but no people in it.");
        if (result.Count > MaxRows) return (null, $"That is {result.Count} rows. Please split it into batches of {MaxRows} or fewer.");
        return (result, "");
    }

    /// <summary>
    /// Matches a name from the CSV to one of the chosen files, ignoring case,
    /// folders and — as a fallback — the extension, so "Aureo.JPG" finds "aureo.png".
    /// </summary>
    public static Func<string?, string?> FileIndex(IEnumerable<string>? paths)
    {
        var byName = new Dictionary<string, string>();
        var byStem = new Dictionary<string, string>();
        foreach (var path in paths ?? Array.Empty<string>())
        {
            var name = BaseName(path);
            byName[name] = path;
            byStem.TryAdd(Stem(name), path);
        }
        return wanted =>
        {
            var w = BaseName(wanted ?? "");
            if (w.Length == 0) return null;
            return byName.TryGetValue(w, out var exact) ? exact : byStem.TryGetValue(Stem(w), out var loose) ? loose : null;
        };
    }

    private static string BaseName(string path) => path.Split('/', '\\')[^1].Trim().ToLowerInvariant();
    // "aureo.png" -> "aureo"; a name that merely ends in a full stop ("… Jr.") is left whole
    private static string Stem(string name) { var dot = name.LastIndexOf('.'); return dot > 0 && dot < name.Length - 1 ? name[..dot] : name; }

    /// <summary>What the batch needs from the app around it.</summary>
    public sealed record Services(
        Register Register,
        ITextMeasurer Measurer,
        CardGeometry Geometry,
        // open a picture file: the pixels, or why it could not be opened
        Func<string, (RgbaImage? Image, string Error)> Decode,
        IBackgroundRemover? Remover,
        Func<DateTime> Now);

    private static string Col(Dictionary<string, string> row, string name) => row.TryGetValue(name, out var v) ? v : "";

    private static BatchRowResult PrepareRow(Dictionary<string, string> row, Func<string?, string?> findPhoto,
        Func<string?, string?> findSignature, HashSet<string> seen, Services s)
    {
        var line = int.Parse(row["_line"], CultureInfo.InvariantCulture);
        var label = Naming.CollapseSpaces(Col(row, "name").Length > 0
            ? Col(row, "name")
            : string.Join(", ", new[] { Col(row, "last"), Col(row, "first") }.Where(v => v.Length > 0)));
        if (label.Length == 0) label = $"line {line}";
        BatchRowResult Fail(string problem) => new() { Line = line, Label = label, Problem = problem };

        var title = Naming.ComposeTitle(Col(row, "title"), Col(row, "title2"));

        // either separate parts, or a whole name from an older list
        var parts = new NameParts(
            Naming.CollapseSpaces(Col(row, "last")), Naming.CollapseSpaces(Col(row, "first")),
            Naming.CollapseSpaces(Col(row, "middle")), Naming.CollapseSpaces(Col(row, "suffix")));
        var whole = Naming.CollapseSpaces(Col(row, "name"));
        var split = parts.Last.Length > 0 || parts.First.Length > 0;
        if (split && parts.Last.Length == 0) return Fail("No last name.");
        if (split && parts.First.Length == 0) return Fail("No first name.");
        if (!split && whole.Length == 0) return Fail("No name.");
        var name = split ? Naming.ComposeName(parts) : whole;
        if (!CardRules.NamePrints(name)) return Fail("The name has characters the card can’t print.");
        if (title.Length == 0) return Fail("No job title.");
        var kindText = Col(row, "kind");
        var kind = kindText.Length == 0 ? PersonKind.Employee : PersonKind.Parse(kindText);
        if (kind.Length == 0) return Fail($"“{kindText}” is neither “intern” nor “employee”.");

        var seqText = Col(row, "seq");
        if (CardRules.SeqNumber(seqText) is not { } seq) return Fail($"“{seqText}” is not an employee number (1–{CardRules.MostSeqDigits} digits).");
        var year = CardRules.YearNumber(Col(row, "year")) ?? s.Now().Year;
        var id = Naming.EmployeeId(year, seq);

        if (seen.Contains(id)) return Fail($"{id} appears twice in this CSV.");
        var allowReplacement = Yes().IsMatch(Col(row, "replacement"));
        var already = s.Register.Find(year, seq) ?? s.Register.FindSharedNow(year, seq).Entry;
        if (already is not null && !allowReplacement)
            return Fail($"{id} is already issued to {already.Name}. Put “yes” in the replacement column to re-print it.");

        // text must fit the card
        var layout = TextFit.Layout(s.Measurer, s.Geometry, name, title, year, seq);
        if (!layout.Name.Fits) return Fail("The name is too long for the card.");
        if (!layout.Title.Fits) return Fail("The job title is too long for the card.");

        var photoColumn = Col(row, "photo");
        var photoPath = findPhoto(photoColumn.Length > 0 ? photoColumn : name);     // fall back to a file named after the person
        if (photoPath is null) return Fail(photoColumn.Length > 0 ? $"No photo file named “{photoColumn}”." : "No photo column and no file matching the name.");
        var photoName = Path.GetFileName(photoPath);
        var (photoImage, photoError) = s.Decode(photoPath);
        if (photoImage is null) return Fail($"{photoName}: {photoError}");
        var photo = PhotoPipeline.Analyse(photoImage, photoName, s.Geometry.Photo, s.Remover);
        if (!photo.Check.Ok) return Fail($"{photoName}: {photo.Check.Problem}");

        ScannedSignature? signature = null;
        var sigColumn = Col(row, "signature");
        if (sigColumn.Length > 0)
        {
            var sigPath = findSignature(sigColumn);
            if (sigPath is null) return Fail($"No signature file named “{sigColumn}”.");
            var (sigImage, sigError) = s.Decode(sigPath);
            if (sigImage is null) return Fail($"{Path.GetFileName(sigPath)}: {sigError}");
            var (sig, error) = SignatureInk.FromImage(sigImage, Path.GetFileName(sigPath));
            if (sig is null) return Fail($"{Path.GetFileName(sigPath)}: {error}");
            signature = sig;
        }

        seen.Add(id);
        return new BatchRowResult
        {
            Line = line, Label = label, Ok = true, Id = id,
            Warnings = photo.Check.Warnings,
            NoSignature = signature is null,
            Data = new CardData(name, title, year, seq, photo.Image, photo.Check.Placement, signature),
            Entry = RegisterEntry.Issue(
                year, seq, name, title,
                // an older list's whole name has no parts: only the surname is guessed, for the file name
                split ? parts : new NameParts(Last: Naming.LastNameForFile(whole)),
                Naming.CollapseSpaces(Col(row, "title")), Naming.CollapseSpaces(Col(row, "title2")),
                kind, replacement: already is not null, s.Now()),
        };
    }

    /// <summary>
    /// Check every row. The shared register should have been read first
    /// (Register.LoadIndex), so duplicates across computers are caught.
    /// </summary>
    public static BatchOutcome Run(string csvText, IEnumerable<string>? photoPaths, IEnumerable<string>? signaturePaths,
        Services services, Action<int, int>? onProgress = null)
    {
        var (rows, error) = ReadRows(csvText);
        if (rows is null) return new BatchOutcome(Array.Empty<BatchRowResult>(), error);

        var findPhoto = FileIndex(photoPaths);
        var findSignature = FileIndex(signaturePaths);
        var seen = new HashSet<string>();
        var results = new List<BatchRowResult>();
        foreach (var row in rows)
        {
            onProgress?.Invoke(results.Count + 1, rows.Count);
            results.Add(PrepareRow(row, findPhoto, findSignature, seen, services));
        }
        return new BatchOutcome(results, results.Any(r => r.Ok) ? "" : "None of the rows could be used — see the list below.");
    }

    /// <summary>The file a batch is saved as, and the title inside it.</summary>
    public static (string FileName, string Title) FileFor(int cards, bool sheets, DateTime now)
    {
        var stamp = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return (sheets ? $"apg-id-sheets-{stamp}.pdf" : $"apg-id-batch-{stamp}.pdf",
                $"Alpha Premier IDs — {cards} card{Words.S(cards)} — {stamp}");
    }
}
