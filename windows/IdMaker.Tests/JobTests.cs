// The jobs Core runs for the windows (Jobs.cs), with stand-ins for what a
// window would hand in: pages that are not really drawn, a decoder that
// reads no file, a register that exists only in memory.

using IdMaker.Core;

namespace IdMaker.Tests;

public class JobTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "idmaker-jobs-" + Guid.NewGuid().ToString("N"));

    public JobTests() { Directory.CreateDirectory(_folder); }

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>A store that says when something is written to it.</summary>
    private sealed class WatchedStore : IKeyValueStore
    {
        private readonly MemoryStore _inner = new();
        public Action<string>? Written { get; set; }
        public string? Get(string key) => _inner.Get(key);
        public void Set(string key, string? value) { Written?.Invoke(key); _inner.Set(key, value); }
    }

    private static PdfPage BlankPage() => new(1, 1, new byte[3], CardConfig.CardWidthMm, CardConfig.CardHeightMm);

    private static Session ReadyToPrint(Register register, IKeyValueStore store)
    {
        var s = Make.Session(register, store);
        Make.FillForm(s);
        s.GoStep(Session.LastStep);
        Assert.Equal(Session.LastStep, s.StepIndex);
        return s;
    }

    private static PrintJob JobOf(Session s)
    {
        var job = s.Print();
        Assert.NotNull(job);
        return job!;
    }

    [Fact]
    public async Task An_id_is_issued_only_after_the_pdf_is_saved()
    {
        var store = new WatchedStore();
        var register = Make.LocalRegister(store);
        var s = ReadyToPrint(register, store);
        var path = Path.Combine(_folder, "print.pdf");
        var events = new List<string>();
        // the register's own list is the key "register": every write to it is a card being issued
        store.Written = key =>
        {
            if (key == "register") lock (events) events.Add($"issued, with the PDF {(File.Exists(path) ? "saved" : "not saved")} and {(File.Exists(path + ".part") ? "still being written" : "whole")}");
        };
        var job = JobOf(s);
        Assert.Equal(1, job.Cards);
        Assert.Equal("APG-2026-124 Ana Reyes", job.Title);
        var pages = new List<Func<PdfPage>>
        {
            () => { lock (events) events.Add($"page drawn, {register.All().Count} issued"); return BlankPage(); },
        };

        var saved = await s.SavePrint(path, job, pages);

        Assert.Equal("", saved.Error);
        Assert.Equal(1, saved.Cards);
        Assert.Equal("", await saved.Trouble);
        Assert.Equal(new[] { "page drawn, 0 issued", "issued, with the PDF saved and whole" }, events);
        Assert.True(new FileInfo(path).Length > 100);
        Assert.Equal("APG-2026-124", Assert.Single(register.All()).Id);
        Assert.Empty(s.Queue);
    }

    [Fact]
    public async Task A_print_that_cannot_be_saved_issues_nothing()
    {
        var firebase = new FakeFirebase();
        var store = new MemoryStore();
        var register = await Make.SignedIn(firebase, store);
        var s = ReadyToPrint(register, store);
        await s.DuplicateCheck;
        var creates = firebase.Creates;
        var nowhere = Path.Combine(_folder, "no-such-folder", "print.pdf");

        var saved = await s.SavePrint(nowhere, JobOf(s), new List<Func<PdfPage>> { BlankPage });

        Assert.StartsWith("The file couldn’t be saved there (", saved.Error);
        Assert.EndsWith("). If it is open in another program, close it, or choose a different folder.", saved.Error);
        Assert.Equal(0, saved.Cards);
        Assert.Equal("", await saved.Trouble);
        Assert.Empty(register.All());
        Assert.Equal(creates, firebase.Creates);
        Assert.Equal(0, register.WaitingForShared);
        // and the card is still there to be saved somewhere else
        Assert.Single(s.JobCards());
    }

    [Fact]
    public async Task A_page_that_cannot_be_drawn_fails_the_save_and_leaves_no_half_file()
    {
        var store = new MemoryStore();
        var register = Make.LocalRegister(store);
        var s = ReadyToPrint(register, store);
        var path = Path.Combine(_folder, "print.pdf");

        var saved = await s.SavePrint(path, JobOf(s), new List<Func<PdfPage>> { () => throw new InvalidOperationException("A page of the print could not be drawn.") });

        // said as what it is: the folder is not at fault, so there is no advice about the file being open elsewhere
        Assert.Equal("A page of this print couldn’t be drawn (A page of the print could not be drawn). Nothing was saved. Try again; if it happens again, close the app and open it again.", saved.Error);
        Assert.Equal(0, saved.Cards);
        Assert.Empty(register.All());
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task A_slip_in_page_drawing_is_not_reported_as_a_file_open_in_another_program()
    {
        var path = Path.Combine(_folder, "batch.pdf");
        int? none = null;

        // what a programming error in a renderer looks like from here: the same exception type a window uses to say "could not draw"
        var error = await PrintFiles.Write(path, new List<Func<PdfPage>> { BlankPage, () => { _ = none!.Value; return BlankPage(); } }, "Batch", new DateTime(2026, 11, 17));

        Assert.StartsWith("A page of this print couldn’t be drawn (", error);
        Assert.DoesNotContain("another program", error);
        Assert.DoesNotContain("different folder", error);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task A_failure_of_another_kind_in_page_drawing_is_not_caught_as_a_save_that_failed()
    {
        var path = Path.Combine(_folder, "batch.pdf");

        // not something the operator can act on, so it is not turned into a message here: it reaches the app's own last-error report
        await Assert.ThrowsAsync<FormatException>(() =>
            PrintFiles.Write(path, new List<Func<PdfPage>> { () => throw new FormatException("Not a number.") }, "Batch", new DateTime(2026, 11, 17)));

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task A_saved_print_whose_cards_cannot_reach_the_shared_register_says_so_afterwards()
    {
        var firebase = new FakeFirebase();
        var store = new MemoryStore();
        var register = await Make.SignedIn(firebase, store);
        var s = ReadyToPrint(register, store);
        await s.DuplicateCheck;
        firebase.Offline = true;

        var saved = await s.SavePrint(Path.Combine(_folder, "print.pdf"), JobOf(s), new List<Func<PdfPage>> { BlankPage });

        Assert.Equal("", saved.Error);
        Assert.Single(register.All());
        Assert.StartsWith("APG-2026-124 was not written to the shared register", await saved.Trouble);
        Assert.Equal(1, register.WaitingForShared);
    }

    /// <summary>Two cards in one print: Reyes (124) queued, Cruz (125) on screen.</summary>
    private static Session TwoCardsToPrint(Register register, IKeyValueStore store)
    {
        var s = ReadyToPrint(register, store);
        Assert.True(s.AddAnother());
        Make.FillForm(s, "125");
        s.SetNamePart(Field.Last, "Cruz");
        s.GoStep(Session.LastStep);
        Assert.Equal(Session.LastStep, s.StepIndex);
        return s;
    }

    // The form stays live while the file is written: only Save, the pictures
    // and "Add another ID" are off. The page function below stands in for the
    // operator doing something in that time.

    [Fact]
    public async Task A_queued_card_removed_while_the_file_is_written_is_still_issued_as_printed()
    {
        var store = new MemoryStore();
        var register = Make.LocalRegister(store);
        var s = TwoCardsToPrint(register, store);
        var job = JobOf(s);
        Assert.Equal(2, job.Cards);
        var pages = new List<Func<PdfPage>> { () => { s.RemoveFromQueue(0); return BlankPage(); } };

        var saved = await s.SavePrint(Path.Combine(_folder, "print.pdf"), job, pages);

        Assert.Equal("", saved.Error);
        Assert.Equal(2, saved.Cards);
        // the PDF holds both cards, so both numbers are taken
        Assert.Equal(new[] { "APG-2026-124 Ana Reyes", "APG-2026-125 Ana Cruz" }, register.All().OrderBy(e => e.Seq).Select(e => $"{e.Id} {e.Name}"));
        Assert.Empty(s.Queue);
    }

    [Fact]
    public async Task A_name_typed_again_while_the_file_is_written_does_not_change_what_is_issued()
    {
        var store = new MemoryStore();
        var register = Make.LocalRegister(store);
        var s = TwoCardsToPrint(register, store);
        var job = JobOf(s);
        var pages = new List<Func<PdfPage>>
        {
            () =>
            {
                s.GoStep(0);
                s.SetNamePart(Field.Last, "Santos");
                s.GoStep(Session.LastStep);
                return BlankPage();
            },
        };

        var saved = await s.SavePrint(Path.Combine(_folder, "print.pdf"), job, pages);

        Assert.Equal("", saved.Error);
        // the paper says Cruz
        Assert.Equal(new[] { "APG-2026-124 Ana Reyes", "APG-2026-125 Ana Cruz" }, register.All().OrderBy(e => e.Seq).Select(e => $"{e.Id} {e.Name}"));
        Assert.Equal("Cruz", Assert.Single(register.All(), e => e.Seq == 125).Last);
        // and the form now says that number is taken, by the card that was printed
        Assert.Equal("Ana Cruz", s.Duplicate()?.Entry.Name);
    }

    [Fact]
    public void A_print_job_carries_the_cards_it_will_issue()
    {
        var store = new MemoryStore();
        var s = TwoCardsToPrint(Make.LocalRegister(store), store);

        var job = JobOf(s);

        Assert.False(job.Issue.Example);
        Assert.Equal(new[] { "APG-2026-124", "APG-2026-125" }, job.Issue.Cards.Select(c => c.Entry.Id));
        // the very cards the pages are drawn from, in the same order (one copy of each here)
        Assert.Equal(2, job.Entries.Count);
        Assert.Same(job.Issue.Cards[0].Data, job.Entries[0]);
        Assert.Same(job.Issue.Cards[1].Data, job.Entries[1]);
    }

    [Fact]
    public void With_nothing_to_print_there_is_no_print_job()
    {
        Assert.Null(Make.Session().Print());
    }

    [Fact]
    public void Pictures_of_the_card_are_named_as_a_pair()
    {
        var front = Path.Combine(_folder, "APG-2026-124_Reyes-front.png");
        var back = PrintFiles.BackPictureFor(front, "elsewhere");
        Assert.Equal(Path.Combine(_folder, "APG-2026-124_Reyes-back.png"), back);
        Assert.Equal(Path.Combine(_folder, "proof-back.png"), PrintFiles.BackPictureFor(Path.Combine(_folder, "proof.png"), "elsewhere"));
        Assert.Equal("Saved APG-2026-124_Reyes-front.png and APG-2026-124_Reyes-back.png. The card is not issued until you save the PDF.", PrintFiles.PicturesSaved(front, back));
        Assert.Equal("The pictures couldn’t be saved there (Disk full). Choose a different folder.", PrintFiles.PicturesNotSaved(new IOException("Disk full.")));
    }

    [Fact]
    public async Task Signing_in_reads_the_register_sends_what_was_waiting_and_checks_the_number_again()
    {
        var firebase = new FakeFirebase();
        var store = new MemoryStore();
        var register = Make.SharedRegister(firebase, store);
        var s = Make.Session(register, store);
        Assert.True(s.HasSharedRegister);
        firebase.Put(new RegisterEntry { Id = "APG-2026-124", Year = 2026, Seq = 124, Name = "Ben Cruz", IssuedAt = "2026-11-16T01:30:00.000Z" });
        var waiting = new RegisterEntry { Id = "APG-2026-200", Year = 2026, Seq = 200, Name = "Ana Reyes", IssuedAt = "2026-11-17T01:30:00.000Z" };
        Assert.Equal(SharedOutcome.Waiting, (await register.RecordShared(waiting)).Outcome);
        s.SetSeq("124");
        await s.DuplicateCheck;
        Assert.Equal(SharedAnswer.Unreachable, s.DupShared);

        var wrong = await s.SignIn(FakeFirebase.Email, "not the password");
        Assert.False(wrong.Ok);
        Assert.StartsWith("That email or password is not right.", wrong.Message);
        Assert.False(register.SignedIn);

        var right = await s.SignIn(FakeFirebase.Email, FakeFirebase.Password);
        Assert.True(right.Ok);
        Assert.Equal("Signed in. This computer now checks every number against the shared register.", right.Message);
        Assert.NotNull(firebase.Get("APG-2026-200"));
        Assert.Equal(0, register.WaitingForShared);
        // the number on the form is now known to be somebody else's
        Assert.Equal(SharedAnswer.Taken, s.DupShared);

        var check = await s.CheckRegister();
        Assert.Equal("The shared register is answering: 2 cards in it.", check);

        Assert.Equal("Signed out. Numbers are checked against this computer only until somebody signs in.", s.SignOut());
        Assert.False(register.SignedIn);
        Assert.Equal(Register.SignInFirst, await s.CheckRegister());
    }

    [Fact]
    public async Task Start_up_sends_what_was_waiting_and_says_when_a_card_was_refused()
    {
        var firebase = new FakeFirebase();
        var store = new MemoryStore();
        var keeper = new FakeKeeper();
        var register = await Make.SignedIn(firebase, store, keeper);
        var mine = new RegisterEntry { Id = "APG-2026-124", Year = 2026, Seq = 124, Name = "Ana Reyes", IssuedAt = "2026-11-17T01:30:00.000Z" };
        firebase.Offline = true;
        Assert.Equal(SharedOutcome.Waiting, (await register.RecordShared(mine)).Outcome);
        firebase.Offline = false;

        // nothing refused: the list stays shut
        var quiet = Make.Session(await Make.SignedIn(new FakeFirebase()));
        Assert.False(await quiet.WarmRegister());

        // meanwhile somebody else took the number
        firebase.Put(mine with { Name = "Ben Cruz", IssuedAt = "2026-11-16T01:30:00.000Z" });
        var restarted = Make.SharedRegister(firebase, store, keeper);
        var s = Make.Session(restarted, store);
        Assert.True(await s.WarmRegister());
        Assert.Single(restarted.Refused);
        Assert.Equal(0, restarted.WaitingForShared);
    }

    [Fact]
    public void A_saved_list_brought_in_is_counted_and_the_number_checked_again()
    {
        var other = Make.LocalRegister();
        other.Record(new RegisterEntry { Id = "APG-2026-124", Year = 2026, Seq = 124, Name = "Ben Cruz", IssuedAt = "2026-11-16T01:30:00.000Z" });
        var s = Make.Session();
        s.SetSeq("124");
        Assert.Null(s.Duplicate());

        Assert.Equal("Added 1 card from that list.", s.ImportRegister(other.ToCsv()));
        Assert.NotNull(s.Duplicate());
        Assert.Equal("Nothing new in that list.", s.ImportRegister(other.ToCsv()));
        Assert.Equal(other.ToCsv(), s.RegisterCsv());
    }

    [Fact]
    public async Task A_batch_s_rows_are_issued_here_at_once_and_then_in_the_shared_register()
    {
        var firebase = new FakeFirebase();
        var store = new MemoryStore();
        var register = await Make.SignedIn(firebase, store);
        var s = Make.Session(register, store);
        Assert.True(await s.ReadSharedRegister());
        s.SetSeq("124");
        Assert.Null(s.Duplicate());
        var rows = new[]
        {
            new RegisterEntry { Id = "APG-2026-124", Year = 2026, Seq = 124, Name = "Ana Reyes", IssuedAt = "2026-11-17T01:30:00.000Z" },
            new RegisterEntry { Id = "APG-2026-125", Year = 2026, Seq = 125, Name = "Ben Cruz", IssuedAt = "2026-11-17T01:30:00.000Z" },
        };

        var telling = s.IssueBatch(rows);
        // before the shared register has answered: recorded here, and the number on the form already counts as taken
        Assert.Equal(2, register.All().Count);
        Assert.NotNull(s.Duplicate());
        Assert.Equal("", await telling);
        Assert.NotNull(firebase.Get("APG-2026-124"));
        Assert.NotNull(firebase.Get("APG-2026-125"));

        Assert.True(await Make.Session().ReadSharedRegister());         // no shared register: nothing to read, nothing wrong
        firebase.Failing = System.Net.HttpStatusCode.InternalServerError;
        Assert.False(await Make.Session(await SignedInBefore(firebase)).ReadSharedRegister());
    }

    // signed in while the register still answered
    private static async Task<Register> SignedInBefore(FakeFirebase firebase)
    {
        var failing = firebase.Failing;
        firebase.Failing = null;
        var register = await Make.SignedIn(firebase);
        firebase.Failing = failing;
        return register;
    }

    [Fact]
    public async Task A_photo_is_looked_at_once_and_lands_only_on_the_card_it_was_for()
    {
        var s = Make.Session();
        var changes = 0;
        s.Changed += () => changes++;
        var seen = new List<bool>();
        (RgbaImage?, string) NotAPicture(string path) { seen.Add(s.CheckingPhoto); return (null, "That file is not a picture."); }

        // turned away by its name, before anything is opened
        Assert.True(await s.TakePhoto("holiday.heic", NotAPicture, null));
        Assert.StartsWith("This is an iPhone HEIC photo", s.PhotoError);
        Assert.Empty(seen);

        // opened, and not a picture
        Assert.True(await s.TakePhoto(Path.Combine(_folder, "ana.png"), NotAPicture, null));
        Assert.Equal("That file is not a picture.", s.PhotoError);
        Assert.Equal(new[] { true }, seen);
        Assert.False(s.CheckingPhoto);
        Assert.Equal(1, changes);

        // a real cut-out is put through the pipeline
        var cutOut = Make.Silhouette(600, 1000, headTop: 60, headW: 200, headH: 260, neckW: 90, neckH: 40, bodyW: 520);
        Assert.True(await s.TakePhoto("ana.png", _ => (cutOut, ""), null));
        Assert.True(s.Photo is not null || s.PhotoError.Length > 0);

        // the card was put aside while the photo was being looked at: it lands nowhere
        var fresh = Make.Session();
        Assert.False(await fresh.TakePhoto("ana.png", _ => { fresh.NewId(); return (cutOut, ""); }, null));
        Assert.Null(fresh.Photo);
        Assert.Equal("", fresh.PhotoError);
        Assert.False(fresh.CheckingPhoto);
    }

    [Fact]
    public async Task A_second_photo_is_not_taken_while_the_first_is_being_looked_at()
    {
        var s = Make.Session();
        using var gate = new ManualResetEventSlim();
        var first = s.TakePhoto("ana.png", _ => { gate.Wait(); return (null, "first"); }, null);
        Assert.True(s.CheckingPhoto);
        Assert.False(await s.TakePhoto("ben.png", _ => (null, "second"), null));
        gate.Set();
        Assert.True(await first);
        Assert.Equal("first", s.PhotoError);
    }

    [Fact]
    public async Task A_scan_that_cannot_be_read_leaves_the_signature_that_was_there()
    {
        var s = Make.Session();
        s.SetSignature(Session.ExampleSignature());
        var before = s.Signature;

        var wrongKind = await s.TakeSignatureScan("signature.pdf", _ => (null, "never opened"));
        Assert.True(wrongKind.Landed);
        Assert.Equal("Please choose a PNG, JPG or WebP picture of the signature.", wrongKind.Error);
        var unreadable = await s.TakeSignatureScan("signature.png", _ => (null, "That file is not a picture."));
        Assert.Equal(new ScanTaken(true, "That file is not a picture."), unreadable);
        Assert.Same(before, s.Signature);

        var gone = await s.TakeSignatureScan("signature.png", _ => { s.NewId(); return (null, "too late"); });
        Assert.Equal(new ScanTaken(false, ""), gone);
    }

    [Fact]
    public void The_form_s_map_follows_every_change_to_the_design()
    {
        var store = new MemoryStore();
        var s = Make.Session(store: store);
        var templates = new TemplateStore(store);
        var desk = new DesignDesk(s, templates, _folder);

        // looked, moved nothing: nothing is kept
        Assert.True(desk.Begin());
        templates.BeginEditing();
        Assert.Equal("Nothing was moved.", desk.Keep());
        Assert.False(templates.IsCustom);

        // moved and kept
        Assert.True(desk.Begin());
        var start = templates.Box(BoxKey.Name);
        templates.SetBox(BoxKey.Name, start with { Y = start.Y - 2 });
        desk.Apply();
        Assert.Equal(start.Y - 2, s.Geometry.Name.Box.Y, 6);
        Assert.Equal("Saved on this computer.", desk.Keep());
        Assert.True(new TemplateStore(store).Restore());

        // moved again and discarded: back to what was kept, in the store and on the form
        Assert.True(desk.Begin());
        templates.SetBox(BoxKey.Name, start with { Y = start.Y - 6 });
        Assert.Equal("Left as it was.", desk.Discard());
        Assert.Equal(start.Y - 2, templates.Box(BoxKey.Name).Y, 6);
        Assert.Equal(start.Y - 2, s.Geometry.Name.Box.Y, 6);

        desk.ResetBoxes();
        Assert.Equal(start.Y, s.Geometry.Name.Box.Y, 6);

        // back to the built-in card: the map and the stored pictures go
        File.WriteAllText(desk.StoredArtwork("front"), "front");
        File.WriteAllText(desk.StoredArtwork("back"), "back");
        Assert.Equal("Back to the built-in design.", desk.BackToBuiltIn());
        Assert.False(templates.IsCustom);
        Assert.Empty(Directory.GetFiles(_folder));
        Assert.Same(CardGeometry.BuiltIn, s.Geometry);
        Assert.Equal("Back to the built-in design.", desk.BackToBuiltIn());     // nothing left to remove is not an error
    }

    [Fact]
    public void The_design_cannot_be_changed_once_a_card_is_started()
    {
        var store = new MemoryStore();
        var s = Make.Session(store: store);
        var desk = new DesignDesk(s, new TemplateStore(store), _folder);
        s.SetNamePart(Field.Last, "Reyes");
        Assert.False(desk.Begin());
    }

    [Fact]
    public void New_artwork_is_named_after_its_file_and_the_form_takes_its_map()
    {
        var store = new MemoryStore();
        var s = Make.Session(store: store);
        var templates = new TemplateStore(store);
        var desk = new DesignDesk(s, templates, _folder);
        Assert.Equal("anniversary card 2027", DesignDesk.NameFromFile(Path.Combine("art", "anniversary-card_2027.png")));

        var said = desk.Adopt(Path.Combine("art", "anniversary-card_2027.png"), (TemplateStore.ArtworkWidth, TemplateStore.ArtworkHeight), (TemplateStore.ArtworkWidth, TemplateStore.ArtworkHeight));
        Assert.True(templates.HasArtwork);
        Assert.Contains("anniversary card 2027", templates.DesignName);
        Assert.Equal(templates.Note.Length > 0 ? templates.Note : "In use. Check the map against the new artwork.", said);
        Assert.True(new TemplateStore(store).Restore());
        Assert.Equal("That artwork couldn’t be used (Not a PNG). Save it as PNG and try again.", DesignDesk.ArtworkNotUsable(new IOException("Not a PNG.")));
    }
}
