// What the window shows, decided in Core (Screen.From) and checked here
// without a window. Each test is one rule the two windows used to work out
// separately.

using System.Net;
using IdMaker.Core;

namespace IdMaker.Tests;

public class ScreenTests
{
    private static readonly ScreenInput Windows = new(HostPlatform.Windows) { Loaded = true };
    private static readonly ScreenInput Mac = new(HostPlatform.Mac) { Loaded = true };

    /// <summary>The value, which the test needs to be there.</summary>
    private static T Must<T>(T? value) where T : class
    {
        Assert.NotNull(value);
        return value!;
    }

    private static void GoToCheck(Session s)
    {
        Make.FillForm(s);
        s.GoStep(Session.LastStep);
        Assert.Equal(Session.LastStep, s.StepIndex);
    }

    [Fact]
    public void An_error_shows_only_once_its_field_was_touched()
    {
        var s = Make.Session();
        var blank = Screen.From(s, Windows);
        Assert.Equal("", blank.Name.LastError);
        Assert.Equal("", blank.Title.KindError);
        Assert.Equal("", blank.Number.Error);
        Assert.Equal("", blank.Sign.Error);

        s.Touch(Field.Last);
        s.Touch(Field.Kind);
        s.Touch(Field.Seq);
        s.Touch(Field.Signature);
        var touched = Screen.From(s, Windows);
        Assert.Equal("Required.", touched.Name.LastError);
        Assert.Equal("", touched.Name.FirstError);
        Assert.Equal("Required.", touched.Title.KindError);
        Assert.Equal("Required.", touched.Number.Error);
        Assert.True(touched.Number.LineIsError);
        Assert.Equal("Required.", touched.Sign.Error);
        Assert.Equal("Required.", touched.Sign.Message);
    }

    [Fact]
    public void A_name_too_long_for_the_card_is_said_once_both_halves_are_typed()
    {
        var s = Make.Session();
        s.Measurer = new FakeMeasurer { CharEm = 3 };
        s.SetNamePart(Field.Last, "Montenegro-Villanueva");
        var half = Screen.From(s, Windows).Name;
        Assert.False(half.PreviewIsError);
        Assert.Equal("", half.Preview);

        s.SetNamePart(Field.First, "Christopher Joseph");
        var whole = Screen.From(s, Windows).Name;
        Assert.True(whole.PreviewIsError);
        Assert.StartsWith("This name is too long for the card", whole.Preview);

        Assert.Equal("", Screen.From(Make.Session(), Windows).Name.Preview);
    }

    [Fact]
    public void A_job_title_too_long_for_the_card_is_said_once_a_title_was_chosen()
    {
        var s = Make.Session();
        s.Measurer = new FakeMeasurer { CharEm = 3 };
        Assert.Equal("", Screen.From(s, Windows).Title.FitError);
        s.PickRole1(RoleChoice.Other);
        s.TypeRole1("Senior Regional Marketing Coordinator");
        var title = Screen.From(s, Windows).Title;
        Assert.StartsWith("This job title is too long", title.FitError);
        Assert.True(title.CustomShown);
        Assert.False(title.ForgetShown);
    }

    [Fact]
    public void The_second_role_never_offers_the_first()
    {
        Assert.True(Session.SecondRoleHides("IT Associate", "it associate"));
        Assert.False(Session.SecondRoleHides("HR Associate", "IT Associate"));
        Assert.False(Session.SecondRoleHides("IT Associate", ""));
        Assert.False(Session.SecondRoleHides(RoleChoice.Other, RoleChoice.Other));

        var s = Make.Session();
        var before = Screen.From(s, Windows).Title;
        Assert.False(before.SecondShown);
        Assert.True(before.AddSecondShown);
        s.AddSecondRole();
        s.PickRole2(RoleChoice.Other);
        var after = Screen.From(s, Windows).Title;
        Assert.True(after.SecondShown);
        Assert.False(after.AddSecondShown);
        Assert.True(after.SecondCustomShown);
    }

    [Fact]
    public void The_line_under_the_number_says_which_computers_were_checked()
    {
        var local = Make.Session();
        local.SetSeq("124");
        var number = Screen.From(local, Windows).Number;
        Assert.Equal("Checked against this computer only.", number.Help);
        Assert.Equal(number.Help, number.Line);
        Assert.False(number.LineIsError);
        Assert.False(number.ReissueShown);
    }

    [Fact]
    public async Task The_line_under_the_number_says_when_nobody_is_signed_in_or_the_register_is_out_of_reach()
    {
        var store = new MemoryStore();
        var nobody = Make.Session(Make.SharedRegister(new FakeFirebase(), store), store);
        nobody.SetSeq("124");
        await nobody.DuplicateCheck;
        Assert.Equal("Checked against this computer only. Sign in under Issued IDs to check every computer.", Screen.From(nobody, Windows).Number.Help);

        var firebase = new FakeFirebase();
        var register = await Make.SignedIn(firebase);
        var s = Make.Session(register);
        s.SetSeq("124");
        await s.DuplicateCheck;
        Assert.Equal("", Screen.From(s, Mac).Number.Help);

        var down = Make.Session(await Make.SignedIn(firebase));
        firebase.Failing = HttpStatusCode.InternalServerError;
        down.SetSeq("125");
        await down.DuplicateCheck;
        Assert.Equal("Could not reach the shared register, so only this computer was checked.", Screen.From(down, Mac).Number.Help);
    }

    [Fact]
    public void A_number_already_issued_offers_a_replacement_and_then_says_whose_it_replaces()
    {
        var store = new MemoryStore();
        var register = Make.LocalRegister(store);
        register.Record(new RegisterEntry { Id = "APG-2026-124", Year = 2026, Seq = 124, Name = "Ana Reyes", IssuedAt = "2026-11-17T01:30:00.000Z" });
        var s = Make.Session(register, store);
        s.SetSeq("124");
        var taken = Screen.From(s, Windows).Number;
        Assert.True(taken.ReissueShown);
        Assert.True(taken.LineIsError);
        Assert.Contains("was already issued to Ana Reyes", taken.Line);

        s.SetReissue(true);
        var replacing = Screen.From(s, Windows).Number;
        Assert.False(replacing.LineIsError);
        Assert.StartsWith("Replacement for APG-2026-124 (Ana Reyes, ", replacing.Line);
    }

    [Fact]
    public void The_line_under_the_buttons_names_what_this_step_still_needs()
    {
        var s = Make.Session();
        var first = Screen.From(s, Windows).Footer;
        Assert.False(first.StatusShown);
        Assert.Equal("", first.StatusText);
        Assert.False(first.BlockedShown);
        Assert.False(first.BackShown);
        Assert.True(first.NextShown);
        Assert.Equal("Next", first.NextTitle);
        Assert.True(first.NextEnabled);

        // nothing is said, and Next waits, until the card itself has loaded
        var loading = Screen.From(s, Windows with { Loaded = false });
        Assert.False(loading.Footer.StatusShown);
        Assert.False(loading.Footer.NextEnabled);
        Assert.True(loading.Stage.LoadingShown);
        Assert.Equal("Loading the card…", loading.Stage.LoadingText);
        Assert.False(loading.Stage.CardShown);
        Assert.Equal("The fonts are missing.", Screen.From(s, Windows with { Loaded = false, LoadError = "The fonts are missing." }).Stage.LoadingText);

        // a refusal takes the line over, and goes when what it named is put right
        s.NextStep();
        var refused = Screen.From(s, Windows).Footer;
        Assert.True(refused.BlockedShown);
        Assert.False(refused.StatusShown);
        Assert.Equal("Please complete the highlighted fields.", refused.BlockedText);
        s.SetNamePart(Field.Last, "Reyes");
        s.SetNamePart(Field.First, "Ana");
        var fixedNow = Screen.From(s, Windows).Footer;
        Assert.False(fixedNow.BlockedShown);
        Assert.Null(s.BlockedAbout);
        Assert.False(fixedNow.StatusShown);
    }

    [Fact]
    public void Back_and_Next_follow_the_step_and_the_last_step_before_the_check_says_so()
    {
        var s = Make.Session();
        Make.FillForm(s);
        s.GoStep((int)Step.Sign);
        var sign = Screen.From(s, Windows).Footer;
        Assert.True(sign.BackShown);
        Assert.Equal("Next", sign.NextTitle);

        s.GoStep(Session.LastStep);
        var check = Screen.From(s, Windows);
        Assert.False(check.Footer.NextShown);
        Assert.True(check.Footer.BackShown);
        Assert.False(check.Footer.StatusShown);
        Assert.False(Screen.From(s, Windows with { Saved = true }).Footer.BackShown);
        Assert.Equal(Session.LastStep, check.StepIndex);
    }

    [Fact]
    public void After_stop_adding_the_empty_form_is_not_listed_as_missing()
    {
        var s = Make.Session();
        GoToCheck(s);
        Assert.True(s.AddAnother());
        s.SetNamePart(Field.Last, "Cruz");
        s.CancelAdding();
        Assert.Equal(Session.LastStep, s.StepIndex);
        var screen = Screen.From(s, Windows);
        Assert.False(screen.Footer.StatusShown);
        var check = Must(screen.Check);
        Assert.Equal("Ready to print", check.Heading);
        Assert.True(check.SubShown);
        Assert.Equal("Printing the 1 card already in this print.", check.Sub);
        Assert.False(check.SummaryShown);
        Assert.Equal("", check.SummaryNumber);
        Assert.True(check.SaveEnabled);
        Assert.False(check.PicturesEnabled);
        Assert.False(check.AddAnotherEnabled);
    }

    [Fact]
    public void Each_step_chip_is_locked_current_finished_or_to_come()
    {
        var s = Make.Session();
        s.SetNamePart(Field.Last, "Reyes");
        s.SetNamePart(Field.First, "Ana");
        s.NextStep();
        Assert.Equal(1, s.StepIndex);
        var chips = Screen.From(s, Windows).Chips;
        Assert.Equal(Session.Steps.Length, chips.Count);

        Assert.True(chips[0].Done);
        Assert.Equal("Name. Finished.", chips[0].Says);
        Assert.Equal("", chips[0].ToolTip);

        Assert.True(chips[1].Current);
        Assert.False(chips[1].Done);
        Assert.Equal("Job & number. Current step.", chips[1].Says);
        Assert.Equal("Job & no.", chips[1].Short);           // what fits an equal fifth of the bar

        Assert.True(chips[2].Locked);
        Assert.Equal("Photo. Locked until step 2, Job & number, is finished.", chips[2].Says);
        Assert.Equal("Finish job & number first", chips[2].ToolTip);

        // a step cannot be both finished and out of reach, and the last step is never "finished"
        Assert.All(chips, c => Assert.False(c.Done && c.Locked));
        Make.FillForm(s);
        s.GoStep(0);
        var all = Screen.From(s, Windows).Chips;
        Assert.False(all[Session.LastStep].Done);
        Assert.False(all[Session.LastStep].Locked);
        Assert.Equal("Check", all[Session.LastStep].Says);
        Assert.True(all[3].Done);
    }

    [Fact]
    public void The_photo_step_says_one_thing_at_a_time_most_urgent_first()
    {
        var s = Make.Session();
        var none = Screen.From(s, Windows).Photo;
        Assert.Equal(Tone.Note, none.Tone);
        Assert.Equal("Any clear photo of the person.", none.Message);
        Assert.Equal("Choose a photo", none.ButtonTitle);
        Assert.False(none.HasPhoto);
        Assert.Equal("If the cut-out looks wrong", none.GuideLabel);
        Assert.Equal("Any clear photo of the person. The app removes the background itself.", Screen.From(s, Windows with { RemoverReady = true }).Photo.Message);
        Assert.StartsWith("A cut-out PNG, or a photo on a plain green background.", Screen.From(s, Windows with { RemoverProblem = "no model" }).Photo.Message);

        var looking = Screen.From(s, Windows with { CheckingPhoto = true }).Photo;
        Assert.Equal("Looking at the photo…", looking.Message);
        Assert.Equal("Looking at the photo…", looking.ButtonTitle);
        Assert.False(looking.ButtonEnabled);

        // a photo that was turned away says so at once, without the field having been left
        s.AcceptPhoto(new PhotoResult(new RgbaImage(4, 4), "bad.png", PhotoBackground.Other, new PhotoCheck { Ok = false, Problem = "Too dark.", FixIsPrompt = true }));
        var refused = Screen.From(s, Windows).Photo;
        Assert.Equal(Tone.Error, refused.Tone);
        Assert.Equal("Too dark.", refused.Message);
        Assert.True(refused.WantsPrompt);
        Assert.Equal("Get a photo the ID will accept", refused.GuideLabel);

        s.AcceptPhoto(new PhotoResult(new RgbaImage(4, 4), "ana.png", PhotoBackground.Removed,
            new PhotoCheck { Ok = true, Placement = new PhotoPlacement(10, 10, 30, 50, 0), Warnings = new[] { "A little soft.", "Small." } }));
        var warned = Screen.From(s, Windows).Photo;
        Assert.Equal(Tone.Warning, warned.Tone);
        Assert.Equal("A little soft. Small.", warned.Message);
        Assert.Equal("Choose a different photo", warned.ButtonTitle);
        Assert.Equal("ana.png", warned.FileName);
        Assert.True(warned.HasPhoto);
        Assert.False(warned.ResetEnabled);
        Assert.Equal(100, warned.ZoomPercent);
        Assert.Equal("100 %", warned.ZoomText);

        s.AcceptPhoto(new PhotoResult(new RgbaImage(4, 4), "ana.png", PhotoBackground.Removed, new PhotoCheck { Ok = true, Placement = new PhotoPlacement(10, 10, 30, 50, 0) }));
        Assert.Equal("The background was removed on this computer. Check the edges on the card.", Screen.From(s, Windows).Photo.Message);
        s.AcceptPhoto(Make.Photo());
        Assert.Equal("", Screen.From(s, Windows).Photo.Message);
        s.SetZoom(1.2);
        var zoomed = Screen.From(s, Windows).Photo;
        Assert.True(zoomed.ResetEnabled);
        Assert.Equal(120, zoomed.ZoomPercent);
    }

    [Fact]
    public void The_photo_can_be_dragged_only_on_the_front_of_a_card_still_being_made()
    {
        var s = Make.Session();
        Assert.False(Screen.CanDragPhoto(s, front: true, editingMap: false));
        Make.FillForm(s);
        s.GoStep((int)Step.Photo);
        Assert.True(Screen.From(s, Windows).Photo.CanDrag);
        Assert.False(Screen.From(s, Windows with { Front = false }).Photo.CanDrag);
        Assert.False(Screen.From(s, Windows with { EditingMap = true }).Photo.CanDrag);
        s.GoStep(Session.LastStep);
        Assert.False(Screen.From(s, Windows).Photo.CanDrag);
    }

    [Fact]
    public void The_signature_step_names_its_button_its_help_and_the_tablet_as_each_window_did()
    {
        var s = Make.Session();
        var none = Screen.From(s, Windows with { TabletDriver = true }).Sign;
        Assert.Equal("Sign now", none.ButtonTitle);
        Assert.False(none.HasSignature);
        Assert.False(none.MessageShown);
        Assert.Equal("Signing opens on the whole screen, so the pen has room. It prints in white on the card.", none.Help);
        Assert.Equal("A pen tablet draws thicker where you press harder.", none.TabletNote);
        Assert.Contains("The installer can add the XP-Pen driver.", Screen.From(s, Windows).Sign.TabletNote);
        Assert.Contains("The app can install the XP-Pen driver.", Screen.From(s, Mac).Sign.TabletNote);

        // a scan that could not be read is said, and whatever signature was there stays
        var unread = Screen.From(s, Windows with { SignatureError = "That scan is blank." }).Sign;
        Assert.True(unread.MessageShown);
        Assert.Equal("That scan is blank.", unread.Message);
        Assert.Equal("", unread.Error);

        s.SetSignature(Session.ExampleSignature());
        var signed = Screen.From(s, Windows).Sign;
        Assert.Equal("Sign again", signed.ButtonTitle);
        Assert.True(signed.HasSignature);

        s.SetSignature(new ScannedSignature(2, 2, new byte[4], "ana-sign.jpg"));
        Assert.Equal("Scanned: ana-sign.jpg. It prints in white on the card.", Screen.From(s, Windows).Sign.Help);
    }

    [Fact]
    public void The_stage_says_which_side_it_shows_and_how_the_sheet_is_cut()
    {
        var s = Make.Session();
        var front = Screen.From(s, Windows).Stage;
        Assert.True(front.SideTabsShown);
        Assert.True(front.CardShown);
        Assert.False(front.SheetShown);
        Assert.Equal("The front updates as you type.", front.Caption);
        Assert.Equal("Front of the ID card for a new hire", front.CardSays);
        var back = Screen.From(s, Windows with { Front = false }).Stage;
        Assert.Equal("The back is the same on every card.", back.Caption);
        Assert.Equal("Back of the ID card", back.CardSays);

        GoToCheck(s);
        var pairs = Screen.From(s, Windows).Stage;
        Assert.False(pairs.SideTabsShown);
        Assert.False(pairs.CardShown);
        Assert.True(pairs.SheetShown);
        Assert.Equal("Cut along the marks and stick each pair back to back.", pairs.Caption);
        Assert.Equal("Front of the ID card for Ana Reyes", pairs.CardSays);
        s.SetLayout(SheetMode.Duplex);
        Assert.Equal("Print both sides, then cut along the marks.", Screen.From(s, Windows).Stage.Caption);
    }

    [Fact]
    public void The_sheet_preview_names_its_page_and_never_leaves_the_pages_there_are()
    {
        var s = Make.Session();
        Assert.Null(s.SheetPreview());
        GoToCheck(s);
        var one = Must(s.SheetPreview());
        Assert.Equal("Page 1 of 1 · A4", one.Caption);
        Assert.Equal("Preview of page 1 of the A4 sheet.", one.Says);
        Assert.False(one.NavShown);
        Assert.Single(one.Entries);

        s.SetLayout(SheetMode.Duplex);
        var fronts = Must(s.SheetPreview());
        Assert.Equal("Fronts. Page 1 of 2 · A4", fronts.Caption);
        Assert.Equal("Preview of page 1 of the A4 sheet. Fronts.", fronts.Says);
        Assert.True(fronts.NavShown);
        Assert.False(fronts.PreviousEnabled);
        Assert.True(fronts.NextEnabled);

        s.TurnPreviewPage(1);
        s.TurnPreviewPage(1);                   // past the end: brought back to the last page
        var backs = Must(s.SheetPreview());
        Assert.Equal(1, s.PreviewPage);
        Assert.Equal("Backs, reversed to match. Page 2 of 2 · A4", backs.Caption);
        Assert.True(backs.Page.Backs);
        Assert.True(backs.PreviousEnabled);
        Assert.False(backs.NextEnabled);
        Assert.Equal(s.BackUpsideDown(backs.Plan), backs.BackUpsideDown);

        s.TurnPreviewPage(-5);
        Must(s.SheetPreview());
        Assert.Equal(0, s.PreviewPage);
    }

    [Fact]
    public void The_check_step_sums_up_the_card_and_counts_the_paper()
    {
        var s = Make.Session();
        GoToCheck(s);
        var check = Must(Screen.From(s, Windows).Check);
        Assert.Equal("Is everything correct?", check.Heading);
        Assert.False(check.SubShown);
        Assert.True(check.SummaryShown);
        Assert.Equal("Ana Reyes", check.SummaryName);
        Assert.Equal("HR ASSOCIATE", check.SummaryTitle);
        Assert.Equal("APG-2026-124", check.SummaryNumber);
        Assert.False(check.WarningShown);
        Assert.True(check.HasPlan);
        Assert.Equal("1", check.CopiesText);
        Assert.False(check.CopiesDownEnabled);
        Assert.True(check.CopiesUpEnabled);
        Assert.Equal("Copies of this card", check.CopiesLabel);
        var plan = Must(s.CurrentPlan());
        Assert.Equal($"{plan.PerSheet} cards per A4 sheet.", check.PrintNote);
        Assert.Equal(s.PrintFileLine(), check.FileLine);
        Assert.True(check.SaveEnabled);
        Assert.Equal("Save the PDF", check.SaveTitle);
        Assert.True(check.PicturesEnabled);
        Assert.True(check.AddAnotherEnabled);

        // more than one sheet is worth saying; and the + button stops where the clamp does
        s.SetCopies(plan.PerSheet + 1);
        var more = Must(Screen.From(s, Windows).Check);
        Assert.Equal($"{plan.PerSheet} cards per A4 sheet · {plan.PerSheet + 1} cards, 2 sheets.", more.PrintNote);
        Assert.True(more.CopiesDownEnabled);
        s.SetCopies(100000);
        var most = Must(Screen.From(s, Windows).Check);
        Assert.Equal(Session.MostCopies(plan).ToString(), most.CopiesText);
        Assert.False(most.CopiesUpEnabled);
        s.NudgeCopies(1);
        Assert.Equal(Session.MostCopies(plan), s.PrintCopies(plan));

        var saving = Must(Screen.From(s, Windows with { Saving = true }).Check);
        Assert.False(saving.SaveEnabled);
        Assert.Equal("Saving the PDF…", saving.SaveTitle);
        Assert.False(saving.PicturesEnabled);
        Assert.False(saving.AddAnotherEnabled);

        Assert.Equal("ID ready to print", Must(Screen.From(s, Windows with { Saved = true, SavedCount = 1 }).Check).Heading);
        Assert.Equal("3 IDs ready to print", Must(Screen.From(s, Windows with { Saved = true, SavedCount = 3 }).Check).Heading);
        Assert.True(Must(Screen.From(s, Windows with { Saved = true }).Check).Saved);
    }

    [Fact]
    public void The_check_step_is_made_only_where_it_is_shown()
    {
        var s = Make.Session();
        Make.FillForm(s);
        for (var step = 0; step < Session.LastStep; step++)
        {
            s.GoStep(step);
            Assert.Equal(step, s.StepIndex);
            Assert.Null(Screen.From(s, Windows).Check);
            Assert.Null(Screen.From(s, Mac).Check);
        }
        s.GoStep(Session.LastStep);
        Assert.NotNull(Screen.From(s, Windows).Check);
        Assert.NotNull(Screen.From(s, Mac).Check);
    }

    [Fact]
    public void A_redraw_counts_the_cards_in_the_print_once()
    {
        // Off the last step a finished card has no review entry yet, so every count of
        // the print's cards makes one, and making one reads the clock: the clock counts the counts.
        var store = new MemoryStore();
        var reads = 0;
        var s = new Session(Make.LocalRegister(store), new TitleList(store), store, () => { reads++; return new DateTime(2026, 11, 17, 9, 30, 0); })
        {
            Measurer = new FakeMeasurer(),
        };
        Make.FillForm(s);
        s.GoStep((int)Step.Sign);
        Assert.True(s.Ready());

        reads = 0;
        var screen = Screen.From(s, Windows);

        Assert.Equal(1, reads);
        Assert.Equal("1 card", screen.Job.Count);
    }

    [Fact]
    public void The_strip_lists_what_is_in_this_print_once_there_is_something_to_list()
    {
        var s = Make.Session();
        Assert.False(Screen.From(s, Windows).Job.Shown);
        GoToCheck(s);
        var alone = Screen.From(s, Windows).Job;
        Assert.True(alone.Shown);
        Assert.Equal("1 card", alone.Count);
        Assert.False(alone.NoteShown);
        Assert.False(alone.CancelAddShown);
        var only = Assert.Single(alone.Rows);
        Assert.True(only.Current);
        Assert.Equal("1.  Ana Reyes · APG-2026-124", only.Text);
        Assert.Equal("Ana Reyes · APG-2026-124", only.ToolTip);

        Assert.True(s.AddAnother());
        var adding = Screen.From(s, Windows).Job;
        Assert.True(adding.Shown);
        Assert.True(adding.CancelAddShown);
        Assert.Equal("Finish this card and they all go on one sheet.", adding.Note);
        var queued = Assert.Single(adding.Rows);
        Assert.False(queued.Current);
        Assert.Equal("Take Ana Reyes out of this print", queued.RemoveSays);
        Assert.NotEqual(alone.Key, adding.Key);

        Make.FillForm(s, "125");
        s.GoStep(Session.LastStep);
        var two = Screen.From(s, Windows).Job;
        Assert.Equal("2 cards", two.Count);
        Assert.Equal("All of these print together, and are issued when you save.", two.Note);
        Assert.False(two.CancelAddShown);
        Assert.Equal("Copies of each card", Must(Screen.From(s, Windows).Check).CopiesLabel);
        Assert.False(Screen.From(s, Windows with { Saved = true }).Job.Shown);
    }

    [Fact]
    public void The_tool_drawer_is_headed_by_its_tool_and_the_design_is_offered_only_before_a_card_is_started()
    {
        var s = Make.Session();
        var closed = Screen.From(s, Windows).Tools;
        Assert.Null(closed.Open);
        Assert.True(closed.DesignOffered);
        Assert.Equal("", closed.DrawerTitle);
        Assert.Equal(0, closed.Issued);
        Assert.Equal("Issued IDs", closed.RegisterButton);
        Assert.Null(Screen.From(s, Windows).Register);

        Assert.Equal("Card design", Screen.From(s, Windows with { Tool = ToolKey.Design }).Tools.DrawerTitle);
        Assert.Equal("Printer setting", Screen.From(s, Mac with { Tool = ToolKey.Printer }).Tools.DrawerTitle);
        // the one name the two windows spell differently
        Assert.Equal("Batch from CSV", Screen.From(s, Windows with { Tool = ToolKey.Batch }).Tools.DrawerTitle);
        Assert.Equal("Batch from a list", Screen.From(s, Mac with { Tool = ToolKey.Batch }).Tools.DrawerTitle);

        // a card has been started: the design drawer shuts and its button goes
        s.SetNamePart(Field.Last, "Reyes");
        var started = Screen.From(s, Windows with { Tool = ToolKey.Design }).Tools;
        Assert.False(started.DesignOffered);
        Assert.Null(started.Open);
        Assert.Equal(ToolKey.Printer, Screen.From(s, Windows with { Tool = ToolKey.Printer }).Tools.Open);
    }

    [Fact]
    public void The_issued_ids_drawer_lists_the_newest_and_counts_the_rest()
    {
        var store = new MemoryStore();
        var register = Make.LocalRegister(store);
        var s = Make.Session(register, store);
        var empty = Must(Screen.From(s, Windows with { Tool = ToolKey.Register }).Register);
        Assert.False(empty.ExportEnabled);
        Assert.Equal("No cards issued yet.", empty.Note);
        Assert.Empty(empty.Rows);
        Assert.Equal("", empty.More);
        Assert.Equal("This copy of the app keeps its own list only.", empty.Account);
        Assert.False(empty.SignInShown);
        Assert.False(empty.SignedInShown);
        Assert.False(empty.RefusedShown);

        for (var i = 1; i <= 30; i++)
        {
            register.Record(new RegisterEntry
            {
                Id = Naming.EmployeeId(2026, i), Year = 2026, Seq = i, Name = $"Person {i}", Title = "IT Associate",
                IssuedAt = $"2026-11-17T01:{i:00}:00.000Z", Replacement = i == 30,
            });
        }
        var windows = Screen.From(s, Windows with { Tool = ToolKey.Register });
        Assert.Equal("Issued IDs (30)", windows.Tools.DrawerTitle);
        Assert.Equal("Issued IDs (30)", windows.Tools.RegisterButton);
        Assert.Equal(30, windows.Tools.Issued);
        var listed = Must(windows.Register);
        Assert.True(listed.ExportEnabled);
        Assert.Equal("Latest cards issued on this computer.", listed.Note);
        Assert.Equal(25, listed.Rows.Count);
        Assert.Equal("… and 5 more in the saved list.", listed.More);
        Assert.Equal("APG-2026-030 (replacement)", listed.Rows[0].Head);
        Assert.Equal("Person 30 · IT Associate", listed.Rows[0].Detail);
        Assert.NotEqual("", listed.Rows[0].When);
        Assert.Equal("APG-2026-029", listed.Rows[1].Head);

        // the Mac's drawer is the shorter one
        var mac = Must(Screen.From(s, Mac with { Tool = ToolKey.Register }).Register);
        Assert.Equal(12, mac.Rows.Count);
        Assert.Equal("… and 18 more in the saved list.", mac.More);
        Assert.NotEqual(listed.Key, mac.Key);
    }

    [Fact]
    public async Task The_issued_ids_drawer_says_who_is_signed_in_what_is_waiting_and_what_was_refused()
    {
        var firebase = new FakeFirebase();
        var store = new MemoryStore();
        var register = Make.SharedRegister(firebase, store);
        var s = Make.Session(register, store);
        var signedOut = Must(Screen.From(s, Windows with { Tool = ToolKey.Register }).Register);
        Assert.True(signedOut.SignInShown);
        Assert.False(signedOut.SignedInShown);
        Assert.Equal("Sign in with your Keystone account, once on this computer, so numbers are checked against every computer.", signedOut.Account);

        var ana = new RegisterEntry { Id = "APG-2026-124", Year = 2026, Seq = 124, Name = "Ana Reyes", IssuedAt = "2026-11-17T01:30:00.000Z" };
        Assert.Equal(SharedOutcome.Waiting, (await register.RecordShared(ana)).Outcome);
        Assert.EndsWith(" 1 card is waiting to be written to it.", Screen.From(s, Windows with { Tool = ToolKey.Register }).Register?.Account);

        // somebody else holds the number by the time this computer signs in and sends it
        firebase.Put(ana with { Name = "Ben Cruz", IssuedAt = "2026-11-16T01:30:00.000Z" });
        Assert.True((await register.SignIn(FakeFirebase.Email, FakeFirebase.Password)).Ok);
        Assert.Equal(0, await register.SendWaiting());
        var signedIn = Must(Screen.From(s, Mac with { Tool = ToolKey.Register }).Register);
        Assert.False(signedIn.SignInShown);
        Assert.True(signedIn.SignedInShown);
        Assert.Equal($"Signed in as {FakeFirebase.Email}.", signedIn.Account);
        Assert.True(signedIn.RefusedShown);
        Assert.StartsWith("APG-2026-124 belongs to Ben Cruz (issued ", signedIn.RefusedNote);
        Assert.EndsWith("The card made here for Ana Reyes is a duplicate: take it back, and make it again with a number that is free.", signedIn.RefusedNote);
    }

    [Fact]
    public async Task The_issued_ids_drawer_counts_the_waiting_cards_in_words()
    {
        var firebase = new FakeFirebase();
        var store = new MemoryStore();
        var register = Make.SharedRegister(firebase, store);
        var s = Make.Session(register, store);
        string Account() => Must(Screen.From(s, Windows with { Tool = ToolKey.Register }).Register).Account;
        RegisterEntry Card(int seq) => new() { Id = $"APG-2026-{seq}", Year = 2026, Seq = seq, Name = "Ana Reyes", IssuedAt = "2026-11-17T01:30:00.000Z" };

        // nobody signed in
        await register.RecordShared(Card(124));
        Assert.EndsWith("every computer. 1 card is waiting to be written to it.", Account());
        await register.RecordShared(Card(125));
        Assert.EndsWith("every computer. 2 cards are waiting to be written to it.", Account());

        // signed in on another computer, and the register out of reach
        var elsewhere = new FakeFirebase();
        var signedIn = await Make.SignedIn(elsewhere);
        var t = Make.Session(signedIn);
        string AccountThere() => Must(Screen.From(t, Mac with { Tool = ToolKey.Register }).Register).Account;
        elsewhere.Offline = true;
        await signedIn.RecordShared(Card(126));
        Assert.Equal($"Signed in as {FakeFirebase.Email}. 1 card is still waiting to be written to it.", AccountThere());
        await signedIn.RecordShared(Card(127));
        Assert.Equal($"Signed in as {FakeFirebase.Email}. 2 cards are still waiting to be written to it.", AccountThere());
    }

    [Fact]
    public void A_refused_move_sends_the_cursor_to_what_is_wrong()
    {
        var s = Make.Session();
        var refused = s.NextStep();
        var after = Screen.AfterMove(s, 0, refused, saved: false, openSigningOnArrival: true);
        Assert.False(after.StepChanged);
        Assert.Equal(FocusTarget.Last, after.Focus);
        Assert.True(after.ShowFront);
        Assert.False(after.OpenSigning);

        Assert.Equal(FocusTarget.First, Screen.FocusFor(s, Field.NameFit));
        Assert.Equal(FocusTarget.TitleChoice, Screen.FocusFor(s, Field.Title));
        Assert.Equal(FocusTarget.Title2Choice, Screen.FocusFor(s, Field.Title2));
        s.PickRole1(RoleChoice.Other);
        s.AddSecondRole();
        s.PickRole2(RoleChoice.Other);
        Assert.Equal(FocusTarget.TitleCustom, Screen.FocusFor(s, Field.TitleFit));
        Assert.Equal(FocusTarget.Title2Custom, Screen.FocusFor(s, Field.Title2));
        Assert.Equal(FocusTarget.Kind, Screen.FocusFor(s, Field.Kind));
        Assert.Equal(FocusTarget.Seq, Screen.FocusFor(s, Field.Seq));
        Assert.Equal(FocusTarget.PhotoButton, Screen.FocusFor(s, Field.Photo));
        Assert.Equal(FocusTarget.SignButton, Screen.FocusFor(s, Field.Signature));
    }

    [Fact]
    public void A_step_that_opens_puts_the_cursor_on_what_the_step_is_about()
    {
        var s = Make.Session();
        s.SetNamePart(Field.Last, "Reyes");
        s.SetNamePart(Field.First, "Ana");
        var moved = s.NextStep();
        var title = Screen.AfterMove(s, 0, moved, saved: false, openSigningOnArrival: true);
        Assert.True(title.StepChanged);
        // the step's first question until it is answered, then the job title
        Assert.Equal(FocusTarget.Kind, title.Focus);
        s.PickKind(PersonKind.Intern);
        Assert.Equal(FocusTarget.TitlePicked, Screen.LandingFor(s, (int)Step.Title));
        // the number shares the job's step: once the job is settled, that is where the cursor goes
        s.PickRole1(s.TitleOptions()[0]);
        Assert.Equal(FocusTarget.Seq, Screen.LandingFor(s, (int)Step.Title));
        Assert.Equal(FocusTarget.Last, Screen.LandingFor(s, (int)Step.Name));
        Assert.Equal(FocusTarget.PhotoButton, Screen.LandingFor(s, (int)Step.Photo));
        Assert.Equal(FocusTarget.SignButton, Screen.LandingFor(s, (int)Step.Sign));
        Assert.Equal(FocusTarget.SaveButton, Screen.LandingFor(s, Session.LastStep));

        // arriving anywhere forgets the save, so no arrival lands on "Start a new ID", saved or not
        Make.FillForm(s);
        s.GoStep(Session.LastStep);
        var arrived = Screen.AfterMove(s, (int)Step.Sign, null, saved: true, openSigningOnArrival: true);
        Assert.False(arrived.Saved);
        Assert.Equal(FocusTarget.SaveButton, arrived.Focus);
        // and staying on the saved print moves nothing
        var stayed = Screen.AfterMove(s, Session.LastStep, null, saved: true, openSigningOnArrival: true);
        Assert.True(stayed.Saved);
        Assert.Null(stayed.Focus);

        // a move that goes nowhere leaves the cursor alone
        Assert.Null(Screen.AfterMove(s, s.StepIndex, null, saved: false, openSigningOnArrival: true).Focus);
    }

    [Fact]
    public void Arriving_at_the_signature_step_unsigned_opens_the_signing_screen_once()
    {
        var s = Make.Session();
        Make.FillForm(s);
        s.SetSignature(null);
        s.GoStep((int)Step.Sign);
        Assert.Equal((int)Step.Sign, s.StepIndex);
        Assert.True(Screen.AfterMove(s, (int)Step.Photo, null, saved: false, openSigningOnArrival: true).OpenSigning);
        // not when the window was told not to, not when already there, not when there is a signature
        Assert.False(Screen.AfterMove(s, (int)Step.Photo, null, saved: false, openSigningOnArrival: false).OpenSigning);
        Assert.False(Screen.AfterMove(s, (int)Step.Sign, null, saved: false, openSigningOnArrival: true).OpenSigning);
        s.SetSignature(Session.ExampleSignature());
        Assert.False(Screen.AfterMove(s, (int)Step.Photo, null, saved: false, openSigningOnArrival: true).OpenSigning);
    }

    [Fact]
    public void Leaving_the_last_step_turns_the_preview_to_the_front_and_forgets_the_save()
    {
        var s = Make.Session();
        GoToCheck(s);
        var stayed = Screen.AfterMove(s, Session.LastStep, null, saved: true, openSigningOnArrival: true);
        Assert.False(stayed.ShowFront);
        Assert.True(stayed.Saved);
        Assert.Null(stayed.Focus);

        s.Back();
        var left = Screen.AfterMove(s, Session.LastStep, null, saved: true, openSigningOnArrival: true);
        Assert.True(left.StepChanged);
        Assert.True(left.ShowFront);
        Assert.False(left.Saved);
        Assert.Equal(FocusTarget.SignButton, left.Focus);

        s.GoStep(Session.LastStep);
        Assert.Equal(FocusTarget.SaveButton, Screen.AfterMove(s, (int)Step.Sign, null, saved: true, openSigningOnArrival: true).Focus);
    }

    [Fact]
    public void The_batch_tool_says_what_was_chosen_and_what_the_layout_comes_to()
    {
        var nothing = Screen.BatchChosen(null, 0, 0);
        Assert.Equal("No list chosen yet.", nothing.List);
        Assert.Equal("No photos chosen yet.", nothing.Photos);
        Assert.Equal("Optional: rows without one get no signature.", nothing.Signatures);
        var chosen = Screen.BatchChosen(Path.Combine("lists", "november.csv"), 1, 2);
        Assert.Equal("november.csv", chosen.List);
        Assert.Equal("1 photo chosen.", chosen.Photos);
        Assert.Equal("2 signatures chosen.", chosen.Signatures);

        Assert.Equal("Two card-sized pages per person.", Screen.BatchLayoutLine(Batch.LayoutCards, "A4"));
        var plan = Must(Sheets.Plan("A4", SheetMode.Pairs));
        Assert.Equal($"{plan.PerSheet} cards per sheet ({plan.Cols} × {plan.Rows}, {(plan.Landscape ? "landscape" : "portrait")}).", Screen.BatchLayoutLine(Batch.LayoutPairs, "A4"));
        Assert.NotEqual(Screen.BatchLayoutLine(Batch.LayoutPairs, "A4"), Screen.BatchLayoutLine(Batch.LayoutDuplex, "A4"));
    }

    [Fact]
    public void A_batch_row_is_reported_as_skipped_warned_or_ready()
    {
        // Issued IDs: the latest three, the rest one press away
        Assert.Equal((2, ""), Screen.IssuedFold(2, open: false));
        Assert.Equal((3, ""), Screen.IssuedFold(3, open: true));
        Assert.Equal((3, "Show all 9"), Screen.IssuedFold(9, open: false));
        Assert.Equal((9, "Show fewer"), Screen.IssuedFold(9, open: true));

        // at a glance: made, to look at, skipped; and the rows with the problems first, in list order within each
        var rows = new[]
        {
            new BatchRowResult { Ok = true, Id = "APG-2026-125", Label = "Aureo, Chris", Line = 2 },
            new BatchRowResult { Line = 3, Label = "Cruz, Juan", Problem = "No job title." },
            new BatchRowResult { Ok = true, Id = "APG-2026-124", Label = "Reyes, Ana", NoSignature = true, Line = 4 },
            new BatchRowResult { Line = 5, Label = "Lim, Kevin", Problem = "No photo." },
            new BatchRowResult { Ok = true, Id = "APG-2026-126", Label = "Uy, Carla", Warnings = new[] { "A little soft." }, Line = 6 },
        };
        var sum = Screen.BatchSummary(rows);
        Assert.Equal((3, 2, 2), (sum.Made, sum.ToCheck, sum.Skipped));
        Assert.Equal(new[] { 3, 5, 4, 6, 2 }, sum.Rows.Select(r => r.Line));
        Assert.Equal((0, 0, 0), (Screen.BatchSummary(Array.Empty<BatchRowResult>()).Made, Screen.BatchSummary(Array.Empty<BatchRowResult>()).ToCheck, Screen.BatchSummary(Array.Empty<BatchRowResult>()).Skipped));

        var skipped = Screen.BatchRow(new BatchRowResult { Line = 3, Label = "Cruz, Juan", Problem = "No job title." });
        Assert.Equal((Tone.Error, "Line 3 · Cruz, Juan", "Skipped: No job title."), skipped);
        var warned = Screen.BatchRow(new BatchRowResult { Ok = true, Id = "APG-2026-124", Label = "Reyes, Ana", NoSignature = true, Warnings = new[] { "A little soft." } });
        Assert.Equal((Tone.Warning, "APG-2026-124 · Reyes, Ana", "No signature on this card. A little soft."), warned);
        var ready = Screen.BatchRow(new BatchRowResult { Ok = true, Id = "APG-2026-125", Label = "Aureo, Chris" });
        Assert.Equal((Tone.Note, "APG-2026-125 · Aureo, Chris", "Ready."), ready);
    }

    [Fact]
    public void Text_that_will_not_read_on_new_artwork_is_named_in_one_sentence()
    {
        var none = Screen.ContrastReport(Array.Empty<ContrastWarning>());
        Assert.Equal("", none.Head);
        Assert.Empty(none.Lines);

        var one = Screen.ContrastReport(new[] { new ContrastWarning(BoxKey.Name, "name", "#E8CD77", 1.5, 3) });
        Assert.Equal("The name would be hard to read on this artwork. Put a darker shape behind that box.", one.Head);
        var line = Assert.Single(one.Lines);
        Assert.StartsWith("name: prints in #E8CD77, contrast ", line);

        var three = Screen.ContrastReport(new[]
        {
            new ContrastWarning(BoxKey.Name, "name", "#E8CD77", 1.5, 3),
            new ContrastWarning(BoxKey.Title, "job title", "#FFFFFF", 2, 3),
            new ContrastWarning(BoxKey.EmployeeNo, "employee number", "#FFFFFF", 2.5, 3),
        });
        Assert.Equal("The name, job title and employee number would be hard to read on this artwork. Put a darker shape behind those boxes.", three.Head);
        Assert.Equal(3, three.Lines.Count);
    }

    [Fact]
    public void Counts_are_worded_the_same_everywhere()
    {
        Assert.Equal("", Words.S(1));
        Assert.Equal("s", Words.S(0));
        Assert.Equal("s", Words.S(2));
        Assert.Equal("(Access to the path is denied)", Words.Why(new UnauthorizedAccessException("Access to the path is denied.")));
    }
}
