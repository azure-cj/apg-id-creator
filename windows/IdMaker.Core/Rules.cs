// What may be written on a card: which characters a name, a suffix and a
// job title may hold, and what an employee number looks like. Each rule is
// written here once. The form (Session.Problems) and the batch reader
// (Batch.PrepareRow) both ask these, and word their own messages.
//
// The two do not ask all the same questions yet: see "Differences
// preserved" in docs/untangle/decision-log.md.

using System.Globalization;
using System.Text.RegularExpressions;

namespace IdMaker.Core;

public static partial class CardRules
{
    /// <summary>An employee number is written with at most this many digits. The number box in each window is this long.</summary>
    public const int MostSeqDigits = 4;

    [GeneratedRegex(@"^[\p{L}\p{M} .,'’-]+$")] private static partial Regex NameChars();
    [GeneratedRegex(@"^[\p{L}\p{M}. \d]+$")] private static partial Regex SuffixChars();
    [GeneratedRegex(@"^[\p{L}\p{M}\d .,'’&()/-]+$")] private static partial Regex TitleChars();

    /// <summary>Letters, spaces, periods, commas, hyphens and apostrophes: a name, or a part of one.</summary>
    public static bool NamePrints(string name) => NameChars().IsMatch(name);

    /// <summary>Jr., Sr., II, III, 3rd: letters, periods, spaces and digits.</summary>
    public static bool SuffixPrints(string suffix) => SuffixChars().IsMatch(suffix);

    /// <summary>Letters, numbers, spaces and . , ' & ( ) / -</summary>
    public static bool TitlePrints(string title) => TitleChars().IsMatch(title);

    /// <summary>Is this a card number as it is written: one to four of the digits 0 to 9?</summary>
    public static bool IsSeqText(string text) => text.Length is >= 1 and <= MostSeqDigits && text.All(char.IsAsciiDigit);

    /// <summary>The number that text stands for, or null when it is not a card number. 0 is not one.</summary>
    public static int? SeqNumber(string text) =>
        IsSeqText(text) && int.Parse(text, CultureInfo.InvariantCulture) is var number and > 0 ? number : null;

    /// <summary>
    /// The year a list gives for a card: four of the digits 0 to 9, or null.
    /// Like the number, only the digits the card prints count; anything else
    /// is not a year, and the batch falls back to this year.
    /// </summary>
    public static int? YearNumber(string text) =>
        text.Length == 4 && text.All(char.IsAsciiDigit) ? int.Parse(text, CultureInfo.InvariantCulture) : null;

    /// <summary>What the number box keeps of what was typed into it: digits only, and no more than fit.</summary>
    public static string SeqTyped(string typed) => new(typed.Where(char.IsAsciiDigit).Take(MostSeqDigits).ToArray());
}
