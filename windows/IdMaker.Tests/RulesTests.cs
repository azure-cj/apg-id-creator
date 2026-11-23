// What may be written on a card (CardRules): the rules the form and the
// batch reader both ask.

using IdMaker.Core;

namespace IdMaker.Tests;

public class RulesTests
{
    [Theory]
    [InlineData("Reyes", true)]
    [InlineData("dela Cruz-Santos", true)]
    [InlineData("O’Brien", true)]
    [InlineData("O'Brien", true)]
    [InlineData("Peña", true)]
    [InlineData("Juan S. dela Cruz, Jr.", true)]
    [InlineData("R2-D2", false)]
    [InlineData("Ana_Reyes", false)]
    [InlineData("", false)]
    public void A_name_is_letters_spaces_periods_commas_hyphens_and_apostrophes(string name, bool prints)
    {
        Assert.Equal(prints, CardRules.NamePrints(name));
    }

    [Theory]
    [InlineData("Jr.", true)]
    [InlineData("III", true)]
    [InlineData("3rd", true)]
    [InlineData("Jr-", false)]
    public void A_suffix_may_hold_digits_but_not_punctuation_beyond_a_period(string suffix, bool prints)
    {
        Assert.Equal(prints, CardRules.SuffixPrints(suffix));
    }

    [Theory]
    [InlineData("IT Associate", true)]
    [InlineData("Sales & Marketing (Region 4)", true)]
    [InlineData("IT/Admin Associate", true)]
    [InlineData("Driver #2", false)]
    [InlineData("C++ Developer", false)]
    public void A_job_title_may_hold_numbers_and_a_few_marks(string title, bool prints)
    {
        Assert.Equal(prints, CardRules.TitlePrints(title));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("007", 7)]
    [InlineData("124", 124)]
    [InlineData("9999", 9999)]
    [InlineData("0", null)]
    [InlineData("0000", null)]
    [InlineData("12345", null)]
    [InlineData("12a", null)]
    [InlineData("-12", null)]
    [InlineData(" 12", null)]
    [InlineData("", null)]
    [InlineData("١٢٣", null)]                 // digits, but not the ones the card prints
    public void An_employee_number_is_one_to_four_digits_and_not_zero(string text, int? number)
    {
        Assert.Equal(number, CardRules.SeqNumber(text));
        Assert.Equal(number is not null || text is "0" or "0000", CardRules.IsSeqText(text));
    }

    [Fact]
    public void The_number_box_keeps_digits_only_and_no_more_than_fit()
    {
        Assert.Equal("1234", CardRules.SeqTyped("a1-2 3x45"));
        Assert.Equal("", CardRules.SeqTyped("abc"));
        Assert.Equal(4, CardRules.MostSeqDigits);

        // the form and its message go by the same number
        var s = Make.Session();
        Assert.Equal("1234", s.SetSeq("123456"));
        s.SetSeq("0");
        Assert.Equal("Invalid. Use 1 to 4 digits, such as 124.", s.Problems()[Field.Seq]);
    }

    [Fact]
    public void A_batch_refuses_a_number_the_form_would_refuse()
    {
        var services = new Batch.Services(Make.LocalRegister(), new FakeMeasurer(), CardGeometry.BuiltIn,
            _ => (null, "not a picture"), null, () => new DateTime(2026, 11, 17, 9, 30, 0));
        var outcome = Batch.Run("last,first,title,seq\r\nReyes,Ana,HR Associate,12345\r\nCruz,Ben,IT Associate,١٢٣\r\nSy,Cy,IT Associate,0\r\n", null, null, services);
        Assert.Equal(3, outcome.Results.Count);
        Assert.Equal("“12345” is not an employee number (1–4 digits).", outcome.Results[0].Problem);
        Assert.Equal("“١٢٣” is not an employee number (1–4 digits).", outcome.Results[1].Problem);
        Assert.Equal("“0” is not an employee number (1–4 digits).", outcome.Results[2].Problem);
    }

    [Theory]
    [InlineData("2026", 2026)]
    [InlineData("1999", 1999)]
    [InlineData("0000", 0)]
    [InlineData("", null)]
    [InlineData("26", null)]
    [InlineData("20260", null)]
    [InlineData("abcd", null)]
    [InlineData("-026", null)]
    [InlineData("٢٠٢٦", null)]                // digits, but not the ones the card prints
    [InlineData("２０２６", null)]
    public void A_year_is_four_of_the_digits_the_card_prints(string text, int? year)
    {
        Assert.Equal(year, CardRules.YearNumber(text));
    }

    [Fact]
    public void A_year_in_other_digits_falls_back_to_this_year_and_the_batch_goes_on()
    {
        var services = new Batch.Services(Make.LocalRegister(), new FakeMeasurer(), CardGeometry.BuiltIn,
            _ => (Make.Silhouette(600, 1000, 60, 200, 260, 90, 40, 520), ""), null, () => new DateTime(2026, 11, 17, 9, 30, 0));
        var outcome = Batch.Run(
            "last,first,title,seq,year,photo\r\nCruz,Ben,IT Associate,12,٢٠٢٦,a.png\r\nSy,Cy,IT Associate,13,abcd,a.png\r\nUy,Di,IT Associate,14,2025,a.png\r\n",
            new[] { "a.png" }, null, services);
        Assert.Equal("", outcome.Error);
        // it used to stop the whole batch with a FormatException; a year in words always fell back
        Assert.Equal(new[] { "APG-2026-012", "APG-2026-013", "APG-2025-014" }, outcome.Results.Select(r => r.Id));
        Assert.All(outcome.Results, r => Assert.True(r.Ok, r.Problem));
        Assert.Equal(new[] { 2026, 2026, 2025 }, outcome.Results.Select(r => r.Entry!.Year));
    }
}
