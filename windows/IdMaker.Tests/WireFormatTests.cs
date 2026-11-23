// What is sent to Firestore, to the letter. The register's rules live in
// another repository (firestore.rules in Deign86/apg-keystone) and accept
// exactly these fields with exactly these types, so a change here that is
// not made there leaves every card waiting in the outbox. These tests fail
// on any change to a field's name, its type, or the order they are sent in.

using System.Net;
using IdMaker.Core;

namespace IdMaker.Tests;

public class WireFormatTests
{
    private static readonly RegisterEntry Juan = new()
    {
        Id = "APG-2026-124", Year = 2026, Seq = 124, Name = "Juan S. dela Cruz Jr.", Title = "IT/Admin Associate",
        IssuedAt = "2026-11-17T01:30:00.000Z", Replacement = false,
        Last = "dela Cruz", First = "Juan", Middle = "Santos", Suffix = "Jr.",
        Role1 = "IT Associate", Role2 = "Admin Associate", Kind = PersonKind.Intern,
    };

    private const string JuanFields =
        "{\"id\":{\"stringValue\":\"APG-2026-124\"},"
        + "\"year\":{\"integerValue\":\"2026\"},"
        + "\"seq\":{\"integerValue\":\"124\"},"
        + "\"name\":{\"stringValue\":\"Juan S. dela Cruz Jr.\"},"
        + "\"title\":{\"stringValue\":\"IT/Admin Associate\"},"
        + "\"issuedAt\":{\"stringValue\":\"2026-11-17T01:30:00.000Z\"},"
        + "\"replacement\":{\"booleanValue\":false},"
        + "\"last\":{\"stringValue\":\"dela Cruz\"},"
        + "\"first\":{\"stringValue\":\"Juan\"},"
        + "\"middle\":{\"stringValue\":\"Santos\"},"
        + "\"suffix\":{\"stringValue\":\"Jr.\"},"
        + "\"role1\":{\"stringValue\":\"IT Associate\"},"
        + "\"role2\":{\"stringValue\":\"Admin Associate\"},"
        + "\"by\":{\"stringValue\":\"uid-hr\"},"
        + "\"byEmail\":{\"stringValue\":\"hr@apg.test\"},"
        + "\"kind\":{\"stringValue\":\"intern\"}}";

    private const string ListUrl = "https://firestore.googleapis.com/v1/projects/demo-idmaker/databases/(default)/documents/keystone/apg/idreg";

    [Fact]
    public async Task An_issued_id_is_sent_as_exactly_these_fields_of_exactly_these_types()
    {
        var firebase = new FakeFirebase();
        var r = await Make.SignedIn(firebase);

        Assert.Equal(SharedOutcome.Sent, (await r.RecordShared(Juan)).Outcome);
        Assert.Equal(JuanFields, firebase.Docs["APG-2026-124"].ToJsonString());
        Assert.Contains($"POST {ListUrl}?documentId=APG-2026-124", firebase.Requests);
    }

    [Fact]
    public async Task A_card_from_before_the_form_asked_who_it_is_for_is_sent_without_a_kind()
    {
        var firebase = new FakeFirebase();
        var r = await Make.SignedIn(firebase);
        var old = Juan with { Id = "APG-2026-125", Seq = 125, Kind = "", Middle = "", Suffix = "", Role2 = "" };

        Assert.Equal(SharedOutcome.Sent, (await r.RecordShared(old)).Outcome);
        Assert.Equal(
            "{\"id\":{\"stringValue\":\"APG-2026-125\"},"
            + "\"year\":{\"integerValue\":\"2026\"},"
            + "\"seq\":{\"integerValue\":\"125\"},"
            + "\"name\":{\"stringValue\":\"Juan S. dela Cruz Jr.\"},"
            + "\"title\":{\"stringValue\":\"IT/Admin Associate\"},"
            + "\"issuedAt\":{\"stringValue\":\"2026-11-17T01:30:00.000Z\"},"
            + "\"replacement\":{\"booleanValue\":false},"
            + "\"last\":{\"stringValue\":\"dela Cruz\"},"
            + "\"first\":{\"stringValue\":\"Juan\"},"
            + "\"middle\":{\"stringValue\":\"\"},"
            + "\"suffix\":{\"stringValue\":\"\"},"
            + "\"role1\":{\"stringValue\":\"IT Associate\"},"
            + "\"role2\":{\"stringValue\":\"\"},"
            + "\"by\":{\"stringValue\":\"uid-hr\"},"
            + "\"byEmail\":{\"stringValue\":\"hr@apg.test\"}}",
            firebase.Docs["APG-2026-125"].ToJsonString());
    }

    [Fact]
    public async Task A_replacement_is_sent_as_the_same_fields_under_the_card_it_replaces()
    {
        var firebase = new FakeFirebase();
        var r = await Make.SignedIn(firebase);
        Assert.Equal(SharedOutcome.Sent, (await r.RecordShared(Juan)).Outcome);

        var again = Juan with { IssuedAt = "2026-12-01T02:00:00.000Z", Replacement = true };
        Assert.Equal(SharedOutcome.Sent, (await r.RecordShared(again)).Outcome);
        Assert.Equal(
            JuanFields.Replace("2026-11-17T01:30:00.000Z", "2026-12-01T02:00:00.000Z").Replace("\"booleanValue\":false", "\"booleanValue\":true"),
            firebase.Docs["APG-2026-124/replacements/20261201T020000000Z"].ToJsonString());
        Assert.Contains($"POST {ListUrl}/APG-2026-124/replacements?documentId=20261201T020000000Z", firebase.Requests);
        // the first card is as it was
        Assert.Equal(JuanFields, firebase.Docs["APG-2026-124"].ToJsonString());
        Assert.DoesNotContain(firebase.Requests, q => q.StartsWith("PATCH ") || q.StartsWith("PUT ") || q.StartsWith("DELETE "));
    }

    [Fact]
    public async Task What_the_form_issues_is_what_is_sent()
    {
        var firebase = new FakeFirebase();
        var register = await Make.SignedIn(firebase);
        var s = Make.Session(register);
        s.SetNamePart(Field.Last, "dela Cruz");
        s.SetNamePart(Field.First, "Juan");
        s.SetNamePart(Field.Middle, "Santos");
        s.SetNamePart(Field.Suffix, "Jr.");
        s.PickKind(PersonKind.Intern);
        s.PickRole1("IT Associate");
        s.AddSecondRole();
        s.PickRole2(RoleChoice.Other);
        s.TypeRole2("Admin Associate");
        s.SetSeq("124");
        await s.DuplicateCheck;
        s.AcceptPhoto(Make.Photo());
        s.SetSignature(Session.ExampleSignature());
        s.GoStep(Session.LastStep);
        Assert.Equal(Session.LastStep, s.StepIndex);

        var report = await s.RecordShared(s.MarkIssued());
        Assert.Equal("", report.Message());
        // 9:30 in the morning of 17 Nov 2026 on the test's clock, written as UTC
        var stamp = new DateTime(2026, 11, 17, 9, 30, 0).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(JuanFields.Replace("2026-11-17T01:30:00.000Z", stamp), firebase.Docs["APG-2026-124"].ToJsonString());
    }

    [Theory]
    [InlineData("intern", "intern")]
    [InlineData("Intern", "intern")]
    [InlineData(" EMPLOYEE ", "employee")]
    [InlineData("volunteer", "")]
    [InlineData("", "")]
    public void What_is_sent_for_a_kind_is_what_would_be_read_back(string kept, string sent)
    {
        var kind = RegisterFields.Named("kind");
        Assert.True(kind.OnlyWhenKnown);
        Assert.Equal(sent, kind.Sent(Juan with { Kind = kept }));
        // every other text field is sent as it is kept
        Assert.All(RegisterFields.All.Where(f => f.Kind == RegisterFieldKind.Text && !f.OnlyWhenKnown), f => Assert.Equal(f.TextOf(Juan), f.Sent(Juan)));
    }

    [Fact]
    public void The_saved_list_has_the_columns_it_always_had_and_every_field_is_one_of_them()
    {
        var register = Make.LocalRegister();
        register.Record(Juan);
        var lines = register.ToCsv().TrimStart('\uFEFF').Split("\r\n");
        Assert.Equal("\"id\",\"year\",\"seq\",\"last\",\"first\",\"middle\",\"suffix\",\"name\",\"title\",\"role1\",\"role2\",\"issuedAt\",\"replacement\",\"kind\"", lines[0]);
        Assert.Equal("\"APG-2026-124\",\"2026\",\"124\",\"dela Cruz\",\"Juan\",\"Santos\",\"Jr.\",\"Juan S. dela Cruz Jr.\",\"IT/Admin Associate\",\"IT Associate\",\"Admin Associate\",\"2026-11-17T01:30:00.000Z\",\"no\",\"intern\"", lines[1]);
        Assert.Equal(RegisterFields.All.Select(f => f.Name).Order(StringComparer.Ordinal), RegisterFields.CsvColumns.Select(f => f.Name).Order(StringComparer.Ordinal));

        // and the list reads back as the same card, on a computer that has not seen it
        var other = Make.LocalRegister();
        Assert.Equal(1, other.ImportCsv(register.ToCsv()));
        Assert.Equal(Juan, Assert.Single(other.All()));
    }

    [Fact]
    public void The_field_list_is_the_one_the_register_s_own_page_describes()
    {
        // register/README.md is what the rules in apg-keystone were written from: its table and Core's list name the same fields
        var page = File.ReadAllText(Path.Combine(Repo.Root, "register", "README.md")).Replace("\r\n", "\n");     // however git checked it out
        var table = page[page.IndexOf("| Field | What |", StringComparison.Ordinal)..];
        table = table[..table.IndexOf("\n\n", StringComparison.Ordinal)];
        var described = table.Split('\n').Skip(2)
            .SelectMany(row => row.Split('|')[1].Split(','))
            .Select(name => name.Trim().Trim('`'))
            .Order(StringComparer.Ordinal).ToList();
        var sent = RegisterFields.All.Select(f => f.Name).Append(RegisterFields.By).Append(RegisterFields.ByEmail).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(sent, described);
        Assert.Equal(16, sent.Count);
    }

    [Fact]
    public void The_form_and_a_batch_record_a_card_the_same_way()
    {
        var entry = RegisterEntry.Issue(2026, 7, "Ana Reyes", "HR Associate", new NameParts("Reyes", "Ana"), "HR Associate", "", PersonKind.Employee,
            replacement: false, new DateTime(2026, 11, 17, 1, 30, 0, 250, DateTimeKind.Utc));
        Assert.Equal("APG-2026-007", entry.Id);
        Assert.Equal("2026-11-17T01:30:00.250Z", entry.IssuedAt);
        Assert.Equal("Reyes", entry.Last);
        Assert.Equal("Ana", entry.First);
        Assert.Equal("", entry.Middle);
        Assert.Equal(PersonKind.Employee, entry.Kind);
        Assert.False(entry.Replacement);
    }

    [Fact]
    public void The_migration_script_sends_the_fields_of_the_list_under_the_same_names()
    {
        // register/migrate.mjs writes the same documents from the old sheet, in JavaScript, so it cannot
        // read Core's list: it is held to it here. It sends every field that is always sent, then who
        // issued it; not kind, which the old sheet never had and the rules let a document go without.
        var script = File.ReadAllText(Path.Combine(Repo.Root, "register", "migrate.mjs")).Replace("\r\n", "\n");
        var from = script.IndexOf("function documentOf(", StringComparison.Ordinal);
        Assert.True(from >= 0, "register/migrate.mjs no longer has a documentOf function.");
        var document = script[from..];
        document = document[..document.IndexOf("\n}\n", StringComparison.Ordinal)];

        var written = System.Text.RegularExpressions.Regex.Matches(document, @"\b([A-Za-z0-9]+): (text\(|whole\(|\{ booleanValue)")
            .Select(m => (Name: m.Groups[1].Value, Kind: m.Groups[2].Value switch { "text(" => RegisterFieldKind.Text, "whole(" => RegisterFieldKind.Whole, _ => RegisterFieldKind.YesNo }))
            .ToList();
        var always = RegisterFields.All.Where(f => !f.OnlyWhenKnown).Select(f => (f.Name, f.Kind))
            .Append((RegisterFields.By, RegisterFieldKind.Text)).Append((RegisterFields.ByEmail, RegisterFieldKind.Text)).ToList();
        Assert.Equal(always, written);
        Assert.Equal(15, written.Count);
        Assert.DoesNotContain(written, w => RegisterFields.All.Any(f => f.OnlyWhenKnown && f.Name == w.Name));

        // and every column it reads from the sheet is a field of the list (the sheet's headers are matched in lower case)
        var read = System.Text.RegularExpressions.Regex.Matches(script, @"cell\(r, '([a-z0-9]+)'\)").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.NotEmpty(read);
        var names = RegisterFields.All.Select(f => f.Name.ToLowerInvariant()).ToList();
        Assert.All(read, column => Assert.Contains(column, names));
        Assert.Equal(written.Count - 2, read.Count);
    }
}
