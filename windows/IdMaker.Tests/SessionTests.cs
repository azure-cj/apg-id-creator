using System.Net;
using IdMaker.Core;

namespace IdMaker.Tests;

public class StepTests
{
    [Fact]
    public void No_step_opens_over_an_unfinished_one()
    {
        var s = Make.Session();
        Assert.Equal(0, s.FurthestStep());

        // every way forward from an empty form stays on step 1
        s.NextStep();
        Assert.Equal(0, s.StepIndex);
        for (var i = 1; i <= Session.LastStep; i++)
        {
            s.ChipPressed(i);
            Assert.Equal(0, s.StepIndex);
        }
        s.GoStep(4);
        Assert.Equal(0, s.StepIndex);
    }

    [Fact]
    public void A_refusal_names_the_reason_and_points_at_the_field()
    {
        var s = Make.Session();
        var r = s.NextStep();
        Assert.Equal(Field.Last, r.FocusField);
        Assert.Equal("Please complete the highlighted fields.", s.BlockedText);
        Assert.Equal(0, s.BlockedAbout);

        // pressing a locked chip from a finished step names the step in the way
        s.SetNamePart(Field.Last, "Reyes");
        s.SetNamePart(Field.First, "Ana");
        Assert.Null(s.NextStep().FocusField);
        Assert.Equal(1, s.StepIndex);
        var chip = s.ChipPressed(4);
        Assert.Equal(1, s.StepIndex);
        // the first thing the step asks is who the ID is for
        Assert.Equal(Field.Kind, chip.FocusField);
        // one line on every step; the fields themselves are marked
        Assert.Equal("Please complete the highlighted fields.", s.BlockedText);
        s.PickKind(PersonKind.Intern);
        Assert.Equal(Field.Title, s.ChipPressed(4).FocusField);

        // and it clears once the thing it named is fixed: the job, and the number on the same step
        s.PickRole1("HR Associate");
        s.SetSeq("124");
        s.ClearBlockedIfFixed(s.Problems());
        Assert.Null(s.BlockedAbout);
        Assert.Equal("", s.BlockedText);
    }

    [Fact]
    public void Who_the_id_is_for_is_asked_never_guessed_and_goes_on_the_record()
    {
        var store = new MemoryStore();
        var s = Make.Session(Make.LocalRegister(store), store);
        Make.FillForm(s);
        Assert.Equal(PersonKind.Employee, s.Kind);

        // anything but the two choices is no choice, and the form does not go past the step without one
        s.PickKind("contractor");
        Assert.Equal("", s.Kind);
        Assert.Equal("Required.", s.Problems()[Field.Kind]);
        s.GoStep(Session.LastStep);
        Assert.Equal((int)Step.Title, s.StepIndex);

        s.PickKind(" Intern ");
        Assert.Equal(PersonKind.Intern, s.Kind);
        Assert.Equal("", s.Problems()[Field.Kind]);
        s.GoStep(Session.LastStep);
        Assert.Equal(Session.LastStep, s.StepIndex);
        var issued = Assert.Single(s.MarkIssued());
        Assert.Equal(PersonKind.Intern, issued.Kind);

        // the next card starts with nothing chosen: the last answer is not carried over
        s.NewId();
        Assert.Equal("", s.Kind);
    }

    [Fact]
    public void A_finished_form_walks_to_the_end_and_back_is_always_allowed()
    {
        var s = Make.Session();
        Make.FillForm(s);
        for (var i = 0; i < Session.LastStep; i++) Assert.Null(s.NextStep().FocusField);
        Assert.Equal(Session.LastStep, s.StepIndex);
        Assert.NotNull(s.ReviewEntry);
        Assert.Equal("APG-2026-124", s.ReviewEntry!.Id);
        s.Back();
        Assert.Equal(Session.LastStep - 1, s.StepIndex);
        s.GoStep(0);
        Assert.Equal(0, s.StepIndex);
    }

    [Fact]
    public void Something_behind_coming_undone_sends_the_form_back_to_it()
    {
        var s = Make.Session();
        Make.FillForm(s);
        s.GoStep(4);
        Assert.Equal(4, s.StepIndex);
        // the name stops fitting the card
        s.SetNamePart(Field.First, "Bartholomew Maximilian Featherstonehaugh Cholmondeley Marjoribanks");
        var r = s.NextStep();
        Assert.Equal(0, s.StepIndex);
        Assert.Equal(Field.NameFit, r.FocusField);
    }

    [Fact]
    public void Until_the_fonts_are_there_a_name_is_not_judged_for_fit()
    {
        var s = Make.Session(measurer: false);
        s.SetNamePart(Field.Last, "Marjoribanks");
        s.SetNamePart(Field.First, "Bartholomew Maximilian Featherstonehaugh Cholmondeley");
        Assert.Equal("", s.Problems()[Field.NameFit]);
        s.Measurer = new FakeMeasurer();
        Assert.NotEqual("", s.Problems()[Field.NameFit]);
    }

    [Fact]
    public void Fields_say_what_is_wrong_with_them()
    {
        var s = Make.Session();
        s.SetNamePart(Field.Last, "Reyes2");
        s.SetNamePart(Field.Suffix, "Jr!");
        s.PickRole1(RoleChoice.Other);
        var p = s.Problems();
        Assert.Contains("letters", p[Field.Last]);
        Assert.Contains("Jr., Sr.", p[Field.Suffix]);
        Assert.Equal("Required.", p[Field.Title]);
        s.TypeRole1("R&D <lead>");
        Assert.Contains("letters, numbers", s.Problems()[Field.Title]);
        s.TypeRole1("R&D Lead (Acting)");
        Assert.Equal("", s.Problems()[Field.Title]);

        Assert.Equal("12", s.SetSeq("1a2"));
        Assert.Equal("1234", s.SetSeq("123456"));
        Assert.Equal("", s.Problems()[Field.Seq]);
        s.SetSeq("0");
        Assert.Contains("1 to 4 digits", s.Problems()[Field.Seq]);
    }

    [Fact]
    public void A_second_role_cannot_repeat_the_first()
    {
        var s = Make.Session();
        s.PickRole1("IT Associate");
        s.AddSecondRole();
        Assert.Equal("Required, or remove the second position.", s.Problems()[Field.Title2]);
        s.PickRole2("HR Associate");
        Assert.Equal("IT/HR Associate", s.JobTitle);
        s.PickRole1("HR Associate");                 // now the same as the second: the second is cleared
        Assert.Equal("", s.Role2.Pick);
        s.PickRole2(RoleChoice.Other);
        s.TypeRole2("hr associate");
        Assert.StartsWith("Same as the position above", s.Problems()[Field.Title2]);
        s.RemoveSecondRole();
        Assert.Equal("", s.Problems()[Field.Title2]);
        Assert.Equal("HR Associate", s.JobTitle);
    }

    [Fact]
    public void Leaving_a_name_field_tidies_capitals_typed_without_shift()
    {
        var s = Make.Session();
        s.SetNamePart(Field.Last, "dela cruz");
        Assert.Equal("Dela Cruz", s.LeaveNamePart(Field.Last));
        s.SetNamePart(Field.Last, "dela Cruz");
        Assert.Equal("dela Cruz", s.LeaveNamePart(Field.Last));
        s.SetNamePart(Field.Suffix, "iii");
        Assert.Equal("III", s.LeaveNamePart(Field.Suffix));

        // and moving on tidies them too, for when the field was never "left"
        var t = Make.Session();
        t.SetNamePart(Field.Last, "reyes");
        t.SetNamePart(Field.First, "ana maria");
        t.NextStep();
        Assert.Equal(1, t.StepIndex);
        Assert.Equal("Reyes", t.Last);
        Assert.Equal("Ana Maria", t.First);
    }

    [Fact]
    public void A_photo_that_cannot_be_used_keeps_the_step_shut_and_says_why()
    {
        var s = Make.Session();
        s.AcceptPhoto(new PhotoResult(new RgbaImage(2, 2), "a.jpg", PhotoBackground.Other, new PhotoCheck { Problem = "Nope.", FixIsPrompt = true }));
        Assert.Null(s.Photo);
        Assert.Equal("Nope.", s.Problems()[Field.Photo]);
        Assert.True(s.PhotoFixIsPrompt);
        s.PhotoFailed("Could not open.");
        Assert.Equal("Could not open.", s.Problems()[Field.Photo]);
        s.AcceptPhoto(Make.Photo());
        Assert.Equal("", s.Problems()[Field.Photo]);
    }

    [Fact]
    public void Manual_adjustment_is_clamped_and_resets()
    {
        var s = Make.Session();
        s.Nudge(1, 1);
        Assert.True(s.Adjust.IsNone);                // nothing to adjust without a photo
        s.AcceptPhoto(Make.Photo());
        s.Nudge(0.5, -0.5);
        s.SetZoom(9);
        Assert.Equal(new PhotoAdjust(0.5, -0.5, CardConfig.Photo.ZoomMax), s.Adjust);
        s.SetZoom(0.01);
        Assert.Equal(CardConfig.Photo.ZoomMin, s.Adjust.Zoom);
        s.ResetAdjust();
        Assert.True(s.Adjust.IsNone);
    }
}

public class DuplicateTests
{
    private static readonly RegisterEntry Taken = new()
    {
        Id = "APG-2026-124", Year = 2026, Seq = 124, Name = "Juan dela Cruz", Title = "Driver", IssuedAt = "2026-11-03T02:00:00.000Z",
    };

    /// <summary>A form on a computer signed in to the shared register, which holds one card.</summary>
    private static (Session Session, FakeFirebase Firebase, Register Register) Shared(bool taken = true)
    {
        var store = new MemoryStore();
        var firebase = new FakeFirebase();
        if (taken) firebase.Put(Taken);
        var register = Make.SignedIn(firebase, store).GetAwaiter().GetResult();
        return (Make.Session(register, store), firebase, register);
    }

    /// <summary>The same, with the register slow to answer: it answers when the gate is set.</summary>
    private static (Session Session, FakeFirebase Firebase, Register Register, TaskCompletionSource Gate) Slow(bool taken = true)
    {
        var (s, firebase, register) = Shared(taken);
        var gate = new TaskCompletionSource();
        firebase.ListGate = gate.Task;
        return (s, firebase, register, gate);
    }

    [Fact]
    public async Task A_number_in_the_shared_register_is_refused_and_names_who_has_it()
    {
        var (s, firebase, _) = Shared();
        s.SetSeq("124");
        await s.DuplicateCheck;
        Assert.Equal(SharedAnswer.Taken, s.DupShared);
        var problem = s.Problems()[Field.Seq];
        Assert.Contains("APG-2026-124 was already issued to Juan dela Cruz", problem);
        Assert.Contains("in the shared register", problem);

        // a replacement card is the way through
        s.SetReissue(true);
        Assert.Equal("", s.Problems()[Field.Seq]);
        Assert.Equal(2, firebase.Lists);                     // signing in looked once; the form read the list once
    }

    [Fact]
    public async Task A_free_number_opens_and_later_numbers_are_answered_from_memory()
    {
        var (s, firebase, _) = Shared();
        s.SetSeq("125");
        await s.DuplicateCheck;
        Assert.Equal(SharedAnswer.Free, s.DupShared);
        Assert.Equal("", s.Problems()[Field.Seq]);

        s.SetSeq("126");
        s.SetSeq("124");
        Assert.Equal(SharedAnswer.Taken, s.DupShared);       // straight away: no waiting, no request
        Assert.Equal(2, firebase.Lists);
    }

    [Fact]
    public async Task While_the_register_has_not_answered_the_number_is_not_cleared()
    {
        var (s, _, _, gate) = Slow(taken: false);
        Make.FillForm(s, "124");
        Assert.Equal("Checking the shared register for this number…", s.Problems()[Field.Seq]);
        // a fast typist pressing Next cannot get past the number's step
        s.GoStep(Session.LastStep);
        Assert.Equal((int)Step.Title, s.StepIndex);

        gate.SetResult();
        await s.DuplicateCheck;
        Assert.Equal("", s.Problems()[Field.Seq]);
        s.GoStep(Session.LastStep);
        Assert.Equal(Session.LastStep, s.StepIndex);
    }

    [Fact]
    public async Task A_register_that_cannot_be_reached_warns_but_lets_work_carry_on()
    {
        var (s, firebase, register) = Shared();
        firebase.Failing = HttpStatusCode.InternalServerError;
        s.SetSeq("124");
        await s.DuplicateCheck;
        Assert.Equal(SharedAnswer.Unreachable, s.DupShared);
        Assert.Equal("", s.Problems()[Field.Seq]);
        Assert.Contains("HTTP 500", register.LastProblem);
    }

    [Fact]
    public async Task A_computer_nobody_has_signed_in_on_warns_but_lets_work_carry_on()
    {
        var store = new MemoryStore();
        var register = Make.SharedRegister(new FakeFirebase(), store);
        var s = Make.Session(register, store);
        s.SetSeq("124");
        await s.DuplicateCheck;
        Assert.Equal(SharedAnswer.Unreachable, s.DupShared);
        Assert.Equal("", s.Problems()[Field.Seq]);
        Assert.Equal(Register.SignInFirst, register.LastProblem);
    }

    [Fact]
    public async Task An_answer_for_a_number_that_has_since_changed_is_ignored()
    {
        var (s, _, _, gate) = Slow();
        s.SetSeq("124");
        var first = s.DuplicateCheck;
        s.SetSeq("");
        gate.SetResult();
        await first;
        Assert.Equal(SharedAnswer.Free, s.DupShared);       // an empty box has no duplicate
        Assert.Equal("Required.", s.Problems()[Field.Seq]);
    }

    [Fact]
    public async Task A_duplicate_found_after_the_form_moved_on_brings_it_back_to_the_number()
    {
        var (s, _, register, gate) = Slow();
        // the register was unreachable a moment ago, so the form was let through
        Make.FillForm(s, "124");
        var changed = 0;
        s.Changed += () => changed++;
        s.GoStep((int)Step.Sign, force: true);
        gate.SetResult();
        await s.DuplicateCheck;
        Assert.Equal((int)Step.Title, s.StepIndex);
        Assert.Equal((int)Step.Title, s.BlockedAbout);
        Assert.True(changed > 0);
        Assert.True(register.IndexLoaded);
    }

    [Fact]
    public async Task A_late_answer_does_not_throw_the_operator_out_of_a_print_that_is_already_queued()
    {
        var (s, _, _, gate) = Slow(taken: false);
        // one card banked (the register was not answering, so it was let through), then "stop adding"
        Make.FillForm(s, "200");
        s.GoStep(Session.LastStep, force: true);
        s.Queue.Add(s.JobCards().Count > 0 ? s.JobCards()[0] with { Current = false }
            : new JobCard("APG-2026-200", "Ana Reyes", "HR Associate", s.CardData(), new RegisterEntry { Id = "APG-2026-200", Year = 2026, Seq = 200, Name = "Ana Reyes" }, false));
        s.CancelAdding();
        Assert.Equal(Session.LastStep, s.StepIndex);
        Assert.False(s.StartedCard);

        gate.SetResult();
        await s.DuplicateCheck;
        // the answer was for a card that has been put aside: it is dropped, not applied to the blank one
        Assert.Equal(Session.LastStep, s.StepIndex);
        Assert.Equal(SharedAnswer.Free, s.DupShared);
        Assert.Null(s.BlockedAbout);
    }

    [Fact]
    public void Putting_the_card_aside_changes_its_serial_so_late_work_for_it_can_stand_down()
    {
        var s = Make.Session();
        var before = s.CardSerial;
        s.SetNamePart(Field.Last, "Reyes");
        Assert.Equal(before, s.CardSerial);          // typing is the same card
        s.ClearCard();
        Assert.NotEqual(before, s.CardSerial);
        var second = s.CardSerial;
        s.NewId();
        Assert.NotEqual(second, s.CardSerial);
    }

    [Fact]
    public void A_number_issued_on_this_computer_is_caught_without_any_network()
    {
        var store = new MemoryStore();
        var register = Make.LocalRegister(store);
        register.Record(new RegisterEntry { Id = "APG-2026-124", Year = 2026, Seq = 124, Name = "Ana Reyes", IssuedAt = "2026-11-01T00:00:00.000Z" });
        var s = Make.Session(register, store);
        s.SetSeq("124");
        Assert.Contains("on this computer", s.Problems()[Field.Seq]);
        s.SetYear(2025);
        Assert.Equal("", s.Problems()[Field.Seq]);
    }
}

public class PrintJobTests
{
    [Fact]
    public void Adding_another_id_banks_the_card_and_its_number_cannot_be_used_twice_in_one_print()
    {
        var s = Make.Session();
        Make.FillForm(s, "124");
        s.GoStep(Session.LastStep);
        Assert.True(s.AddAnother());
        Assert.Single(s.Queue);
        Assert.Equal(0, s.StepIndex);
        Assert.False(s.StartedCard);
        Assert.False(s.DesignAllowed);                   // a print is in progress

        Make.FillForm(s, "124");
        Assert.Contains("already waiting in this print", s.Problems()[Field.Seq]);
        s.SetSeq("125");
        s.GoStep(Session.LastStep);
        Assert.Equal(new[] { "APG-2026-124", "APG-2026-125" }, s.JobCards().Select(c => c.Id));
        Assert.True(s.JobCards()[1].Current);
        Assert.Equal("apg-ids-2-2026-11-17.pdf", s.PrintFile().FileName);
    }

    [Fact]
    public void Cancelling_an_extra_id_returns_to_the_print_that_was_waiting()
    {
        var s = Make.Session();
        Make.FillForm(s, "124");
        s.GoStep(Session.LastStep);
        s.AddAnother();
        s.SetNamePart(Field.Last, "Half");
        s.CancelAdding();
        Assert.Equal(Session.LastStep, s.StepIndex);
        Assert.False(s.StartedCard);
        Assert.Single(s.JobCards());
        Assert.Equal("APG-2026-124_Reyes.pdf", s.PrintFile().FileName);

        // taking the last card out leaves nothing to print, so the form starts again
        s.RemoveFromQueue(0);
        Assert.Equal(0, s.StepIndex);
    }

    [Fact]
    public void Saving_issues_every_card_in_the_print_and_empties_the_queue()
    {
        var store = new MemoryStore();
        var register = Make.LocalRegister(store);
        var s = Make.Session(register, store);
        Make.FillForm(s, "124");
        s.GoStep(Session.LastStep);
        s.AddAnother();
        Make.FillForm(s, "125");
        s.PickRole1(RoleChoice.Other);
        s.TypeRole1("Messenger");
        s.GoStep(Session.LastStep);

        var issued = s.MarkIssued();
        Assert.Equal(2, issued.Count);
        Assert.Empty(s.Queue);
        Assert.Equal(new[] { "APG-2026-124", "APG-2026-125" }, register.All().Select(e => e.Id).OrderBy(x => x));
        Assert.Contains("Messenger", new TitleList(store).All());
        Assert.Equal("Reyes", issued[0].Last);
        Assert.False(issued[0].Replacement);

        // the next card sees them at once
        s.NewId();
        s.SetSeq("125");
        Assert.Contains("already issued", s.Problems()[Field.Seq]);
    }

    [Fact]
    public void The_example_card_is_never_written_to_the_register()
    {
        var store = new MemoryStore();
        var register = Make.LocalRegister(store);
        var s = Make.Session(register, store);
        s.LoadExample(Make.Photo(), 2026);
        Assert.Equal(Session.LastStep, s.StepIndex);
        Assert.Equal("Christopher Joseph V. Aureo", s.PrintedName);
        Assert.True(s.Ready());
        Assert.Empty(s.MarkIssued());
        Assert.Empty(register.All());
    }

    [Fact]
    public void One_copy_each_unless_more_are_asked_for_and_never_more_than_twenty_sheets()
    {
        var s = Make.Session();
        Make.FillForm(s);
        s.GoStep(Session.LastStep);
        var plan = s.CurrentPlan()!;
        Assert.Equal(SheetMode.Pairs, plan.Mode);
        Assert.Equal(1, s.PrintCopies(plan));
        Assert.Equal("1 page of A4", s.PrintFileLine());

        s.SetCopies(5);
        Assert.Equal("2 pages of A4", s.PrintFileLine());     // four pairs to a sheet
        var print = s.PrintPages()!.Value;
        Assert.Equal(5, print.Entries.Count);
        Assert.Equal(2, print.Pages.Count);

        s.SetCopies(100000);
        Assert.Equal(80, s.PrintCopies(plan));               // twenty sheets of four
        s.NudgeCopies(1);
        Assert.Equal(80, s.Copies);

        s.SetLayout(SheetMode.Duplex);
        Assert.Null(s.Copies);                                // the two layouts hold different numbers
        Assert.Equal("2 pages of A4", s.PrintFileLine());     // one sheet, both sides
    }

    [Fact]
    public void Printer_settings_are_remembered_and_a_short_edge_turn_draws_the_backs_the_other_way_up()
    {
        var store = new MemoryStore();
        var s = Make.Session(store: store);
        s.SetLayout(SheetMode.Duplex);
        s.SetFlip(DuplexFlip.ShortEdge);
        s.SetRotateBack(true);

        var again = Make.Session(store: store);
        Assert.Equal(SheetMode.Duplex, again.Layout);
        Assert.Equal(DuplexFlip.ShortEdge, again.Flip);
        Assert.True(again.RotateBack);
        Assert.False(again.BackUpsideDown(again.CurrentPlan()!));     // rotated, then turned back by the short-edge flip
        again.SetFlip(DuplexFlip.LongEdge);
        Assert.True(again.BackUpsideDown(again.CurrentPlan()!));
    }

    [Fact]
    public void The_design_can_only_be_changed_before_an_id_is_started()
    {
        var s = Make.Session();
        Assert.True(s.DesignAllowed);
        s.SetNamePart(Field.Last, "R");
        Assert.False(s.DesignAllowed);
        s.ClearCard();
        Assert.True(s.DesignAllowed);
    }
}

public class RegisterTests
{
    [Fact]
    public void Entries_are_kept_newest_first_and_replaced_by_id()
    {
        var store = new MemoryStore();
        var r = Make.LocalRegister(store);
        r.Record(new RegisterEntry { Id = "APG-2026-001", Year = 2026, Seq = 1, Name = "A", IssuedAt = "2026-01-01T00:00:00.000Z" });
        r.Record(new RegisterEntry { Id = "APG-2026-002", Year = 2026, Seq = 2, Name = "B", IssuedAt = "2026-02-01T00:00:00.000Z" });
        r.Record(new RegisterEntry { Id = "APG-2026-001", Year = 2026, Seq = 1, Name = "A again", IssuedAt = "2026-03-01T00:00:00.000Z", Replacement = true });
        var all = Make.LocalRegister(store).All();
        Assert.Equal(new[] { "A again", "B" }, all.Select(e => e.Name));
        Assert.Equal("A again", r.Find(2026, 1)!.Name);
        Assert.Null(r.Find(2026, null));
        Assert.Null(r.Find(2025, 1));
    }

    [Fact]
    public void The_register_exports_as_csv_and_reads_its_own_export_back()
    {
        var r = Make.LocalRegister();
        r.Record(new RegisterEntry { Id = "APG-2026-124", Year = 2026, Seq = 124, Name = "Ana \"Annie\" Reyes", Title = "HR, Admin", Last = "Reyes", First = "Ana", IssuedAt = "2026-11-17T01:30:00.000Z" });
        var csv = r.ToCsv();
        Assert.StartsWith("\uFEFF\"id\",\"year\",\"seq\"", csv);
        Assert.Contains("\"Ana \"\"Annie\"\" Reyes\"", csv);

        var other = Make.LocalRegister();
        Assert.Equal(1, other.ImportCsv(csv));
        Assert.Equal(0, other.ImportCsv(csv));                 // already there: nothing added twice
        var e = other.Find(2026, 124)!;
        Assert.Equal("Ana \"Annie\" Reyes", e.Name);
        Assert.Equal("HR, Admin", e.Title);
        Assert.Equal("Reyes", e.Last);
    }

    /* ---- the shared register ---------------------------------------
       Every test below talks to FakeFirebase, a project that exists only
       in memory. None calls the real one.
       ---------------------------------------------------------------- */

    private static readonly RegisterEntry Ana = new()
    {
        Id = "APG-2026-124", Year = 2026, Seq = 124, Name = "Ana Reyes", Title = "HR Associate", Last = "Reyes", First = "Ana", IssuedAt = "2026-11-17T01:30:00.000Z",
    };

    private const string ListUrl = "https://firestore.googleapis.com/v1/projects/demo-idmaker/databases/(default)/documents/keystone/apg/idreg";

    [Fact]
    public async Task Signing_in_is_asked_once_and_remembered_wrapped_never_in_plain_text()
    {
        var store = new MemoryStore();
        var keeper = new FakeKeeper();
        var firebase = new FakeFirebase();
        var r = await Make.SignedIn(firebase, store, keeper);
        Assert.True(r.SignedIn);
        Assert.Equal(FakeFirebase.Email, r.SignedInAs);

        // what is on disk is the wrapped renewal token: not the password, and not the token as Firebase gave it
        var kept = store.Get("register-signin");
        Assert.NotNull(kept);
        Assert.DoesNotContain(FakeFirebase.Password, kept);
        Assert.DoesNotContain("renewal-", kept);
        Assert.DoesNotContain("pass-", kept);

        // the app is closed and opened again: nobody is asked, and the register reads
        var later = Make.SharedRegister(firebase, store, keeper);
        Assert.True(later.SignedIn);
        Assert.True(await later.LoadIndex());
        Assert.Equal(1, firebase.SignIns);
        Assert.Equal(1, firebase.Renewals);
    }

    [Fact]
    public async Task Without_windows_protection_the_sign_in_lasts_for_this_run_only()
    {
        var firebase = new FakeFirebase();
        foreach (var keeper in new ISecretKeeper?[] { null, new FakeKeeper { Broken = true } })
        {
            var store = new MemoryStore();
            var r = await Make.SignedIn(firebase, store, keeper);
            Assert.True(r.SignedIn);
            Assert.Null(store.Get("register-signin"));                  // nothing written rather than something readable
            Assert.False(Make.SharedRegister(firebase, store, keeper).SignedIn);
        }
    }

    [Fact]
    public async Task A_kept_sign_in_this_account_cannot_open_is_asked_for_again()
    {
        var store = new MemoryStore();
        var firebase = new FakeFirebase();
        await Make.SignedIn(firebase, store, new FakeKeeper());
        // another Windows account, or the file copied to another computer
        Assert.False(Make.SharedRegister(firebase, store, new FakeKeeper { Broken = true }).SignedIn);
        store.Set("register-signin", "{\"kept\":\"not ours\"}");
        Assert.False(Make.SharedRegister(firebase, store, new FakeKeeper()).SignedIn);
        store.Set("register-signin", "{not json");
        Assert.False(Make.SharedRegister(firebase, store, new FakeKeeper()).SignedIn);
    }

    [Fact]
    public async Task Each_way_signing_in_can_fail_says_what_to_do_next()
    {
        var firebase = new FakeFirebase();
        firebase.Accounts["off@apg.test"] = new FakeFirebase.Account("pw", "uid-off", Disabled: true);
        firebase.Accounts["visitor@apg.test"] = new FakeFirebase.Account("pw", "uid-visitor", Member: false);
        var store = new MemoryStore();
        var r = Make.SharedRegister(firebase, store, new FakeKeeper());

        async Task<string> Error(string email, string password)
        {
            var result = await r.SignIn(email, password);
            Assert.False(result.Ok);
            Assert.False(r.SignedIn);
            Assert.Null(store.Get("register-signin"));
            return result.Error;
        }

        Assert.Contains("Enter the email", await Error(" ", "x"));
        Assert.Contains("Enter the password", await Error(FakeFirebase.Email, ""));
        Assert.Contains("email or password is not right", await Error(FakeFirebase.Email, "wrong"));
        Assert.Contains("email or password is not right", await Error("nobody@apg.test", "x"));
        Assert.Contains("turned off", await Error("off@apg.test", "pw"));
        // a real sign-in that the CEO never made a member opens nothing, and is not kept
        Assert.Contains("may not use the ID register", await Error("visitor@apg.test", "pw"));
        Assert.Contains("Keystone", await Error("visitor@apg.test", "pw"));

        firebase.Offline = true;
        Assert.Contains("could not reach", await Error(FakeFirebase.Email, FakeFirebase.Password));
        firebase.Offline = false;

        var wrongKey = new Register(new MemoryStore(), firebase.Http) { Project = Make.Project with { ApiKey = "someone-elses" } };
        Assert.Contains("API key", (await wrongKey.SignIn(FakeFirebase.Email, FakeFirebase.Password)).Error);
        Assert.Contains("no shared register", (await Make.LocalRegister().SignIn(FakeFirebase.Email, FakeFirebase.Password)).Error);

        // and the right one still works after all that
        Assert.True((await r.SignIn($"  {FakeFirebase.Email} ", FakeFirebase.Password)).Ok);
    }

    [Fact]
    public async Task The_register_is_read_whole_page_by_page_and_an_odd_document_cannot_break_the_read()
    {
        var firebase = new FakeFirebase();
        for (var i = 1; i <= 650; i++) firebase.Put(new RegisterEntry { Id = $"APG-2026-{i:000}", Year = 2026, Seq = i, Name = $"Person {i}" });
        // documents nobody could make through the app, were the rules ever loosened
        firebase.Docs["APG-2026-900"] = new System.Text.Json.Nodes.JsonObject { ["name"] = new System.Text.Json.Nodes.JsonObject { ["integerValue"] = "42" }, ["year"] = new System.Text.Json.Nodes.JsonObject { ["nullValue"] = null } };
        firebase.Docs["APG-2026-901"] = new System.Text.Json.Nodes.JsonObject();
        var r = await Make.SignedIn(firebase);
        var before = firebase.Lists;

        Assert.True(await r.LoadIndex());
        Assert.Equal(3, firebase.Lists - before);                       // 652 documents, 300 a page
        var (known, entry) = r.FindSharedNow(2026, 1);
        Assert.True(known);
        Assert.Equal("Person 1", entry!.Name);
        Assert.Equal(2026, entry.Year);
        Assert.Equal("Person 650", r.FindSharedNow(2026, 650).Entry!.Name);
        Assert.Equal("42", r.FindSharedNow(2026, 900).Entry!.Name);
        Assert.Equal("", r.FindSharedNow(2026, 901).Entry!.Name);       // still taken: the document is there
        Assert.Null(r.FindSharedNow(2026, 651).Entry);

        var ping = await r.Ping();
        Assert.True(ping.Ok);
        Assert.Equal(652, ping.Rows);
    }

    [Fact]
    public async Task Ten_callers_asking_at_once_make_one_request()
    {
        var firebase = new FakeFirebase();
        var r = await Make.SignedIn(firebase);
        var gate = new TaskCompletionSource();
        firebase.ListGate = gate.Task;
        var before = firebase.Lists;
        var loads = Enumerable.Range(0, 10).Select(_ => r.LoadIndex()).ToArray();
        gate.SetResult();
        Assert.All(await Task.WhenAll(loads), Assert.True);
        Assert.Equal(1, firebase.Lists - before);
    }

    [Fact]
    public async Task A_card_is_created_under_its_own_number_as_whoever_is_signed_in()
    {
        var firebase = new FakeFirebase();
        var r = await Make.SignedIn(firebase);
        Assert.True(await r.LoadIndex());

        Assert.Equal(SharedOutcome.Sent, (await r.RecordShared(Ana)).Outcome);
        Assert.Contains($"POST {ListUrl}?documentId=APG-2026-124", firebase.Requests);
        var doc = firebase.Docs["APG-2026-124"];
        Assert.Equal("Ana Reyes", doc["name"]!["stringValue"]!.ToString());
        Assert.Equal("2026", doc["year"]!["integerValue"]!.ToString());
        Assert.Equal("124", doc["seq"]!["integerValue"]!.ToString());
        Assert.Equal("2026-11-17T01:30:00.000Z", doc["issuedAt"]!["stringValue"]!.ToString());
        Assert.Equal(FakeFirebase.Uid, doc["by"]!["stringValue"]!.ToString());
        Assert.Equal(FakeFirebase.Email, doc["byEmail"]!["stringValue"]!.ToString());
        Assert.Equal(0, r.WaitingForShared);

        // a card is only ever created: the app has no request that changes or removes one
        Assert.DoesNotContain(firebase.Requests, q => q.StartsWith("PATCH ") || q.StartsWith("PUT ") || q.StartsWith("DELETE "));
        // and no photograph, or anything like one, goes with it
        Assert.Equal(new[] { "by", "byEmail", "first", "id", "issuedAt", "last", "middle", "name", "replacement", "role1", "role2", "seq", "suffix", "title", "year" },
            doc.Select(f => f.Key).Order(StringComparer.Ordinal));

        // who the card is for goes with it once the form has asked; a card from before then says nothing rather than guess
        var intern = Ana with { Id = "APG-2026-126", Seq = 126, Name = "Ben Cruz", Kind = PersonKind.Intern };
        Assert.Equal(SharedOutcome.Sent, (await r.RecordShared(intern)).Outcome);
        Assert.Equal("intern", firebase.Docs["APG-2026-126"]["kind"]!["stringValue"]!.ToString());
        Assert.False(doc.ContainsKey("kind"));
        Assert.True(await r.LoadIndex(force: true));
        Assert.Equal(PersonKind.Intern, r.FindSharedNow(2026, 126).Entry!.Kind);
        Assert.Equal("", r.FindSharedNow(2026, 124).Entry!.Kind);

        r.NoteShared(Ana);
        Assert.Equal("Ana Reyes", r.FindSharedNow(2026, 124).Entry!.Name);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_number_another_computer_took_first_is_refused_and_is_not_sent_again(HttpStatusCode alreadyThere)
    {
        var firebase = new FakeFirebase { AlreadyThere = alreadyThere };
        var frontDesk = await Make.SignedIn(firebase);
        var storeB = new MemoryStore();
        var backOffice = await Make.SignedIn(firebase, storeB);
        // both read the register while the number was free
        Assert.True(await frontDesk.LoadIndex());
        Assert.True(await backOffice.LoadIndex());
        Assert.Null(backOffice.FindSharedNow(2026, 124).Entry);

        var ben = new RegisterEntry { Id = "APG-2026-124", Year = 2026, Seq = 124, Name = "Ben Cruz", Title = "Driver", IssuedAt = "2026-11-17T01:30:05.000Z" };
        Assert.Equal(SharedOutcome.Sent, (await frontDesk.RecordShared(Ana)).Outcome);
        backOffice.NoteShared(ben);                                     // as the form does when the PDF is saved
        var second = await backOffice.RecordShared(ben);

        Assert.Equal(SharedOutcome.Refused, second.Outcome);
        Assert.Equal("Ana Reyes", second.Holder!.Name);
        Assert.Equal("Ana Reyes", firebase.Get("APG-2026-124")!.Name);  // the first card is untouched
        Assert.Single(firebase.Docs);
        // from now on this computer knows whose number it is
        Assert.Equal("Ana Reyes", backOffice.FindSharedNow(2026, 124).Entry!.Name);

        // it is not left in the outbox to be refused for ever…
        Assert.Equal(0, backOffice.WaitingForShared);
        var creates = firebase.Creates;
        Assert.Equal(0, await backOffice.SendWaiting());
        Assert.Equal(creates, firebase.Creates);

        // …but it is not forgotten either, even after the app is closed, until somebody has dealt with the card
        var later = Make.SharedRegister(firebase, storeB);
        var refused = Assert.Single(later.Refused);
        Assert.Equal("Ben Cruz", refused.Card.Name);
        Assert.Equal("Ana Reyes", refused.Holder.Name);
        later.ClearRefused();
        Assert.Empty(Make.SharedRegister(firebase, storeB).Refused);
    }

    [Fact]
    public async Task The_form_says_a_refused_card_is_a_duplicate_and_must_not_be_handed_out()
    {
        var firebase = new FakeFirebase();
        var store = new MemoryStore();
        var register = await Make.SignedIn(firebase, store);
        var s = Make.Session(register, store);
        Assert.True(await register.LoadIndex());
        Make.FillForm(s, "124");
        s.GoStep(Session.LastStep);
        // another computer issues the number after this one checked it
        firebase.Put(new RegisterEntry { Id = "APG-2026-124", Year = 2026, Seq = 124, Name = "Juan dela Cruz", Title = "Driver", IssuedAt = "2026-11-17T01:29:58.000Z" });
        var issued = s.MarkIssued();
        Assert.Single(issued);
        var report = await s.RecordShared(issued);

        Assert.Empty(report.Waiting);
        var refused = Assert.Single(report.Refused);
        Assert.Equal("Juan dela Cruz", refused.Holder.Name);
        Assert.Contains("APG-2026-124 was issued to Juan dela Cruz on another computer", report.Message());
        Assert.Contains("the card just made for Ana Reyes is a duplicate", report.Message());
        Assert.Contains("Do not hand it out", report.Message());

        // the next card on that number is stopped at the form
        s.NewId();
        s.SetSeq("124");
        Assert.Contains("already issued", s.Problems()[Field.Seq]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_card_whose_answer_was_lost_is_sent_again_and_is_still_one_card(HttpStatusCode alreadyThere)
    {
        var firebase = new FakeFirebase { AlreadyThere = alreadyThere };
        var r = await Make.SignedIn(firebase);
        firebase.LoseNextAnswer = true;
        Assert.Equal(SharedOutcome.Waiting, (await r.RecordShared(Ana)).Outcome);    // it got there, but this computer cannot know
        Assert.Equal(1, r.WaitingForShared);

        Assert.Equal(0, await r.SendWaiting());                         // "already there", and it is this same card: sent
        Assert.Single(firebase.Docs);
        Assert.Empty(r.Refused);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_replacement_is_recorded_under_the_card_it_replaces_which_stays_as_it_was(HttpStatusCode alreadyThere)
    {
        var firebase = new FakeFirebase { AlreadyThere = alreadyThere };
        firebase.Put(Ana);
        var r = await Make.SignedIn(firebase);
        var again = Ana with { IssuedAt = "2026-12-01T02:00:00.000Z", Replacement = true };

        Assert.Equal(SharedOutcome.Sent, (await r.RecordShared(again)).Outcome);
        Assert.Equal("2026-11-17T01:30:00.000Z", firebase.Get("APG-2026-124")!.IssuedAt);
        Assert.False(firebase.Get("APG-2026-124")!.Replacement);
        var under = firebase.Docs["APG-2026-124/replacements/20261201T020000000Z"];
        Assert.Equal("true", under["replacement"]!["booleanValue"]!.ToString());
        Assert.Equal(FakeFirebase.Uid, under["by"]!["stringValue"]!.ToString());
        Assert.Empty(r.Refused);

        // sent twice, it is still one replacement
        Assert.Equal(SharedOutcome.Sent, (await r.RecordShared(again)).Outcome);
        Assert.Equal(2, firebase.Docs.Count);

        // a replacement for a card from before the register existed takes the number itself
        var old = new RegisterEntry { Id = "APG-2024-007", Year = 2024, Seq = 7, Name = "Old Hand", IssuedAt = "2026-12-01T03:00:00.000Z", Replacement = true };
        Assert.Equal(SharedOutcome.Sent, (await r.RecordShared(old)).Outcome);
        Assert.True(firebase.Get("APG-2024-007")!.Replacement);
    }

    [Fact]
    public async Task A_card_saved_while_offline_is_kept_and_sent_when_the_register_is_back()
    {
        var firebase = new FakeFirebase();
        var store = new MemoryStore();
        var keeper = new FakeKeeper();
        var r = await Make.SignedIn(firebase, store, keeper);
        var a = new RegisterEntry { Id = "APG-2026-010", Year = 2026, Seq = 10, Name = "A", IssuedAt = "2026-11-17T01:00:00.000Z" };
        var b = new RegisterEntry { Id = "APG-2026-011", Year = 2026, Seq = 11, Name = "B", IssuedAt = "2026-11-17T01:01:00.000Z" };

        firebase.Offline = true;
        Assert.Equal(SharedOutcome.Waiting, (await r.RecordShared(a)).Outcome);
        Assert.Equal(SharedOutcome.Waiting, (await r.RecordShared(b)).Outcome);
        Assert.Equal(SharedOutcome.Waiting, (await r.RecordShared(a)).Outcome);      // trying again does not queue it twice
        Assert.Equal(2, r.WaitingForShared);
        Assert.Equal(2, await r.SendWaiting());                         // still down
        Assert.Contains("could not reach", r.LastProblem);
        Assert.Empty(firebase.Docs);

        // the app is closed and opened again later, with the connection back
        firebase.Offline = false;
        var later = Make.SharedRegister(firebase, store, keeper);
        Assert.Equal(2, later.WaitingForShared);
        Assert.Equal(0, await later.SendWaiting());
        Assert.Equal(new[] { "APG-2026-010", "APG-2026-011" }, firebase.Docs.Keys);
        Assert.Null(store.Get("register-outbox"));
    }

    [Fact]
    public async Task A_card_saved_before_anyone_signed_in_waits_for_the_sign_in()
    {
        var firebase = new FakeFirebase();
        var r = Make.SharedRegister(firebase);
        Assert.Equal(SharedOutcome.Waiting, (await r.RecordShared(Ana)).Outcome);
        Assert.Equal(1, r.WaitingForShared);
        Assert.Empty(firebase.Requests);                                // nothing is sent as nobody

        Assert.True((await r.SignIn(FakeFirebase.Email, FakeFirebase.Password)).Ok);
        Assert.Equal(0, await r.SendWaiting());
        Assert.Equal("Ana Reyes", firebase.Get("APG-2026-124")!.Name);
    }

    [Fact]
    public async Task A_card_the_register_will_not_take_from_this_account_waits_and_says_why()
    {
        var firebase = new FakeFirebase();
        var r = await Make.SignedIn(firebase);
        // removed from Keystone's members after signing in: the write is refused, and it is not because of the number
        firebase.Accounts[FakeFirebase.Email] = firebase.Accounts[FakeFirebase.Email] with { Member = false };
        Assert.Equal(SharedOutcome.Waiting, (await r.RecordShared(Ana)).Outcome);
        Assert.Contains("may not use the ID register", r.LastProblem);
        Assert.Equal(1, r.WaitingForShared);
        Assert.Empty(r.Refused);
        Assert.Empty(firebase.Docs);

        firebase.Accounts[FakeFirebase.Email] = firebase.Accounts[FakeFirebase.Email] with { Member = true };
        Assert.Equal(0, await r.SendWaiting());
    }

    [Fact]
    public async Task A_pass_that_has_run_out_is_renewed_without_asking_anyone()
    {
        var firebase = new FakeFirebase();
        var now = new DateTimeOffset(2026, 11, 17, 1, 0, 0, TimeSpan.Zero);
        var r = await Make.SignedIn(firebase, clock: () => now);
        Assert.True(await r.LoadIndex());
        Assert.Equal(0, firebase.Renewals);

        // two hours on, by this computer's clock
        now = now.AddHours(2);
        Assert.Equal(SharedOutcome.Sent, (await r.RecordShared(Ana)).Outcome);
        Assert.Equal(1, firebase.Renewals);

        // the register says the pass is no good before the clock does: renewed once, and the request sent again
        firebase.ExpirePasses();
        var next = Ana with { Id = "APG-2026-125", Seq = 125, Name = "Ben Cruz" };
        Assert.Equal(SharedOutcome.Sent, (await r.RecordShared(next)).Outcome);
        Assert.Equal(2, firebase.Renewals);
        Assert.Equal(1, firebase.SignIns);
        Assert.Equal(2, firebase.Docs.Count);
    }

    [Fact]
    public async Task A_sign_in_that_is_no_longer_accepted_asks_again_and_loses_no_card()
    {
        var firebase = new FakeFirebase();
        var store = new MemoryStore();
        var r = await Make.SignedIn(firebase, store, new FakeKeeper());
        Assert.True(await r.LoadIndex());

        // the password was changed, or the CEO turned the account off
        firebase.CancelSignIns();
        Assert.Equal(SharedOutcome.Waiting, (await r.RecordShared(Ana)).Outcome);
        Assert.False(r.SignedIn);
        Assert.Contains("Sign in again", r.SignInProblem);
        Assert.Null(store.Get("register-signin"));
        Assert.False(r.IndexLoaded);                                    // what was read as that account is not trusted on
        Assert.False(await r.LoadIndex());
        Assert.Contains("Sign in again", r.LastProblem);
        Assert.Contains("Sign in again", (await r.Ping()).Error);
        Assert.Equal(1, r.WaitingForShared);

        Assert.True((await r.SignIn(FakeFirebase.Email, FakeFirebase.Password)).Ok);
        Assert.Equal("", r.SignInProblem);
        Assert.Equal(0, await r.SendWaiting());
        Assert.Equal("Ana Reyes", firebase.Get("APG-2026-124")!.Name);
    }

    [Fact]
    public async Task Signing_out_while_a_read_is_in_flight_does_not_keep_what_it_read()
    {
        var firebase = new FakeFirebase();
        firebase.Put(Ana);
        var r = await Make.SignedIn(firebase);
        var gate = new TaskCompletionSource();
        firebase.ListGate = gate.Task;
        var read = r.LoadIndex();
        r.SignOut();
        gate.SetResult();

        Assert.False(await read);                                       // its answer is not kept
        Assert.False(r.IndexLoaded);
        Assert.False(r.FindSharedNow(2026, 124).Known);
        Assert.False(r.SignedIn);
        Assert.False(await r.LoadIndex());
        Assert.Equal(Register.SignInFirst, r.LastProblem);

        firebase.ListGate = null;
        Assert.True((await r.SignIn(FakeFirebase.Email, FakeFirebase.Password)).Ok);
        Assert.True(await r.LoadIndex());
        Assert.Equal("Ana Reyes", r.FindSharedNow(2026, 124).Entry!.Name);
    }

    [Fact]
    public async Task Asking_the_register_how_it_is_says_how_many_cards_or_what_to_do()
    {
        var firebase = new FakeFirebase();
        firebase.Put(Ana);
        Assert.Contains("no shared register", (await Make.LocalRegister().Ping()).Error);
        Assert.False(Make.LocalRegister().Shared);

        var r = Make.SharedRegister(firebase);
        Assert.True(r.Shared);
        Assert.Equal(Register.SignInFirst, (await r.Ping()).Error);

        Assert.True((await r.SignIn(FakeFirebase.Email, FakeFirebase.Password)).Ok);
        var ping = await r.Ping();
        Assert.True(ping.Ok);
        Assert.Equal(1, ping.Rows);

        firebase.Failing = HttpStatusCode.InternalServerError;
        Assert.Contains("HTTP 500", (await r.Ping()).Error);
        firebase.Failing = HttpStatusCode.Forbidden;                    // removed from Keystone's members since signing in
        Assert.Contains("may not use the ID register", (await r.Ping()).Error);
        firebase.Failing = HttpStatusCode.TooManyRequests;
        Assert.Contains("busy", (await r.Ping()).Error);
        firebase.Failing = null;
        firebase.Offline = true;
        Assert.Contains("could not reach", (await r.Ping()).Error);
    }
}

public class BatchTests
{
    private static Batch.Services Services(Register? register = null) => new(
        register ?? Make.LocalRegister(), new FakeMeasurer(), CardGeometry.BuiltIn,
        path => path.EndsWith("broken.png", StringComparison.Ordinal)
            ? (null, "This file couldn’t be opened as an image.")
            : path.Contains("sign", StringComparison.Ordinal)
                ? (SignScan(), "")
                : (Make.Silhouette(600, 1000, 60, 200, 260, 90, 40, 520), ""),
        null, () => new DateTime(2026, 11, 17));

    private static RgbaImage SignScan()
    {
        var img = new RgbaImage(400, 200);
        Array.Fill(img.Data, (byte)240);
        for (var y = 80; y < 100; y++)
            for (var x = 100; x < 300; x++)
                for (var c = 0; c < 3; c++) img.Data[(y * 400 + x) * 4 + c] = 20;
        return img;
    }

    [Fact]
    public void A_csv_without_the_columns_it_needs_says_which()
    {
        Assert.Equal("This CSV is empty.", Batch.ReadRows("").Error);
        Assert.Contains("needs a “seq” column", Batch.ReadRows("last,first,title\nA,B,C").Error);
        Assert.Contains("“last” and “first”", Batch.ReadRows("title,seq\nA,1").Error);
        Assert.Contains("no people", Batch.ReadRows("name,title,seq\n").Error);
        var many = "name,title,seq\n" + string.Concat(Enumerable.Range(1, 201).Select(i => $"Person {i},Driver,{i}\n"));
        Assert.Contains("201 rows", Batch.ReadRows(many).Error);
    }

    [Fact]
    public void Quoted_fields_commas_and_line_endings_are_read_properly()
    {
        var rows = Csv.Parse("\uFEFFa,\"b,1\",\"say \"\"hi\"\"\"\r\n\r\n\"two\nlines\",x,y\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "a", "b,1", "say \"hi\"" }, rows[0]);
        Assert.Equal("two\nlines", rows[1][0]);
    }

    [Fact]
    public void Files_are_matched_by_name_whatever_the_case_folder_or_extension()
    {
        var find = Batch.FileIndex(new[] { @"C:\Photos\Aureo.JPG", "/home/x/reyes.png" });
        Assert.Equal(@"C:\Photos\Aureo.JPG", find("aureo.png"));
        Assert.Equal("/home/x/reyes.png", find("photos/REYES.PNG"));
        Assert.Null(find("cruz.png"));
        Assert.Null(find(""));
    }

    [Fact]
    public void Good_rows_become_cards_and_bad_rows_are_reported_not_half_issued()
    {
        var register = Make.LocalRegister();
        register.Record(new RegisterEntry { Id = "APG-2026-130", Year = 2026, Seq = 130, Name = "Earlier Person" });
        const string csv =
            "last,first,middle,title,title2,seq,year,photo,signature,replacement\n" +
            "Aureo,Christopher Joseph,Villanueva,IT Associate,,122,2026,aureo.png,aureo-sign.jpg,\n" +     // good, signed
            "Reyes,Ana,,HR Associate,Admin Associate,123,,reyes.png,,\n" +                                  // good, no signature
            "Reyes,Ana,,HR Associate,,123,,reyes.png,,\n" +                                                 // same number twice
            ",Juan,,Driver,,124,,cruz.png,,\n" +                                                            // no last name
            "Cruz,Juan,,Driver,,abc,,cruz.png,,\n" +                                                        // not a number
            "Cruz,Juan,,Driver,,130,,cruz.png,,\n" +                                                        // already issued
            "Cruz,Juan,,Driver,,130,,cruz.png,,yes\n" +                                                     // …unless it is a replacement
            "Lim,Bea,,Driver,,131,,missing.png,,\n" +                                                       // no such photo
            "Tan,Cy,,Driver,,132,,broken.png,,\n" +                                                         // photo will not open
            "Uy,Di,,,,133,,uy.png,,\n";                                                                     // no title
        var files = new[] { "aureo.png", "reyes.png", "cruz.png", "broken.png", "uy.png" };
        var outcome = Batch.Run(csv, files, new[] { "aureo-sign.jpg" }, Services(register));

        Assert.Equal("", outcome.Error);
        Assert.Equal(10, outcome.Results.Count);
        Assert.Equal(new[] { "APG-2026-122", "APG-2026-123", "APG-2026-130" }, outcome.Good.Select(r => r.Id));

        var first = outcome.Results[0];
        Assert.Equal("Christopher Joseph V. Aureo", first.Data!.Name);
        Assert.False(first.NoSignature);
        Assert.IsType<ScannedSignature>(first.Data.Signature);
        Assert.Equal("IT/Admin Associate".Replace("IT/Admin", "HR/Admin"), outcome.Results[1].Data!.Title);
        Assert.True(outcome.Results[1].NoSignature);
        Assert.Equal(2026, outcome.Results[1].Entry!.Year);

        Assert.Contains("appears twice", outcome.Results[2].Problem);
        Assert.Equal("No last name.", outcome.Results[3].Problem);
        Assert.Contains("not an employee number", outcome.Results[4].Problem);
        Assert.Contains("already issued to Earlier Person", outcome.Results[5].Problem);
        Assert.True(outcome.Results[6].Entry!.Replacement);
        Assert.Contains("No photo file named “missing.png”", outcome.Results[7].Problem);
        Assert.Contains("broken.png: This file couldn’t be opened", outcome.Results[8].Problem);
        Assert.Equal("No job title.", outcome.Results[9].Problem);
        Assert.Equal(5, outcome.Results[4].Line + 0 - 1);
    }

    [Fact]
    public void A_list_says_who_is_an_intern_and_a_list_from_before_the_column_means_employees()
    {
        const string csv =
            "last,first,title,seq,photo,kind\n" +
            "Aureo,Chris,IT Associate,122,a.png,intern\n" +
            "Reyes,Ana,HR Associate,123,a.png, Employee \n" +
            "Cruz,Juan,Driver,124,a.png,\n" +                 // left empty: an employee
            "Lim,Bea,Driver,125,a.png,volunteer\n";           // neither
        var outcome = Batch.Run(csv, new[] { "a.png" }, null, Services());
        Assert.Equal(new[] { PersonKind.Intern, PersonKind.Employee, PersonKind.Employee }, outcome.Good.Select(r => r.Entry!.Kind));
        Assert.Contains("neither “intern” nor “employee”", outcome.Results[3].Problem);

        // the example list HR is given to fill in is itself a list this reads, interns and all
        var example = Batch.ReadRows(Batch.CsvTemplate);
        Assert.Equal("", example.Error);
        Assert.Equal(new[] { "employee", "employee", "intern" }, example.Rows!.Select(r => r["kind"]));

        var old = Batch.Run("last,first,title,seq,photo\nUy,Di,Driver,126,a.png\n", new[] { "a.png" }, null, Services());
        Assert.Equal(PersonKind.Employee, Assert.Single(old.Good).Entry!.Kind);
    }

    [Fact]
    public void An_older_list_with_whole_names_still_works_and_nothing_usable_is_an_error()
    {
        var outcome = Batch.Run("name,title,seq\nJuan dela Cruz Jr.,Driver,7\n", new[] { "Juan dela Cruz Jr..png" }, null, Services());
        Assert.Single(outcome.Good);
        Assert.Equal("Cruz", outcome.Good[0].Entry!.Last);
        Assert.Equal("Juan dela Cruz Jr.", outcome.Good[0].Data!.Name);

        var none = Batch.Run("name,title,seq\nJuan,Driver,7\n", Array.Empty<string>(), null, Services());
        Assert.Contains("None of the rows", none.Error);
        Assert.Equal(("apg-id-sheets-2026-11-17.pdf", "Alpha Premier IDs — 1 card — 2026-11-17"), Batch.FileFor(1, true, new DateTime(2026, 11, 17)));
        Assert.Equal(("apg-id-batch-2026-11-17.pdf", "Alpha Premier IDs — 2 cards — 2026-11-17"), Batch.FileFor(2, false, new DateTime(2026, 11, 17)));
    }
}
