// The window tests: the real window, opened on a real Mac, doing what an
// operator does. One test per thing an operator does, as on Windows
// (IdMaker.App.Tests). The register behind it is the pretend project
// (FakeFirebase), so nothing here can reach the real one.
//
// Compiled only with -p:WindowTest=true; run as `AlphaPremierIdMaker
// --windowtest --repo <path>`. Prints what it found and exits 0 when all
// of it holds.

using IdMaker.Core;
using IdMaker.Tests;
using ImageIO;

namespace IdMaker.Mac;

[Register("WindowTestDelegate")]
public sealed class WindowTestDelegate : NSApplicationDelegate
{
    private readonly string? _repo;

    public WindowTestDelegate(string? repo) { _repo = repo; }

    public override void DidFinishLaunching(NSNotification notification) => _ = WindowTest.Run(_repo);
}

internal static class WindowTest
{
    private static int _failed;
    private static string? _report;

    private static void Check(string what, bool holds)
    {
        Console.WriteLine($"{(holds ? "ok    " : "FAILED")}  {what}");
        if (!holds) _failed++;
    }

    public static async Task Run(string? repo)
    {
        // A window waiting on a question nobody is there to answer must not hold the run until CI gives up.
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMinutes(4));
            Console.WriteLine("FAILED  the window tests did not finish in four minutes");
            Console.Out.Flush();
            Environment.Exit(2);
        });

        _report = Environment.GetEnvironmentVariable("IDMAKER_REPORT_DIR");
        if (_report is { Length: > 0 }) Directory.CreateDirectory(_report);
        try
        {
            if (repo is null) Check("the window tests were given the repository (--repo <path>)", false);
            else
            {
                await A_card_can_be_made_from_start_to_saved_pdf(repo);
                await Signing_in_under_issued_ids_connects_this_computer_to_the_shared_register();
                await A_card_the_register_refused_is_put_in_front_of_the_operator_until_it_is_dealt_with();
                await The_background_remover_runs_on_this_mac();
                await A_batch_makes_a_card_for_every_good_row_and_says_why_it_skipped_the_rest(repo);
                The_card_design_can_be_moved_kept_discarded_replaced_and_reset(repo);
                await The_photo_can_be_dragged_on_the_card(repo);
                A_phone_photo_stored_sideways_is_stood_upright(repo);
                await The_driver_is_offered_once_and_the_one_xp_pen_serves_is_the_one_pinned();
            }
        }
        // Whatever a test throws is a failed test, reported with the rest; this is the one place that has to hear all of it.
        catch (Exception e)
        {
            Check($"no test threw ({e.GetType().Name}: {e.Message})", false);
            Console.WriteLine(e.StackTrace);
        }
        Console.WriteLine(_failed == 0 ? "The window tests passed." : $"{_failed} window check(s) failed.");
        Console.Out.Flush();
        Environment.Exit(_failed == 0 ? 0 : 1);
    }

    /* ---- helpers --------------------------------------------------- */

    private static string TempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "idmaker-window-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    // Press a control the way AppKit does when it is clicked.
    private static void Press(NSControl control)
    {
        if (control.Action is not { } action || control.Target is not { } target || !control.Enabled || control.Hidden || !control.SendAction(action, target))
        {
            Check($"“{(control as NSButton)?.Title ?? control.AccessibilityLabel}” could be pressed", false);
        }
    }

    private static void TypeName(MainWindow window, Field part, string text)
    {
        var box = part == Field.Last ? window.LastBox : window.FirstBox;
        box.StringValue = text;
        window.OnNameChanged(part);
    }

    private static async Task<bool> Until(Func<bool> holds)
    {
        for (var i = 0; i < 200; i++)
        {
            if (holds()) return true;
            await Task.Delay(25);
        }
        return holds();
    }

    private static void Shoot(MainWindow window, string name)
    {
        if (_report is not { Length: > 0 }) return;
        // SAFETY: a window made with a content rectangle always has a content view.
        Ui.Snapshot(window.Window.ContentView!, Path.Combine(_report, name + ".png"));
    }

    private static MainWindow Open(string folder, Register register)
    {
        var window = new MainWindow(new FileStore(folder), register)
        {
            OpenSigningOnArrival = false,
            Confirm = (_, _) => true,
            AskForSignature = (_, _) => Session.ExampleSignature(),
            AskForFile = _ => null,
            AskWhereToSave = (_, _) => null,
            AskForFiles = _ => Array.Empty<string>(),
            // nothing is installed on the Mac the tests run on
            OfferDriverOnStart = false,
            InstallDriver = () => Task.FromResult(""),
        };
        window.Window.MakeKeyAndOrderFront(null);
        return window;
    }

    /* ---- the tests ------------------------------------------------- */

    private static async Task A_card_can_be_made_from_start_to_saved_pdf(string repo)
    {
        Console.WriteLine("A card can be made from start to a saved PDF");
        var folder = TempFolder();
        var files = new FileStore(folder);
        var window = Open(folder, new Register(files) { Project = null });
        var s = window.Session;

        Check("the card and its fonts loaded", window.NextButton.Enabled);

        // an empty form does not move on, and says why
        Press(window.NextButton);
        Check("Next on an empty name stays on the name", s.StepIndex == (int)Step.Name);
        Check($"and says what is missing (“{window.BlockedLine}”)", window.BlockedLine.Length > 0);
        Shoot(window, "1-name-refused");

        TypeName(window, Field.Last, "Reyes");
        TypeName(window, Field.First, "Ana");
        Press(window.NextButton);
        Check("with a name, Next opens the job title", s.StepIndex == (int)Step.Title);

        // the Intern / Employee choice has to be made
        window.TitleBox.SelectItem(1);
        Press(window.TitleBox);
        Press(window.NextButton);
        Check("without saying who the ID is for, it stays", s.StepIndex == (int)Step.Title);
        window.KindBox.SelectItem(2);
        Press(window.KindBox);
        Check("choosing “An intern” is taken", s.Kind == PersonKind.Intern);
        // the number is asked for on the same page as the job
        Press(window.NextButton);
        Check("without a number it stays on the job's page, which asks for one", s.StepIndex == (int)Step.Title);

        window.SeqBox.StringValue = "7";
        window.OnSeqChanged();
        await s.DuplicateCheck;
        Press(window.NextButton);
        Check("a free number opens the photo", s.StepIndex == (int)Step.Photo);

        await window.AcceptPhotoFile(Path.Combine(repo, "assets", "example-cut-out.png"));
        Check($"the example cut-out is accepted{(s.PhotoError.Length > 0 ? $" ({s.PhotoError})" : "")}", s.Photo is not null);
        Press(window.NextButton);
        Check("then Next opens the signature", s.StepIndex == (int)Step.Sign);

        Press(window.NextButton);
        Check("unsigned, it stays on the signature", s.StepIndex == (int)Step.Sign);
        Press(window.SignButton);
        Check("signing puts the signature on the card", s.Signature is { HasInk: true });
        Shoot(window, "2-signed");
        Press(window.NextButton);
        Check("then Next opens the check", s.StepIndex == Session.LastStep);
        Shoot(window, "3-check");

        // a print that cannot be saved issues nothing
        await window.SavePrint(Path.Combine(folder, "no-such-folder", "card.pdf"));
        Check($"a save that fails says so (“{window.ReviewErrorLine}”)", window.ReviewErrorLine.Length > 0);
        Check("and issues nothing", window.Register.All().Count == 0 && !window.Saved);

        var pdf = Path.Combine(folder, "card.pdf");
        await window.SavePrint(pdf);
        var head = File.Exists(pdf) ? File.ReadAllBytes(pdf) : Array.Empty<byte>();
        Check($"the PDF is saved ({head.Length:N0} bytes)", head.Length > 10_000 && head[0] == '%' && head[1] == 'P' && head[2] == 'D' && head[3] == 'F');
        Check("no half-written file is left beside it", !File.Exists(pdf + ".part"));
        var id = Naming.EmployeeId(s.Year, 7);
        var issued = window.Register.All();
        Check($"and only then is {id} issued, to an intern", issued.Count == 1 && issued[0].Id == id && issued[0].Name.Contains("Reyes") && issued[0].Kind == PersonKind.Intern);
        Check($"the window says where it went (“{window.SavedLine}”)", window.SavedLine.Contains("card.pdf"));
        Shoot(window, "4-saved");

        // starting again: the same number is now refused
        Press(window.NewIdButton);
        Check("“Start a new ID” empties the form", s.StepIndex == (int)Step.Name && s.Last.Length == 0 && s.Photo is null);
        TypeName(window, Field.Last, "Cruz");
        TypeName(window, Field.First, "Juan");
        Press(window.NextButton);
        window.KindBox.SelectItem(1);
        Press(window.KindBox);
        window.TitleBox.SelectItem(1);
        Press(window.TitleBox);
        Press(window.NextButton);
        window.SeqBox.StringValue = "7";
        window.OnSeqChanged();
        await s.DuplicateCheck;
        Press(window.NextButton);
        Check("the number just issued is refused for somebody else", s.StepIndex == (int)Step.Title);
        Check($"and the line says who has it (“{window.SeqLine}”)", window.SeqLine.Contains(id) && window.SeqLine.Contains("Reyes"));
        Shoot(window, "5-duplicate");
        window.Window.Close();
    }

    private static async Task Signing_in_under_issued_ids_connects_this_computer_to_the_shared_register()
    {
        Console.WriteLine("Signing in under Issued IDs connects this computer to the shared register");
        var folder = TempFolder();
        var firebase = new FakeFirebase();
        var window = Open(folder, new Register(new FileStore(folder), firebase.Http, new FakeKeeper()) { Project = FakeFirebase.Project });

        window.ToggleTool("register");
        Check("Issued IDs shows the sign-in form", window.SignInFormShown);

        window.SignInEmailBox.StringValue = FakeFirebase.Email;
        window.SignInPasswordBox.StringValue = "not the password";
        await window.SignIn();
        Check($"a wrong password is refused (“{window.RegisterMessage}”)", !window.Register.SignedIn && window.RegisterMessage.Length > 0 && window.SignInFormShown);
        Check("and the password box is emptied", window.SignInPasswordBox.StringValue.Length == 0);

        window.SignInPasswordBox.StringValue = FakeFirebase.Password;
        await window.SignIn();
        Check("the right one signs in", window.Register.SignedIn && !window.SignInFormShown);
        Check($"and the window says as whom (“{window.AccountLine}”)", window.AccountLine == $"Signed in as {FakeFirebase.Email}.");
        var kept = string.Concat(Directory.GetFiles(folder).Select(File.ReadAllText));
        Check("the password is in no file this app keeps", !kept.Contains(FakeFirebase.Password));
        Shoot(window, "6-signed-in");
        window.Window.Close();
    }

    private static async Task A_card_the_register_refused_is_put_in_front_of_the_operator_until_it_is_dealt_with()
    {
        Console.WriteLine("A card the register refused is put in front of the operator until it is dealt with");
        var folder = TempFolder();
        var year = DateTime.Now.Year;
        var number = Naming.EmployeeId(year, 200);
        var firebase = new FakeFirebase();
        var keeper = new FakeKeeper();

        // yesterday: this computer, signed in but offline, issued a card that is still waiting to be written…
        var before = new Register(new FileStore(folder), firebase.Http, keeper) { Project = FakeFirebase.Project };
        Check("signed in beforehand", (await before.SignIn(FakeFirebase.Email, FakeFirebase.Password)).Ok);
        firebase.Offline = true;
        var mine = new RegisterEntry { Id = number, Year = year, Seq = 200, Name = "Ana Reyes", Title = "HR Associate", IssuedAt = "2026-11-16T08:00:00.000Z" };
        Check("a card issued offline waits", (await before.RecordShared(mine)).Outcome == SharedOutcome.Waiting);
        // …and meanwhile another computer issued the same number
        firebase.Offline = false;
        firebase.Put(new RegisterEntry { Id = number, Year = year, Seq = 200, Name = "Juan dela Cruz", Title = "Driver", IssuedAt = "2026-11-16T08:30:00.000Z" });

        // today the app opens with the register in reach: the waiting card is sent, refused, and shown without being asked for
        var window = Open(folder, new Register(new FileStore(folder), firebase.Http, keeper) { Project = FakeFirebase.Project });
        Check("the refused card is shown without being asked for", await Until(() => window.RefusedShown));
        Check($"it names who holds the number (“{window.RefusedLine}”)", window.RefusedLine.Contains($"{number} belongs to Juan dela Cruz"));
        Check("and whose card is the duplicate", window.RefusedLine.Contains("The card made here for Ana Reyes is a duplicate"));
        Check("the first card stands in the register", firebase.Get(number)?.Name == "Juan dela Cruz");
        Shoot(window, "7-refused");

        // it stays until somebody says it has been dealt with
        Press(window.RefusedDoneButton);
        Check("“These cards are dealt with” clears it", !window.RefusedShown && window.Register.Refused.Count == 0);
        window.Window.Close();
    }

    private static async Task The_background_remover_runs_on_this_mac()
    {
        Console.WriteLine("The background remover runs on this Mac");
        var folder = TempFolder();
        var window = Open(folder, new Register(new FileStore(folder)) { Project = null });
        var ready = await Until(() => window.RemoverReady || window.RemoverProblem.Length > 0);
        Check($"the model loads from inside the app{(window.RemoverProblem.Length > 0 ? $" ({window.RemoverProblem})" : "")}", ready && window.RemoverReady);
        if (window.Remover is { } remover)
        {
            // Not a photograph: a plain picture, to see the model answer at the picture's own size.
            var picture = new RgbaImage(480, 640);
            for (var i = 0; i < picture.Data.Length; i += 4)
            {
                picture.Data[i] = 90; picture.Data[i + 1] = 140; picture.Data[i + 2] = 200; picture.Data[i + 3] = 255;
            }
            var matte = await Task.Run(() => remover.Matte(picture));
            Check("and answers with one value per pixel", matte is not null && matte.Length == 480 * 640);
        }
        window.Window.Close();
    }

    private static async Task A_batch_makes_a_card_for_every_good_row_and_says_why_it_skipped_the_rest(string repo)
    {
        Console.WriteLine("A batch makes a card for every good row and says why it skipped the rest");
        var folder = TempFolder();
        var window = Open(folder, new Register(new FileStore(folder)) { Project = null });
        var year = DateTime.Now.Year;
        // the same picture under two names, as a folder of photos named for their rows would be
        var photos = new[] { Path.Combine(folder, "reyes.png"), Path.Combine(folder, "cruz.png") };
        foreach (var photo in photos) File.Copy(Path.Combine(repo, "assets", "example-cut-out.png"), photo);
        var csv = Path.Combine(folder, "list.csv");
        File.WriteAllText(csv,
            "last,first,middle,suffix,title,title2,seq,year,photo,signature,replacement,kind\r\n"
            + $"Reyes,Ana,,,HR Associate,,301,{year},reyes.png,,,employee\r\n"
            + $"Cruz,Juan,,,IT Associate,,302,{year},cruz.png,,,intern\r\n"
            + $"Santos,Maria,,,IT Associate,,303,{year},nobody.png,,,intern\r\n");

        window.ToggleTool("batch");
        window.SetBatchFiles(csv, photos, Array.Empty<string>());
        window.BatchLayoutBox.SelectItem(2);            // one card per page
        Press(window.BatchLayoutBox);

        await window.RunBatch(Path.Combine(folder, "no-such-folder", "batch.pdf"));
        Check($"a batch that cannot be saved says so (“{window.BatchStatus}”)", window.BatchStatus.Contains("couldn’t be saved"));
        Check("and issues nothing", window.Register.All().Count == 0);

        var pdf = Path.Combine(folder, "batch.pdf");
        await window.RunBatch(pdf);
        var results = window.BatchResults;
        Check($"the batch ran (“{window.BatchStatus}”)", File.Exists(pdf) && new FileInfo(pdf).Length > 10_000);
        Check("two rows made cards", results.Count(r => r.Ok) == 2);
        var skipped = results.FirstOrDefault(r => !r.Ok);
        Check($"the row with no photo is skipped, with the reason (“{skipped?.Problem}”)", skipped is { Problem.Length: > 0 } && skipped.Label.Contains("Santos"));
        var issued = window.Register.All().Select(e => e.Id).OrderBy(id => id).ToList();
        Check("exactly the two that are in the PDF are issued", issued.SequenceEqual(new[] { Naming.EmployeeId(year, 301), Naming.EmployeeId(year, 302) }));
        Check("an intern in the list is issued as an intern", window.Register.All().Any(e => e.Id == Naming.EmployeeId(year, 302) && e.Kind == PersonKind.Intern));
        Shoot(window, "8-batch");
        window.Window.Close();
    }

    private static void The_card_design_can_be_moved_kept_discarded_replaced_and_reset(string repo)
    {
        Console.WriteLine("The card design can be moved, kept, discarded, replaced and reset");
        var folder = TempFolder();
        var window = Open(folder, new Register(new FileStore(folder)) { Project = null });
        var builtIn = window.Session.Geometry;

        Check("Card design is offered on a blank card", !window.DesignRailButton.Hidden);
        window.ToggleTool("design");
        window.StartAdjusting();
        Check("“Show the map” puts the boxes over the card", window.MapShown);

        // move the name box a millimetre to the right, as four presses of the arrow key do
        window.Map.SelectNext(backwards: false);
        var before = window.Templates.Box(BoxKey.Name);
        for (var i = 0; i < 4; i++) window.Map.Nudge(0.25, 0, resize: false);
        var after = window.Templates.Box(BoxKey.Name);
        Check($"the arrow keys move the selected box ({before.X:0.00} → {after.X:0.00} mm)", Math.Abs(after.X - before.X - 1) < 0.001);
        Check("and the card is drawn to the new map straight away", window.Session.Geometry != builtIn);
        Shoot(window, "9-map");

        window.DiscardMap();
        Check($"“Discard changes” puts it back (“{window.DesignMessage}”)", !window.MapShown && Math.Abs(window.Templates.Box(BoxKey.Name).X - before.X) < 0.001 && !window.Templates.IsCustom);

        window.StartAdjusting();            // the name box is still the selected one
        window.Map.Nudge(0.25, 0, resize: false);
        window.KeepMap();
        Check($"“Keep this map” saves it on this computer (“{window.DesignMessage}”)", !window.MapShown && window.Templates.IsCustom);
        var reopened = new TemplateStore(new FileStore(folder));
        Check("and it is still there when the app opens again", reopened.Restore() && Math.Abs(reopened.Box(BoxKey.Name).X - before.X - 0.25) < 0.001);

        // artwork of the wrong shape is turned away; artwork of the right shape is stored and used
        window.UseArtwork(Path.Combine(repo, "assets", "logo.png"), Path.Combine(repo, "assets", "back.png"));
        Check($"artwork that is not card-shaped is refused (“{window.DesignMessage}”)", !window.Templates.HasArtwork && window.DesignMessage.Length > 0);
        window.UseArtwork(Path.Combine(repo, "assets", "front-template.png"), Path.Combine(repo, "assets", "back.png"));
        Check($"card-shaped artwork is taken ({window.DesignNameLine})", window.Templates.HasArtwork && File.Exists(Path.Combine(folder, "template-front.png")) && window.MapShown);
        window.KeepMap();

        window.ResetDesign();
        Check($"“Go back to the built-in design” removes all of it (“{window.DesignMessage}”)",
            !window.Templates.IsCustom && !File.Exists(Path.Combine(folder, "template-front.png")) && window.Session.Geometry == builtIn);

        // once a card is started the design is out of reach
        TypeName(window, Field.Last, "Reyes");
        Check("Card design is put away once an ID is started", window.DesignRailButton.Hidden);
        window.Window.Close();
    }

    private static async Task The_photo_can_be_dragged_on_the_card(string repo)
    {
        Console.WriteLine("The photo can be dragged on the card");
        var folder = TempFolder();
        var window = Open(folder, new Register(new FileStore(folder)) { Project = null });
        await window.AcceptPhotoFile(Path.Combine(repo, "assets", "example-cut-out.png"));
        var s = window.Session;
        Check("with a photo on the card", s.Photo is not null && s.Adjust.IsNone);
        // 40 points to the right on a card drawn 400 points wide is a tenth of its 54 mm
        window.DragPhotoTo(40, 0);
        Check($"dragging 40 points right moves it 5.4 mm ({s.Adjust.Dx:0.00} mm)", Math.Abs(s.Adjust.Dx - 5.4) < 0.01 && s.Adjust.Dy == 0);
        window.Window.Close();
    }

    private static void A_phone_photo_stored_sideways_is_stood_upright(string repo)
    {
        Console.WriteLine("A phone photo stored sideways is stood upright");
        // a JPEG 40 wide and 20 high, marked "rotate 90 degrees to view"
        var path = Path.Combine(TempFolder(), "sideways.jpg");
        var plain = new RgbaImage(40, 20);
        for (var i = 0; i < plain.Data.Length; i += 4) { plain.Data[i] = 200; plain.Data[i + 1] = 120; plain.Data[i + 2] = 40; plain.Data[i + 3] = 255; }
        using (var picture = MacPictures.ToImage(plain))
        using (var destination = CGImageDestination.Create(NSUrl.FromFilename(path), "public.jpeg", 1))
        {
            destination?.AddImage(picture, NSDictionary.FromObjectAndKey(NSNumber.FromInt32(6), ImageIO.CGImageProperties.Orientation));
            Check("the sideways picture was written", destination is not null && destination.Close());
        }
        var (image, error) = MacPictures.Decode(path);
        Check($"it opens upright, 20 wide and 40 high ({image?.Width} × {image?.Height}{error})", image is { Width: 20, Height: 40 });

        var (none, why) = MacPictures.Decode(Path.Combine(repo, "README.md"));
        Check($"a file that is not a picture is turned away in words (“{why}”)", none is null && why.Contains("couldn’t be opened"));
    }

    private static async Task The_driver_is_offered_once_and_the_one_xp_pen_serves_is_the_one_pinned()
    {
        Console.WriteLine("The pen tablet driver is offered once, and the one XP-Pen serves is the one pinned");
        var folder = TempFolder();
        var asked = 0;
        var installs = 0;
        var window = Open(folder, new Register(new FileStore(folder)) { Project = null });
        window.Confirm = (_, _) => { asked++; return true; };
        window.InstallDriver = () => { installs++; return Task.FromResult(""); };
        window.OfferDriverOnStart = true;

        if (PenDriver.Installed) Check("this Mac already has a tablet driver, so nothing is offered", true);
        else
        {
            window.OfferDriver();
            Check("on a Mac with no tablet driver the app offers XP-Pen’s", asked == 1);
            Check($"saying yes opens the installer (“{window.DriverStatus}”)", await Until(() => installs == 1 && window.DriverStatus.Contains("Apple’s installer is open")));
            window.OfferDriver();
            Check("and it does not ask a second time", asked == 1);
            Check("the signature step still has the button for later", !window.DriverButton.Hidden);
        }

        // XP-Pen's own file, fetched and unpacked as the app does it, but not opened: nothing is installed here.
        var (package, error) = await PenDriver.Fetch();
        Check($"XP-Pen still serves the pinned driver {PenDriver.Version}, and it unpacks to Apple’s installer ({(package is null ? error : Path.GetFileName(package))})",
            package is not null && package.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase) && File.Exists(package));
        window.Window.Close();
    }
}
