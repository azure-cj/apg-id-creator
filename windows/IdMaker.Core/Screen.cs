// What the window shows, worked out once.
//
// The two windows (WPF and AppKit) used to decide each of these for
// themselves, from the same Session: which error shows, what the line under
// the number says, which step chip is locked, when Back is there. Screen.From
// decides them here, and each window copies the answer onto its controls. A
// rule about the form is therefore written once and has a test that runs
// without a window (ScreenTests).
//
// Where the two windows really do differ (how many issued IDs the drawer
// lists, the batch tool's name, the tablet note) the difference is kept and
// is chosen by HostPlatform, so it is visible here rather than hidden in one of
// the windows. docs/untangle/decision-log.md lists each one.

using System.Globalization;

namespace IdMaker.Core;

/// <summary>Which of the two windows is asking.</summary>
public enum HostPlatform { Windows, Mac }

/// <summary>What kind of line a message is. Each window has its own colour for each.</summary>
public enum Tone { Note, Warning, Error }

/// <summary>A control the cursor can be sent to. Each window knows which of its controls that is.</summary>
public enum FocusTarget
{
    Last, First, Middle, Suffix, Kind,
    /// <summary>The job titles on offer, at the first of them.</summary>
    TitleChoice,
    /// <summary>The job titles on offer, at the one already picked if there is one.</summary>
    TitlePicked,
    TitleCustom, Title2Choice, Title2Custom,
    Seq, PhotoButton, SignButton, SaveButton, NewIdButton,
}

/// <summary>Small pieces of wording every message shares.</summary>
public static class Words
{
    /// <summary>"" for one, "s" for any other count: "1 card", "2 cards".</summary>
    public static string S(int count) => count == 1 ? "" : "s";

    /// <summary>The verb that goes with it: "1 card is", "2 cards are".</summary>
    public static string IsAre(int count) => count == 1 ? "is" : "are";

    /// <summary>What the system said went wrong, in brackets, ready to sit inside a sentence.</summary>
    public static string Why(Exception e) => $"({e.Message.TrimEnd('.')})";
}

/// <summary>What the window knows and the Session does not.</summary>
public sealed record ScreenInput(HostPlatform Platform)
{
    /// <summary>The preview shows the front of the card, not the back.</summary>
    public bool Front { get; init; } = true;
    /// <summary>The card's artwork and fonts are loaded.</summary>
    public bool Loaded { get; init; }
    /// <summary>Why they could not be loaded, or "".</summary>
    public string LoadError { get; init; } = "";
    /// <summary>The print on screen has been saved.</summary>
    public bool Saved { get; init; }
    public int SavedCount { get; init; }
    /// <summary>The PDF is being written.</summary>
    public bool Saving { get; init; }
    /// <summary>A photo is being looked at.</summary>
    public bool CheckingPhoto { get; init; }
    public bool RemoverReady { get; init; }
    /// <summary>Why the background remover is not there, or "".</summary>
    public string RemoverProblem { get; init; } = "";
    /// <summary>Why the last scanned signature could not be used, or "".</summary>
    public string SignatureError { get; init; } = "";
    /// <summary>A pen tablet driver is installed on this computer.</summary>
    public bool TabletDriver { get; init; }
    /// <summary>The card design's boxes are being moved.</summary>
    public bool EditingMap { get; init; }
    /// <summary>The tool drawer that is open (a ToolKey), or null.</summary>
    public string? Tool { get; init; }
}

public sealed record StageView(
    bool SideTabsShown, bool LoadingShown, string LoadingText, bool CardShown, bool SheetShown, string Caption, string CardSays);

public sealed record NameView(string LastError, string FirstError, string MiddleError, string SuffixError, string Preview, bool PreviewIsError);

public sealed record TitleView(
    string KindError, string TitleError, bool CustomShown, bool ForgetShown,
    bool SecondShown, bool AddSecondShown, bool SecondCustomShown, string SecondError, string FitError);

public sealed record NumberView(string Error, string Help, bool ReissueShown)
{
    /// <summary>The line under the number: the error while there is one, otherwise the standing note.</summary>
    public string Line => Error.Length > 0 ? Error : Help;
    public bool LineIsError => Error.Length > 0;
}

public sealed record PhotoView(
    Tone Tone, string Message, string ButtonTitle, bool ButtonEnabled, bool HasPhoto, string FileName,
    bool ResetEnabled, double ZoomPercent, string ZoomText, bool CanDrag, bool WantsPrompt, string GuideLabel);

public sealed record SignView(string Error, string ButtonTitle, bool HasSignature, string Help, string Message, string TabletNote)
{
    public bool MessageShown => Message.Length > 0;
}

public sealed record CheckView(
    bool Saved, string Heading, bool SubShown, string Sub,
    bool SummaryShown, string SummaryName, string SummaryTitle, string SummaryNumber,
    bool WarningShown, string Warning,
    bool HasPlan, string CopiesText, bool CopiesDownEnabled, bool CopiesUpEnabled, string CopiesLabel, string PrintNote,
    string FileLine, bool SaveEnabled, string SaveTitle, bool PicturesEnabled, bool AddAnotherEnabled);

/// <summary>One step in the bar across the top of the form.</summary>
public sealed record StepChip(string Label, bool Locked, bool Current, bool Done, string Says, string ToolTip, string Short);

public sealed record FooterView(
    bool BlockedShown, string BlockedText, bool StatusShown, string StatusText,
    bool BackShown, bool NextShown, string NextTitle, bool NextEnabled);

/// <summary>One card in the "In this print" strip.</summary>
public sealed record JobRow(string Text, string ToolTip, bool Current, string RemoveSays);

public sealed record JobView(bool Shown, string Key, IReadOnlyList<JobRow> Rows, string Count, bool NoteShown, string Note, bool CancelAddShown);

public sealed record ToolsView(string? Open, bool DesignOffered, string DrawerTitle, int Issued, string RegisterButton);

/// <summary>One issued ID in the drawer's list.</summary>
public sealed record IssuedRow(string Head, string When, string Detail);

public sealed record RegisterView(
    bool ExportEnabled, string Note, string Key, IReadOnlyList<IssuedRow> Rows, string More,
    string Account, bool SignInShown, bool SignedInShown, bool RefusedShown, string RefusedNote);

/// <summary>What happens to the window after a move between steps.</summary>
/// <param name="StepChanged">A different step is open than before.</param>
/// <param name="ShowFront">The preview goes back to the front of the card.</param>
/// <param name="Saved">Whether the print on screen still counts as saved.</param>
/// <param name="Focus">Where the cursor goes, or null to leave it.</param>
/// <param name="OpenSigning">The signing screen opens by itself.</param>
public sealed record MoveEffects(bool StepChanged, bool ShowFront, bool Saved, FocusTarget? Focus, bool OpenSigning);

/// <summary>Everything the window shows that is decided by a rule.</summary>
public sealed record Screen(
    int StepIndex, StageView Stage, NameView Name, TitleView Title, NumberView Number, PhotoView Photo, SignView Sign,
    CheckView? Check, IReadOnlyList<StepChip> Chips, FooterView Footer, JobView Job, ToolsView Tools, RegisterView? Register)
{
    /// <summary>How many issued IDs the drawer lists. The two windows differ: see the decision log.</summary>
    public static int IssuedListed(HostPlatform platform) => platform == HostPlatform.Windows ? 25 : 12;

    /// <summary>How many of the issued IDs show before the list is opened: the latest few, the ones looked for most.</summary>
    public const int IssuedFolded = 3;

    /// <summary>
    /// How much of the Issued IDs list shows, and what the button under it
    /// says: the latest IssuedFolded while it is folded, all of them once
    /// opened. A list no longer than that has no button ("").
    /// </summary>
    public static (int Shown, string Toggle) IssuedFold(int rows, bool open) =>
        rows <= IssuedFolded ? (rows, "")
        : open ? (rows, "Show fewer")
        : (IssuedFolded, $"Show all {rows}");

    /// <summary>
    /// The screen for the form as it stands. A refusal that has been put
    /// right is cleared on the way (Session.ClearBlockedIfFixed), as the
    /// windows did each time they redrew.
    /// </summary>
    public static Screen From(Session s, ScreenInput input)
    {
        var p = s.Problems();
        s.ClearBlockedIfFixed(p);
        // An error shows only once its field was touched.
        string Shown(Field f) => s.Touched.Contains(f) ? p[f] : "";
        var onCheck = s.StepIndex == Session.LastStep;
        // once a redraw: making the list asks what the card on screen would be recorded as
        var cards = s.JobCards();
        var tools = ToolRail(s, input);

        return new Screen(
            StepIndex: s.StepIndex,
            Stage: new StageView(
                SideTabsShown: !onCheck,
                LoadingShown: !input.Loaded,
                LoadingText: input.LoadError.Length > 0 ? input.LoadError : "Loading the card…",
                // the stage shows the card while it is being made, the sheet at the end
                CardShown: input.Loaded && !onCheck,
                SheetShown: input.Loaded && onCheck,
                Caption: onCheck
                    ? (s.Layout == SheetMode.Duplex ? "Print both sides, then cut along the marks." : "Cut along the marks and stick each pair back to back.")
                    // both sides have a line, so the card stays where it is when it is turned over
                    : input.Front ? "The front updates as you type." : "The back is the same on every card.",
                CardSays: input.Front
                    ? $"Front of the ID card for {(s.PrintedName.Length > 0 ? s.PrintedName : "a new hire")}"
                    : "Back of the ID card"),
            Name: NameStep(s, p, Shown),
            Title: TitleStep(s, p, Shown),
            Number: NumberStep(s, Shown(Field.Seq)),
            Photo: PhotoStep(s, p, input),
            Sign: SignStep(s, Shown(Field.Signature), input),
            // only where it is shown: neither window reads it on another step
            Check: onCheck ? CheckStep(s, p, cards, input) : null,
            Chips: StepChips(s, p),
            Footer: FooterLine(s, p, onCheck, input),
            Job: JobStrip(s, cards, onCheck, input),
            Tools: tools,
            Register: tools.Open == ToolKey.Register ? RegisterTool(s.Register, input.Platform) : null);
    }

    private static NameView NameStep(Session s, Dictionary<Field, string> p, Func<Field, string> shown)
    {
        // whether the whole name fits says nothing until both halves of it have been typed
        var fit = s.Touched.Contains(Field.Last) && s.Touched.Contains(Field.First) ? p[Field.NameFit] : "";
        return new NameView(
            shown(Field.Last), shown(Field.First), shown(Field.Middle), shown(Field.Suffix),
            // The name as it prints is on the card itself, with where it prints
            // outlined, so this line is only for a name that will not fit.
            Preview: fit,
            PreviewIsError: fit.Length > 0);
    }

    private static TitleView TitleStep(Session s, Dictionary<Field, string> p, Func<Field, string> shown) => new(
        KindError: shown(Field.Kind),
        TitleError: shown(Field.Title),
        CustomShown: s.Role1.IsOther,
        // a title this computer remembered can be dropped again
        ForgetShown: s.Role1.Pick.Length > 0 && !s.Role1.IsOther && !TitleList.IsStandard(s.Role1.Pick),
        SecondShown: s.ShowRole2,
        AddSecondShown: !s.ShowRole2,
        SecondCustomShown: s.ShowRole2 && s.Role2.IsOther,
        SecondError: shown(Field.Title2),
        FitError: s.Touched.Contains(Field.Title) ? p[Field.TitleFit] : "");

    private static NumberView NumberStep(Session s, string error)
    {
        var dup = s.Duplicate();
        var register = s.Register;
        // Nothing to say when the check is working: a taken number turns the
        // field red and names who has it. The only thing worth a line here
        // is the check not working.
        var help = dup is not null && s.Reissue ? $"Replacement for {dup.Value.Entry.Id} ({dup.Value.Entry.Name}, {dup.Value.Entry.IssuedOn})."
            : !register.Shared ? "Checked against this computer only."
            : !register.SignedIn ? "Checked against this computer only. Sign in under Issued IDs to check every computer."
            : s.DupShared == SharedAnswer.Unreachable ? "Could not reach the shared register, so only this computer was checked."
            : "";
        return new NumberView(error, help, ReissueShown: dup is not null && dup.Value.Source != Session.DuplicateSource.Queue);
    }

    private static PhotoView PhotoStep(Session s, Dictionary<Field, string> p, ScreenInput input)
    {
        var photo = s.Photo;
        // a photo that was turned away says so at once, touched or not
        var error = s.Touched.Contains(Field.Photo) || s.PhotoError.Length > 0 ? p[Field.Photo] : "";
        var warnings = photo?.Check.Warnings ?? Array.Empty<string>();
        var (tone, text) = input.CheckingPhoto ? (Tone.Note, "Looking at the photo…")
            : error.Length > 0 ? (Tone.Error, error)
            : warnings.Count > 0 ? (Tone.Warning, string.Join(" ", warnings))
            : photo is { Background: PhotoBackground.Removed } ? (Tone.Note, "The background was removed on this computer. Check the edges on the card.")
            : photo is not null ? (Tone.Note, "")
            : (Tone.Note, input.RemoverReady
                ? "Any clear photo of the person. The app removes the background itself."
                : input.RemoverProblem.Length > 0
                    ? "A cut-out PNG, or a photo on a plain green background. (The background remover is not available on this computer.)"
                    : "Any clear photo of the person.");
        var zoom = Math.Round(s.Adjust.Zoom * 100);
        // When the photo was turned away for something the prompt fixes, the fix is put in front of the operator.
        var wantsPrompt = error.Length > 0 && s.PhotoFixIsPrompt;
        return new PhotoView(
            tone, text,
            ButtonTitle: input.CheckingPhoto ? "Looking at the photo…" : photo is not null ? "Choose a different photo" : "Choose a photo",
            ButtonEnabled: !input.CheckingPhoto,
            HasPhoto: photo is not null,
            FileName: photo?.FileName ?? "",
            ResetEnabled: !s.Adjust.IsNone,
            ZoomPercent: zoom,
            ZoomText: string.Create(CultureInfo.CurrentCulture, $"{zoom:0} %"),
            CanDrag: CanDragPhoto(s, input.Front, input.EditingMap),
            WantsPrompt: wantsPrompt,
            GuideLabel: wantsPrompt ? "Get a photo the ID will accept" : "If the cut-out looks wrong");
    }

    /// <summary>May the photo be dragged on the card itself, and resized with the wheel?</summary>
    public static bool CanDragPhoto(Session s, bool front, bool editingMap) =>
        s.Photo is not null && front && !editingMap && s.StepIndex != Session.LastStep;

    private static SignView SignStep(Session s, string error, ScreenInput input)
    {
        var signature = s.Signature;
        return new SignView(
            Error: error,
            ButtonTitle: signature is null ? "Sign now" : "Sign again",
            HasSignature: signature is not null,
            Help: signature is ScannedSignature scan
                ? $"Scanned: {scan.FileName}. It prints in white on the card."
                : "Signing opens on the whole screen, so the pen has room. It prints in white on the card.",
            // a scan that could not be read is said before a signature that is merely missing
            Message: input.SignatureError.Length > 0 ? input.SignatureError : error,
            TabletNote: input.TabletDriver ? "A pen tablet draws thicker where you press harder."
                : input.Platform == HostPlatform.Windows
                    ? "No pen tablet driver is installed on this computer, so sign with the mouse or use a scan. The installer can add the XP-Pen driver."
                    : "No pen tablet driver was found on this Mac, so sign with the mouse or trackpad, or use a scan. The app can install the XP-Pen driver.");
    }

    private static CheckView CheckStep(Session s, Dictionary<Field, string> p, List<JobCard> cards, ScreenInput input)
    {
        var hasCurrent = s.Ready(p);
        var plan = s.CurrentPlan();
        var data = s.CardData();
        var warnings = s.Photo?.Check.Warnings ?? Array.Empty<string>();
        var copies = s.PrintCopies(plan);
        var total = cards.Count * copies;
        var sheets = plan is null ? 0 : (int)Math.Ceiling(total / (double)plan.PerSheet);
        return new CheckView(
            Saved: input.Saved,
            Heading: input.Saved ? (input.SavedCount > 1 ? $"{input.SavedCount} IDs ready to print" : "ID ready to print")
                : hasCurrent ? "Is everything correct?" : "Ready to print",
            SubShown: !hasCurrent && !input.Saved,
            Sub: $"Printing the {cards.Count} card{Words.S(cards.Count)} already in this print.",
            // a plain summary of the card on screen
            SummaryShown: hasCurrent,
            SummaryName: hasCurrent ? Naming.CollapseSpaces(data.Name) : "",
            SummaryTitle: hasCurrent ? (s.Geometry.Title.Uppercase ? Naming.Upper(data.Title) : data.Title) : "",
            SummaryNumber: hasCurrent && data.Seq is { } seq ? Naming.EmployeeId(s.Year, seq) : "",
            WarningShown: hasCurrent && warnings.Count > 0,
            Warning: string.Join(" ", warnings),
            HasPlan: plan is not null,
            CopiesText: copies.ToString(CultureInfo.InvariantCulture),
            CopiesDownEnabled: copies > 1,
            CopiesUpEnabled: plan is not null && copies < Session.MostCopies(plan),
            CopiesLabel: cards.Count > 1 ? "Copies of each card" : "Copies of this card",
            // how many fit, and the sheet count only when it is more than one
            PrintNote: plan is null ? "" : $"{plan.PerSheet} cards per {s.Paper} sheet" + (sheets > 1 ? $" · {total} cards, {sheets} sheets" : "") + ".",
            FileLine: s.PrintFileLine(cards.Count),
            SaveEnabled: cards.Count > 0 && !input.Saving,
            SaveTitle: input.Saving ? "Saving the PDF…" : "Save the PDF",
            PicturesEnabled: hasCurrent && !input.Saving,
            AddAnotherEnabled: hasCurrent && !input.Saving);
    }

    private static List<StepChip> StepChips(Session s, Dictionary<Field, string> p)
    {
        var stop = s.FurthestStep(p);
        var chips = new List<StepChip>();
        for (var i = 0; i < Session.Steps.Length; i++)
        {
            var label = Session.Steps[i].Label;
            var locked = i > stop;
            var current = i == s.StepIndex;
            // a step cannot be both finished and out of reach: locked wins
            var done = Session.StepDone(i, p) && i != Session.LastStep && !current && !locked;
            chips.Add(new StepChip(label, locked, current, done,
                // not disabled: a locked step can still be asked why it will not open
                Says: locked ? $"{label}. Locked until step {stop + 1}, {Session.Steps[stop].Label}, is finished."
                    : current ? $"{label}. Current step."
                    : $"{label}{(done ? ". Finished." : "")}",
                ToolTip: locked ? $"Finish {Session.Steps[stop].Label.ToLowerInvariant()} first" : "",
                Short: Session.Steps[i].Short));
        }
        return chips;
    }

    private static FooterView FooterLine(Session s, Dictionary<Field, string> p, bool onCheck, ScreenInput input)
    {
        // The line under the buttons: a refusal while there is one. On the steps
        // before the check there is no standing hint any more: the step's own fields say
        // what they need once Next is pressed. On the check step, where the
        // fields are not in view, it still names anything that came undone.
        var missingAll = p.Where(kv => kv.Value.Length > 0).Select(kv => kv.Key).ToList();
        // after "stop adding" the form is empty on purpose and the job is whatever is queued
        var queueOnly = onCheck && s.Queue.Count > 0 && !s.StartedCard;
        var status = !input.Loaded ? ""
            : onCheck ? (missingAll.Count > 0 && !queueOnly ? $"Still needed: {string.Join(", ", missingAll.Select(Session.FieldLabel))}." : "")
            : "";
        return new FooterView(
            BlockedShown: s.BlockedAbout is not null,
            BlockedText: s.BlockedText,
            StatusShown: s.BlockedAbout is null && status.Length > 0,
            StatusText: status,
            BackShown: s.StepIndex > 0 && !(onCheck && input.Saved),
            NextShown: !onCheck,
            // the same word on every step, so the way on always reads the same
            NextTitle: "Next",
            // Next is never disabled for an unfinished step. A disabled button
            // is a dead end: it cannot be focused and it cannot explain
            // itself. Pressing it is how you ask what is missing.
            NextEnabled: input.Loaded);
    }

    private static JobView JobStrip(Session s, List<JobCard> cards, bool onCheck, ScreenInput input)
    {
        var rows = new List<JobRow>();
        for (var i = 0; i < cards.Count; i++)
        {
            var c = cards[i];
            rows.Add(new JobRow($"{i + 1}.  {c.Label} · {c.Id}", $"{c.Label} · {c.Id}", c.Current, $"Take {c.Label} out of this print"));
        }
        return new JobView(
            // the strip is for knowing what is already waiting; it has nothing to say during the very first card
            Shown: (s.Queue.Count > 0 || (onCheck && cards.Count > 0)) && !input.Saved,
            Key: string.Join("|", cards.Select(c => c.Id + c.Label + c.Current)),
            Rows: rows,
            Count: $"{cards.Count} card{Words.S(cards.Count)}",
            NoteShown: s.Queue.Count > 0,
            Note: s.Queue.Count == 0 ? ""
                : onCheck ? "All of these print together, and are issued when you save."
                : "Finish this card and they all go on one sheet.",
            CancelAddShown: s.Queue.Count > 0 && !onCheck);
    }

    /// <summary>What a tool's drawer is headed, and its button named.</summary>
    public static string ToolTitle(string tool, HostPlatform platform) => tool switch
    {
        ToolKey.Register => "Issued IDs",
        ToolKey.Batch => platform == HostPlatform.Windows ? "Batch from CSV" : "Batch from a list",
        ToolKey.Printer => "Printer setting",
        ToolKey.Design => "Card design",
        _ => "",
    };

    private static ToolsView ToolRail(Session s, ScreenInput input)
    {
        // the design can only be changed before an ID is started
        var designOk = s.DesignAllowed;
        var open = !designOk && input.Tool == ToolKey.Design ? null : input.Tool;
        var issued = s.Register.All().Count;
        var counted = issued > 0 ? $"Issued IDs ({issued})" : "Issued IDs";
        return new ToolsView(
            Open: open,
            DesignOffered: designOk,
            DrawerTitle: open is null ? "" : open == ToolKey.Register ? counted : ToolTitle(open, input.Platform),
            Issued: issued,
            RegisterButton: counted);
    }

    private static RegisterView RegisterTool(Register register, HostPlatform platform)
    {
        var all = register.All();
        var listed = IssuedListed(platform);
        var shared = register.Shared;
        var signedIn = register.SignedIn;
        var waiting = register.WaitingForShared;
        var refused = register.Refused;
        return new RegisterView(
            ExportEnabled: all.Count > 0,
            // short; why the list matters is behind the panel's information mark
            Note: all.Count > 0 ? "Latest cards issued on this computer." : "No cards issued yet.",
            Key: string.Join("|", all.Take(listed).Select(e => e.Id + e.IssuedAt)) + all.Count,
            Rows: all.Take(listed).Select(e => new IssuedRow(e.Id + (e.Replacement ? " (replacement)" : ""), e.IssuedOn, $"{e.Name} · {e.Title}")).ToList(),
            More: all.Count > listed ? $"… and {all.Count - listed} more in the saved list." : "",
            // who is signed in to the shared register on this computer
            Account: !shared ? "This copy of the app keeps its own list only."
                : signedIn ? $"Signed in as {register.SignedInAs}."
                    + (waiting > 0 ? $" {waiting} card{Words.S(waiting)} {Words.IsAre(waiting)} still waiting to be written to it." : "")
                : (register.SignInProblem.Length > 0 ? register.SignInProblem + " " : "")
                    + "Sign in with your Keystone account, once on this computer, so numbers are checked against every computer."
                    + (waiting > 0 ? $" {waiting} card{Words.S(waiting)} {Words.IsAre(waiting)} waiting to be written to it." : ""),
            SignInShown: shared && !signedIn,
            SignedInShown: shared && signedIn,
            // what the register has refused: duplicates that must not be handed out
            RefusedShown: refused.Count > 0,
            RefusedNote: refused.Count == 0 ? "" : string.Join("\n", refused.Select(r =>
                $"{r.Card.Id} belongs to {r.Holder.Name}{(r.Holder.IssuedOn.Length > 0 ? $" (issued {r.Holder.IssuedOn})" : "")}. "
                + $"The card made here for {r.Card.Name} is a duplicate: take it back, and make it again with a number that is free.")));
    }

    /* ---- moving between steps ------------------------------------- */

    /// <summary>The control a field's error sends the cursor to.</summary>
    public static FocusTarget? FocusFor(Session s, Field f) => f switch
    {
        Field.Last => FocusTarget.Last,
        Field.First or Field.NameFit => FocusTarget.First,
        Field.Middle => FocusTarget.Middle,
        Field.Suffix => FocusTarget.Suffix,
        Field.Title or Field.TitleFit => s.Role1.IsOther ? FocusTarget.TitleCustom : FocusTarget.TitleChoice,
        Field.Title2 => s.Role2.IsOther ? FocusTarget.Title2Custom : FocusTarget.Title2Choice,
        Field.Seq => FocusTarget.Seq,
        Field.Photo => FocusTarget.PhotoButton,
        Field.Signature => FocusTarget.SignButton,
        Field.Kind => FocusTarget.Kind,
        _ => null,
    };

    /// <summary>
    /// Where the cursor lands when a step opens: the control the step is
    /// about, not merely the first thing in it. Landing on the year box when
    /// the step is about the number sent the typing to the wrong field.
    /// </summary>
    public static FocusTarget LandingFor(Session s, int step) => step switch
    {
        (int)Step.Name => FocusTarget.Last,
        // the step's first question until it is answered, then the job title,
        // then the number once the job is settled
        (int)Step.Title => s.Kind.Length == 0 ? FocusTarget.Kind
            : JobSettled(s) ? FocusTarget.Seq : FocusTarget.TitlePicked,
        (int)Step.Photo => FocusTarget.PhotoButton,
        (int)Step.Sign => FocusTarget.SignButton,
        // Never "Start a new ID": arriving at a step is a move to another step, and that
        // forgets the save (AfterMove). The window sends the cursor there itself, after a save.
        _ => FocusTarget.SaveButton,
    };

    /// <summary>Who the ID is for and the job are answered and fit: what is left on the step is the number.</summary>
    private static bool JobSettled(Session s)
    {
        var p = s.Problems();
        return new[] { Field.Kind, Field.Title, Field.Title2, Field.TitleFit }.All(f => p[f].Length == 0);
    }

    /// <summary>
    /// What follows a move, however it was made: Next, Back, a step chip, an
    /// Edit link, a card added or taken out of the print.
    /// </summary>
    /// <param name="from">The step that was open before the move.</param>
    /// <param name="result">What Next or a chip answered; null for a move that cannot be refused.</param>
    /// <param name="saved">Whether the print on screen had been saved.</param>
    /// <param name="openSigningOnArrival">The window opens the signing screen by itself on reaching the signature step.</param>
    public static MoveEffects AfterMove(Session s, int from, MoveResult? result, bool saved, bool openSigningOnArrival)
    {
        var changed = s.StepIndex != from;
        // a different step is a different piece of work: what was saved is behind us
        var stillSaved = saved && !changed;
        var focus = result?.FocusField is { } field ? FocusFor(s, field)
            : changed ? LandingFor(s, s.StepIndex)
            : null;
        return new MoveEffects(
            StepChanged: changed,
            ShowFront: s.StepIndex != Session.LastStep,
            Saved: stillSaved,
            Focus: focus,
            OpenSigning: s.StepIndex == (int)Step.Sign && from != (int)Step.Sign && openSigningOnArrival && s.Signature is null);
    }

    /* ---- the tools: what they say ---------------------------------- */

    /// <summary>The three lines under the batch tool's choosers.</summary>
    public static (string List, string Photos, string Signatures) BatchChosen(string? listPath, int photos, int signatures) => (
        // short; what the columns are and how files are matched is behind the panel's information marks
        listPath is null ? "No list chosen yet." : Path.GetFileName(listPath),
        photos > 0 ? $"{photos} photo{Words.S(photos)} chosen." : "No photos chosen yet.",
        // "optional" is said here, in the line under the name, rather than in a tag beside it
        signatures > 0 ? $"{signatures} signature{Words.S(signatures)} chosen." : "Optional: rows without one get no signature.");

    /// <summary>What the chosen batch layout comes to on the chosen paper.</summary>
    public static string BatchLayoutLine(string layout, string paper)
    {
        if (layout == Batch.LayoutCards) return "Two card-sized pages per person.";
        var plan = Sheets.Plan(paper, layout == Batch.LayoutPairs ? SheetMode.Pairs : SheetMode.Duplex);
        return plan is null ? "" : $"{plan.PerSheet} cards per sheet ({plan.Cols} × {plan.Rows}, {(plan.Landscape ? "landscape" : "portrait")}).";
    }

    /// <summary>
    /// A batch's outcome at a glance: how many cards were made, how many of
    /// those want a look (a warning, or no signature), and how many rows were
    /// skipped; and the rows in the order to read them: the skipped first,
    /// then those to look at, then the rest, each group in list order.
    /// </summary>
    public static (int Made, int ToCheck, int Skipped, IReadOnlyList<BatchRowResult> Rows) BatchSummary(IReadOnlyList<BatchRowResult> results)
    {
        static int Rank(BatchRowResult r) => !r.Ok ? 0 : r.Warnings.Count > 0 || r.NoSignature ? 1 : 2;
        var rows = results.Select((r, i) => (r, i)).OrderBy(x => Rank(x.r)).ThenBy(x => x.i).Select(x => x.r).ToList();
        return (results.Count(r => r.Ok), results.Count(r => Rank(r) == 1), results.Count(r => !r.Ok), rows);
    }

    /// <summary>How one row of a batch is reported. The words say which it is; the tone only agrees with them.</summary>
    public static (Tone Tone, string Head, string Text) BatchRow(BatchRowResult r)
    {
        var (tone, note) = !r.Ok ? (Tone.Error, r.Problem)
            : r.Warnings.Count > 0 || r.NoSignature
                ? (Tone.Warning, string.Join(" ", new[] { r.NoSignature ? "No signature on this card." : "" }.Concat(r.Warnings).Where(t => t.Length > 0)))
                : (Tone.Note, "Ready.");
        return (tone, r.Ok ? $"{r.Id} · {r.Label}" : $"Line {r.Line} · {r.Label}", (r.Ok ? "" : "Skipped: ") + note);
    }

    /// <summary>
    /// Text that will not read against the artwork. One sentence names the
    /// fields and the fix; the lines under it carry the ratios, for whoever
    /// wants to take them back to the designer. Head is "" when everything reads.
    /// </summary>
    public static (string Head, IReadOnlyList<string> Lines) ContrastReport(IReadOnlyList<ContrastWarning> warnings)
    {
        if (warnings.Count == 0) return ("", Array.Empty<string>());
        var names = warnings.Select(w => w.Label).ToList();
        var list = names.Count > 1 ? $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}" : names[0];
        return (
            $"The {list} would be hard to read on this artwork. Put a darker shape behind {(warnings.Count > 1 ? "those boxes" : "that box")}.",
            warnings.Select(w => $"{w.Label}: prints in {w.Ink}, contrast {w.Ratio:0.0}:1 against the artwork (needs {w.Need:0}:1).").ToList());
    }
}
