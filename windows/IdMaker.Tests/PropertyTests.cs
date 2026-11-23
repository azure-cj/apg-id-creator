// Statements that must hold for any input, tried against many generated
// ones. The generator is a seeded Random and a loop: the repository adds no
// package for it, and a failure names the seed so it can be run again.

using System.Net;
using System.Text;
using IdMaker.Core;

namespace IdMaker.Tests;

public class PropertyTests
{
    private const int Tries = 300;

    // letters with marks, quotes, commas, a line break inside a field, and a character outside the BMP
    private const string Alphabet = "abcXYZ ñÑéü'’\",;.-()/&\n\t0123456789😀";

    private static string Text(Random r, int most)
    {
        var s = new StringBuilder();
        for (var n = r.Next(1, most); n > 0; n--)
        {
            var i = r.Next(Alphabet.Length);
            // a surrogate pair goes in whole
            if (char.IsHighSurrogate(Alphabet[i])) s.Append(Alphabet, i, 2);
            else if (!char.IsLowSurrogate(Alphabet[i])) s.Append(Alphabet[i]);
        }
        var text = s.ToString().Trim();
        return text.Length > 0 ? text : "x";
    }

    private static RegisterEntry Card(Random r, int seq, int year = 2026) => new()
    {
        Id = Naming.EmployeeId(year, seq), Year = year, Seq = seq, Name = Text(r, 40), Title = Text(r, 30),
        Last = Text(r, 20), First = Text(r, 20), Middle = Text(r, 10), Suffix = Text(r, 5), Role1 = Text(r, 30), Role2 = Text(r, 30),
        IssuedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(r.Next(30_000_000)).AddMilliseconds(r.Next(1000)).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
        Replacement = r.Next(4) == 0,
        Kind = r.Next(3) switch { 0 => PersonKind.Intern, 1 => PersonKind.Employee, _ => "" },
    };

    [Fact]
    public void Any_register_saved_as_csv_reads_back_the_same()
    {
        for (var seed = 0; seed < Tries; seed++)
        {
            var r = new Random(seed);
            var from = Make.LocalRegister();
            var cards = Enumerable.Range(1, r.Next(1, 12)).Select(seq => Card(r, seq)).ToList();
            foreach (var c in cards) from.Record(c);

            var to = Make.LocalRegister();
            Assert.True(to.ImportCsv(from.ToCsv()) == cards.Count, $"seed {seed}: not every card came back");
            foreach (var c in cards)
            {
                var back = to.Find(c.Year, c.Seq);
                Assert.True(c == back, $"seed {seed}: {c.Id} changed on the way through the CSV: {c} became {back}");
            }
            // and reading it a second time adds nothing
            Assert.True(to.ImportCsv(from.ToCsv()) == 0, $"seed {seed}: a second read added cards");
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task However_often_a_card_is_sent_and_however_the_answers_are_lost_it_is_one_card(HttpStatusCode alreadyThere)
    {
        for (var seed = 0; seed < Tries; seed++)
        {
            var r = new Random(seed);
            var firebase = new FakeFirebase { AlreadyThere = alreadyThere };
            var register = await Make.SignedIn(firebase);
            var card = Card(r, r.Next(1, 999)) with { Replacement = false };

            for (var send = r.Next(1, 6); send > 0; send--)
            {
                firebase.LoseNextAnswer = r.Next(3) == 0;
                firebase.Offline = r.Next(5) == 0;
                await register.RecordShared(card);
            }
            firebase.LoseNextAnswer = false;
            firebase.Offline = false;
            Assert.True(await register.SendWaiting() == 0, $"seed {seed}: the card was left waiting with the register in reach");

            Assert.True(firebase.Docs.Count == 1, $"seed {seed}: {firebase.Docs.Count} documents for one card");
            Assert.True(firebase.Get(card.Id)!.Name == card.Name, $"seed {seed}: the card in the register is not the one sent");
            Assert.True(register.Refused.Count == 0, $"seed {seed}: a card was refused as a duplicate of itself");
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Whatever_order_two_computers_issue_in_a_number_keeps_its_first_card_and_every_other_is_told(HttpStatusCode alreadyThere)
    {
        for (var seed = 0; seed < Tries; seed++)
        {
            var r = new Random(seed);
            var firebase = new FakeFirebase { AlreadyThere = alreadyThere };
            var computers = new[] { await Make.SignedIn(firebase), await Make.SignedIn(firebase) };
            var first = new Dictionary<string, RegisterEntry>();
            var refused = 0;

            for (var n = r.Next(2, 25); n > 0; n--)
            {
                // few numbers, so the two computers collide often
                var card = Card(r, r.Next(1, 6)) with { Replacement = false };
                var write = await computers[r.Next(2)].RecordShared(card);
                if (first.TryAdd(card.Id, card))
                {
                    Assert.True(write.Outcome == SharedOutcome.Sent, $"seed {seed}: the first card on {card.Id} was not taken");
                }
                else
                {
                    Assert.True(write.Outcome == SharedOutcome.Refused, $"seed {seed}: a second card on {card.Id} was {write.Outcome}, not refused");
                    Assert.True(write.Holder!.Name == first[card.Id].Name, $"seed {seed}: the refusal names the wrong holder for {card.Id}");
                    refused++;
                }
            }

            Assert.True(firebase.Docs.Count == first.Count, $"seed {seed}: {firebase.Docs.Count} documents for {first.Count} numbers");
            foreach (var (id, card) in first)
                Assert.True(firebase.Get(id)!.IssuedAt == card.IssuedAt && firebase.Get(id)!.Name == card.Name, $"seed {seed}: {id} no longer holds its first card");
            Assert.True(computers.Sum(c => c.Refused.Count) == refused, $"seed {seed}: a refused card was not kept for the operator");
            Assert.True(computers.All(c => c.WaitingForShared == 0), $"seed {seed}: a refused card was left in the outbox");
        }
    }
}
