// The form: what has been typed, what is still wrong with it, which step
// may be open, and what is waiting to print.
//
// The window draws this and nothing else decides anything. In particular
// FurthestStep is the only place that says how far forward the form may go,
// so the step chips, the Next button and a late answer from the register
// cannot disagree about it.

using System.Globalization;
using System.Text.RegularExpressions;

namespace IdMaker.Core;

public enum Step { Name = 0, Title = 1, Number = 2, Photo = 3, Sign = 4, Check = 5 }

public enum Field { Last, First, Middle, Suffix, NameFit, Title, Title2, TitleFit, Seq, Photo, Signature, Kind }

/// <summary>What the shared register says about the number on the form.</summary>
public enum SharedAnswer
{
    /// <summary>Definitely not there (or there is no shared register).</summary>
    Free,
    /// <summary>Already issued: SharedEntry says to whom.</summary>
    Taken,
    /// <summary>Not known yet. Not knowing is not the same as knowing it is free, so this keeps the step shut.</summary>
    Pending,
    /// <summary>Could not be reached. Warns, but does not block: a sheet being down cannot stop HR working.</summary>
    Unreachable,
}

/// <summary>One role: a title picked from the chips, or typed under "Other…".</summary>
public sealed class RoleChoice
{
    public const string Other = "\u0001other";
    public string Pick { get; set; } = "";
    public string Custom { get; set; } = "";
    public bool IsOther => Pick == Other;
    public string Value => IsOther ? Naming.CollapseSpaces(Custom) : Pick;
    public void Clear() { Pick = ""; Custom = ""; }
}

/// <summary>Everything the front of one card is drawn from.</summary>
public sealed record CardData(
    string Name, string Title, int Year, int? Seq,
    RgbaImage? Photo, PhotoPlacement? Placement, Signature? Signature);

/// <summary>A finished card waiting to print with the others.</summary>
public sealed record JobCard(string Id, string Label, string Title, CardData Data, RegisterEntry Entry, bool Current);

/// <summary>
/// What pressing Next, or a step chip, led to: the field to send the cursor
/// to when the move was refused, or null. Where the form now stands is
/// Session.StepIndex.
/// </summary>
public sealed record MoveResult(Field? FocusField);

/// <summary>
/// The four tools beside the form, by the name each window keeps the open
/// one under. The Windows markup carries the same words as its buttons' tags.
/// </summary>
public static class ToolKey
{
    public const string Register = "register";
    public const string Batch = "batch";
    public const string Printer = "printer";
    public const string Design = "design";
}

public sealed partial class Session
{
    public const int LastStep = (int)Step.Check;

    public static readonly (Step Id, string Label, Field[] Fields)[] Steps =
    {
        (Step.Name, "Name", new[] { Field.Last, Field.First, Field.Middle, Field.Suffix, Field.NameFit }),
        (Step.Title, "Job title", new[] { Field.Kind, Field.Title, Field.Title2, Field.TitleFit }),
        (Step.Number, "Number", new[] { Field.Seq }),
        (Step.Photo, "Photo", new[] { Field.Photo }),
        (Step.Sign, "Signature", new[] { Field.Signature }),
        (Step.Check, "Check", Array.Empty<Field>()),
    };

    public static string FieldLabel(Field f) => f switch
    {
        Field.Last => "last name", Field.First => "first name", Field.Middle => "middle name", Field.Suffix => "suffix",
        Field.NameFit => "a shorter name", Field.Title => "job title", Field.Title2 => "second role",
        Field.TitleFit => "a shorter job title", Field.Seq => "employee number", Field.Photo => "photo",
        Field.Kind => "employee or intern",
        _ => "signature",
    };

    // The three print settings kept between runs: the key each is stored under and the values it takes.
    private const string RotateBackKey = "rotate-back", On = "1", Off = "0";
    private const string FlipKey = "duplex-flip", FlipShort = "short", FlipLong = "long";
    private const string LayoutKey = "print-layout", LayoutDuplex = "duplex", LayoutPairs = "pairs";

    private readonly Register _register;
    private readonly TitleList _titles;
    private readonly IKeyValueStore _settings;
    private readonly Func<DateTime> _now;
    private readonly Action<Action> _post;
    private int _dupToken;

    /// <param name="post">Runs an action back on the thread that owns the form (the window's dispatcher).</param>
    public Session(Register register, TitleList titles, IKeyValueStore settings, Func<DateTime>? now = null, Action<Action>? post = null)
    {
        _register = register;
        _titles = titles;
        _settings = settings;
        _now = now ?? (() => DateTime.Now);
        _post = post ?? (a => a());
        Year = _now().Year;
        RotateBack = settings.Get(RotateBackKey) == On;
        Flip = settings.Get(FlipKey) == FlipShort ? DuplexFlip.ShortEdge : DuplexFlip.LongEdge;
        Layout = settings.Get(LayoutKey) == LayoutDuplex ? SheetMode.Duplex : SheetMode.Pairs;
    }

    /// <summary>The register this form issues into. For Core's own use (Screen); the windows go through the Session.</summary>
    internal Register Register => _register;

    /// <summary>Raised whenever anything the window shows may have changed without the window asking.</summary>
    public event Action? Changed;

    /* ---- what the form holds -------------------------------------- */

    /// <summary>Measures text in the card's fonts. Until it is set, whether a name fits cannot be answered.</summary>
    public ITextMeasurer? Measurer { get; set; }
    public CardGeometry Geometry { get; set; } = CardGeometry.BuiltIn;

    public int Year { get; private set; }
    public string Last { get; private set; } = "";
    public string First { get; private set; } = "";
    public string Middle { get; private set; } = "";
    public string Suffix { get; private set; } = "";
    /// <summary>Who the ID is for: PersonKind.Employee, PersonKind.Intern, or "" until it is chosen.</summary>
    public string Kind { get; private set; } = "";
    public RoleChoice Role1 { get; } = new();
    public RoleChoice Role2 { get; } = new();
    public bool ShowRole2 { get; private set; }
    public string Seq { get; private set; } = "";
    public PhotoResult? Photo { get; private set; }
    public string PhotoError { get; private set; } = "";
    public bool PhotoFixIsPrompt { get; private set; }
    public PhotoAdjust Adjust { get; private set; } = PhotoAdjust.None;
    public Signature? Signature { get; private set; }
    public bool Reissue { get; private set; }
    public bool Example { get; private set; }
    public int StepIndex { get; private set; }
    /// <summary>
    /// Goes up each time the card on screen is replaced by a blank one. Work
    /// that finishes later (a photo being looked at, a scan being read)
    /// compares it with what it was when it started, and stands down if the
    /// card it was for is gone.
    /// </summary>
    public int CardSerial { get; private set; }
    public HashSet<Field> Touched { get; } = new();

    public RegisterEntry? DupLocal { get; private set; }
    public SharedAnswer DupShared { get; private set; } = SharedAnswer.Free;
    public RegisterEntry? SharedEntry { get; private set; }
    /// <summary>The check against the shared register that is in flight, for tests to wait on.</summary>
    public Task DuplicateCheck { get; private set; } = Task.CompletedTask;

    public List<JobCard> Queue { get; } = new();
    public RegisterEntry? ReviewEntry { get; private set; }

    /// <summary>The step a refusal on screen is about, and what it says. Null when there is none.</summary>
    public int? BlockedAbout { get; private set; }
    public string BlockedText { get; private set; } = "";

    // printing
    public bool RotateBack { get; private set; }
    public DuplexFlip Flip { get; private set; }
    public SheetMode Layout { get; private set; }
    public string Paper { get; private set; } = CardConfig.DefaultPaper;
    public int? Copies { get; private set; }
    public int PreviewPage { get; set; }

    /* ---- derived -------------------------------------------------- */

    public int? SeqNumber => CardRules.SeqNumber(Seq);

    public NameParts NameParts => new(Last, First, Middle, Suffix);
    public string Role2Value => ShowRole2 ? Role2.Value : "";
    public string JobTitle => Naming.ComposeTitle(Role1.Value, Role2Value);
    public string PrintedName => Naming.ComposeName(NameParts);

    public CardData CardData() => new(
        PrintedName, JobTitle, Year, SeqNumber,
        Photo?.Image, Photo?.Check.Placement?.Adjusted(Adjust, Geometry.Photo), Signature);

    public CardTextLayout? TextLayout() =>
        Measurer is null ? null : TextFit.Layout(Measurer, Geometry, PrintedName, JobTitle, Year, SeqNumber);

    /// <summary>What is wrong with each field; an empty string where nothing is.</summary>
    public Dictionary<Field, string> Problems()
    {
        var layout = TextLayout();
        const string badChars = "Use letters, spaces, periods, hyphens and apostrophes only.";
        const string badTitle = "Use letters, numbers, spaces and . , & ( ) / - only.";
        string last = Naming.CollapseSpaces(Last), first = Naming.CollapseSpaces(First);
        string middle = Naming.CollapseSpaces(Middle), suffix = Naming.CollapseSpaces(Suffix);
        var dup = Duplicate();
        return new Dictionary<Field, string>
        {
            [Field.Last] = last.Length == 0 ? "Enter the last name." : !CardRules.NamePrints(last) ? badChars : "",
            [Field.First] = first.Length == 0 ? "Enter the first name." : !CardRules.NamePrints(first) ? badChars : "",
            [Field.Middle] = middle.Length > 0 && !CardRules.NamePrints(middle) ? badChars : "",
            [Field.Suffix] = suffix.Length > 0 && !CardRules.SuffixPrints(suffix) ? "Use something like Jr., Sr., II or III." : "",
            [Field.NameFit] = last.Length > 0 && first.Length > 0 && layout is { Name.Fits: false }
                ? "This name is too long for the card, even at the smallest size. Try a shorter first name." : "",
            // never guessed: the attendance system takes interns, and only interns, from this
            [Field.Kind] = Kind.Length == 0 ? "Choose whether this ID is for an employee or an intern." : "",
            [Field.Title] = Role1.Pick.Length == 0 ? "Choose the job title."
                : Role1.IsOther && Role1.Value.Length == 0 ? "Type the job title."
                : Role1.IsOther && !CardRules.TitlePrints(Role1.Value) ? badTitle
                : "",
            [Field.Title2] = !ShowRole2 ? ""
                : Role2.Value.Length > 0 && Naming.SameText(Role2.Value, Role1.Value) ? "This is the same as the job title above. Pick a different role, or remove it."
                : Role2.IsOther && Role2.Value.Length == 0 ? "Type the second role, or remove it."
                : Role2.IsOther && !CardRules.TitlePrints(Role2.Value) ? badTitle
                : Role2.Pick.Length == 0 ? "Choose the second role, or remove it."
                : "",
            [Field.TitleFit] = JobTitle.Length > 0 && layout is { Title.Fits: false }
                ? "This job title is too long for the card, even at the smallest size. Try a shorter wording." : "",
            [Field.Seq] = Seq.Length == 0 ? "Enter the employee number HR assigned, such as 124."
                : SeqNumber is null ? $"Use 1 to {CardRules.MostSeqDigits} digits, such as 124."
                : dup is not null && !Reissue ? DuplicateMessage(dup.Value)
                : DupShared == SharedAnswer.Pending ? "Checking the shared register for this number…"
                : "",
            [Field.Photo] = PhotoError.Length > 0 ? PhotoError : Photo is null ? "Add the new hire’s photo." : "",
            [Field.Signature] = Signature is { HasInk: true } ? "" : "Ask the new hire to sign.",
        };
    }

    public bool Ready(Dictionary<Field, string>? p = null) => (p ?? Problems()).Values.All(v => v.Length == 0);

    /* ---- duplicates ----------------------------------------------- */

    public enum DuplicateSource { Queue, Shared, Local }

    /// <summary>
    /// The entry that already holds this number: one waiting in this print,
    /// or one already issued (shared wins over local).
    /// </summary>
    public (RegisterEntry Entry, DuplicateSource Source)? Duplicate()
    {
        if (SeqNumber is { } seq)
        {
            var id = Naming.EmployeeId(Year, seq);
            var queued = Queue.FirstOrDefault(q => q.Id == id);
            if (queued is not null) return (queued.Entry, DuplicateSource.Queue);
        }
        if (DupShared == SharedAnswer.Taken && SharedEntry is not null) return (SharedEntry, DuplicateSource.Shared);
        if (DupLocal is not null) return (DupLocal, DuplicateSource.Local);
        return null;
    }

    private static string DuplicateMessage((RegisterEntry Entry, DuplicateSource Source) d)
    {
        var e = d.Entry;
        if (d.Source == DuplicateSource.Queue) return $"{e.Id} is already waiting in this print, for {e.Name}. Use a different number.";
        var where = d.Source == DuplicateSource.Shared ? "in the shared register" : "on this computer";
        return $"{e.Id} was already issued to {e.Name} on {e.IssuedOn} ({where}). Check the number with HR, or tick “Replacement card” below.";
    }

    /// <summary>
    /// Is this number already taken? Answered from memory when it can be.
    /// Only a register that has not been read yet goes to the network, and
    /// until that answers the number is Pending rather than clean.
    /// </summary>
    public void RefreshDuplicate()
    {
        var seq = SeqNumber;
        DupLocal = _register.Find(Year, seq);
        SharedEntry = null;
        var token = ++_dupToken;
        if (!_register.Shared || seq is null) { DupShared = SharedAnswer.Free; return; }

        var (known, entry) = _register.FindSharedNow(Year, seq);
        if (known) { SetShared(entry); return; }

        DupShared = SharedAnswer.Pending;
        DuplicateCheck = SettleWhenLoaded(token);
    }

    private void SetShared(RegisterEntry? entry)
    {
        SharedEntry = entry;
        DupShared = entry is null ? SharedAnswer.Free : SharedAnswer.Taken;
    }

    private async Task SettleWhenLoaded(int token)
    {
        var loaded = await _register.LoadIndex().ConfigureAwait(false);
        var done = new TaskCompletionSource();
        _post(() =>
        {
            try
            {
                if (token != _dupToken) return;             // the number changed while we were asking
                if (!loaded) DupShared = SharedAnswer.Unreachable;
                else SetShared(_register.FindSharedNow(Year, SeqNumber).Entry);
                SettleDuplicate();
            }
            finally { done.SetResult(); }
        });
        await done.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// The register has answered. If it answered late — after the number was
    /// accepted and the form moved on — the step it belongs to is no longer
    /// finished, so go back to it and say why, rather than letting a card go
    /// out on a number somebody else already used.
    /// </summary>
    private void SettleDuplicate()
    {
        var p = Problems();
        var stop = FurthestStep(p);
        if (stop < StepIndex)
        {
            SayBlocked(stop, p);
            GoStep(stop);
        }
        Changed?.Invoke();
    }

    /* ---- steps ---------------------------------------------------- */

    public static List<Field> StepProblems(int i, Dictionary<Field, string> p) => Steps[i].Fields.Where(f => p[f].Length > 0).ToList();
    public static bool StepDone(int i, Dictionary<Field, string> p) => StepProblems(i, p).Count == 0;

    /// <summary>
    /// The furthest step that may be open: the first one that is not
    /// finished, or the end if they all are. Going back is always allowed;
    /// going forward stops here, and this is the only place that decides it.
    /// </summary>
    public int FurthestStep(Dictionary<Field, string>? p = null)
    {
        p ??= Problems();
        for (var i = 0; i < Steps.Length; i++) if (!StepDone(i, p)) return i;
        return LastStep;
    }

    /// <summary>
    /// Open a step. `force` is for the two places that mean it: the finished
    /// example, and going back to a print that is already queued.
    /// </summary>
    public void GoStep(int i, bool force = false)
    {
        var want = Math.Clamp(i, 0, LastStep);
        StepIndex = force ? want : Math.Min(want, FurthestStep());
        if (StepIndex == LastStep) BuildReview();
    }

    /// <summary>Say that the way on is shut, and why.</summary>
    private void SayBlocked(int i, Dictionary<Field, string> p)
    {
        BlockedAbout = i;
        var first = StepProblems(i, p).FirstOrDefault();
        var reason = StepProblems(i, p).Count > 0 ? p[first] : $"Finish {Steps[i].Label.ToLowerInvariant()}.";
        BlockedText = i == StepIndex
            ? $"Can’t continue yet. {reason}"
            : $"Can’t continue yet. Step {i + 1}, {Steps[i].Label}, needs fixing first.";
    }

    /// <summary>
    /// A refusal is about a moment, and the moment ends when the thing it
    /// named is fixed. The window calls this each time it redraws.
    /// </summary>
    public void ClearBlockedIfFixed(Dictionary<Field, string> p)
    {
        if (BlockedAbout is { } i && StepDone(i, p)) { BlockedAbout = null; BlockedText = ""; }
    }

    private static Field? FirstProblem(int i, Dictionary<Field, string> p)
    {
        var list = StepProblems(i, p);
        return list.Count > 0 ? list[0] : null;
    }

    /// <summary>Move on, or say what is still missing.</summary>
    public MoveResult NextStep()
    {
        // Moving on from the name tidies its capitals, however the move was
        // made: the mouse, the Enter key, or a pen that never took the focus
        // out of the box.
        if (StepIndex == (int)Step.Name)
        {
            foreach (var part in new[] { Field.Last, Field.First, Field.Middle, Field.Suffix }) TidyNamePart(part);
        }
        var p = Problems();
        var stop = FurthestStep(p);

        // something behind us came undone — a name that stopped fitting, a
        // number that turned out to be a duplicate once the register answered
        if (stop < StepIndex)
        {
            SayBlocked(stop, p);
            GoStep(stop);
            return new MoveResult(FirstProblem(stop, p));
        }

        if (StepProblems(StepIndex, p).Count > 0)
        {
            foreach (var f in Steps[StepIndex].Fields) Touched.Add(f);
            SayBlocked(StepIndex, p);
            return new MoveResult(FirstProblem(StepIndex, p));
        }
        if (StepIndex < LastStep) GoStep(StepIndex + 1);
        return new MoveResult(null);
    }

    public void Back() => GoStep(StepIndex - 1);

    public MoveResult ChipPressed(int i)
    {
        var p = Problems();
        var stop = FurthestStep(p);
        if (i <= stop) { GoStep(i); return new MoveResult(null); }
        // locked: go to what is blocking it and say which step that is
        SayBlocked(stop, p);
        GoStep(stop);
        return new MoveResult(FirstProblem(stop, p));
    }

    /* ---- typing --------------------------------------------------- */

    public void Touch(Field f) => Touched.Add(f);

    public void SetNamePart(Field part, string value)
    {
        Example = false;
        Touched.Add(part);
        switch (part)
        {
            case Field.Last: Last = value; break;
            case Field.First: First = value; break;
            case Field.Middle: Middle = value; break;
            case Field.Suffix: Suffix = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(part));
        }
    }

    /// <summary>
    /// The field was left: tidy the capitals now, rather than fighting the
    /// typing. Returns the value the field should show.
    /// </summary>
    public string LeaveNamePart(Field part)
    {
        var tidy = TidyNamePart(part);
        Touched.Add(part);
        return tidy;
    }

    private string TidyNamePart(Field part)
    {
        var current = part switch { Field.Last => Last, Field.First => First, Field.Middle => Middle, _ => Suffix };
        var tidy = Naming.AutoCapitalise(current, suffix: part == Field.Suffix);
        if (tidy == current) return current;
        switch (part)
        {
            case Field.Last: Last = tidy; break;
            case Field.First: First = tidy; break;
            case Field.Middle: Middle = tidy; break;
            default: Suffix = tidy; break;
        }
        return tidy;
    }

    /// <summary>Say who the ID is for. Anything but employee or intern clears the choice.</summary>
    public void PickKind(string kind) { Example = false; Touched.Add(Field.Kind); Kind = PersonKind.Parse(kind); }

    public void PickRole1(string pick)
    {
        Example = false;
        Touched.Add(Field.Title);
        Role1.Pick = pick;
        // the second role can never repeat the first
        if (ShowRole2 && Role2.Value.Length > 0 && Naming.SameText(Role2.Value, Role1.Value)) Role2.Pick = "";
    }

    public void PickRole2(string pick) { Example = false; Touched.Add(Field.Title2); Role2.Pick = pick; }
    public void TypeRole1(string text) { Example = false; Touched.Add(Field.Title); Role1.Custom = text; }
    public void TypeRole2(string text) { Example = false; Touched.Add(Field.Title2); Role2.Custom = text; }
    public void AddSecondRole() { ShowRole2 = true; Example = false; }

    public void RemoveSecondRole()
    {
        ShowRole2 = false;
        Role2.Clear();
        Touched.Remove(Field.Title2);
        Example = false;
    }

    public IReadOnlyList<string> TitleOptions() => _titles.All();

    /// <summary>
    /// The second role can never repeat the first, so whatever the job title
    /// is disappears from the second list. "Other…" always stays.
    /// </summary>
    public static bool SecondRoleHides(string title, string firstRole) =>
        firstRole.Length > 0 && title != RoleChoice.Other && Naming.SameText(title, firstRole);

    public void ForgetTitle(string title)
    {
        _titles.Forget(title);
        if (Role1.Pick == title) Role1.Pick = "";
        if (Role2.Pick == title) Role2.Pick = "";
    }

    /// <summary>Digits only, four at most. Returns what the box should show.</summary>
    public string SetSeq(string typed)
    {
        Example = false;
        Touched.Add(Field.Seq);
        var digits = CardRules.SeqTyped(typed);
        Seq = digits;
        Reissue = false;
        RefreshDuplicate();
        return digits;
    }

    public IEnumerable<int> YearsOffered()
    {
        var thisYear = _now().Year;
        for (var y = thisYear - CardConfig.YearsBack; y <= thisYear + CardConfig.YearsAhead; y++) yield return y;
    }

    public void SetYear(int year)
    {
        Year = year;
        Touched.Add(Field.Seq);
        Reissue = false;
        RefreshDuplicate();
    }

    public void SetReissue(bool on) => Reissue = on;

    /* ---- photo and signature -------------------------------------- */

    /// <summary>A picture could not even be opened.</summary>
    public void PhotoFailed(string error)
    {
        Example = false;
        Touched.Add(Field.Photo);
        Adjust = PhotoAdjust.None;
        Photo = null;
        PhotoError = error;
        PhotoFixIsPrompt = false;
    }

    public void AcceptPhoto(PhotoResult result)
    {
        Example = false;
        Touched.Add(Field.Photo);
        Adjust = PhotoAdjust.None;
        if (result.Check.Ok) { Photo = result; PhotoError = ""; PhotoFixIsPrompt = false; }
        else
        {
            Photo = null;
            PhotoError = result.Check.Problem.Length > 0 ? result.Check.Problem : "This photo can’t be used.";
            PhotoFixIsPrompt = result.Check.FixIsPrompt;
        }
    }

    private static double R3(double v) => Math.Round(v, 3, MidpointRounding.AwayFromZero);

    public void Nudge(double dxMm, double dyMm)
    {
        if (Photo is null) return;
        Adjust = Adjust with { Dx = R3(Adjust.Dx + dxMm), Dy = R3(Adjust.Dy + dyMm) };
    }

    public void SetAdjustOffset(double dxMm, double dyMm)
    {
        if (Photo is null) return;
        Adjust = Adjust with { Dx = R3(dxMm), Dy = R3(dyMm) };
    }

    public void SetZoom(double zoom)
    {
        if (Photo is null) return;
        Adjust = Adjust with { Zoom = R3(Math.Clamp(zoom, Geometry.Photo.ZoomMin, Geometry.Photo.ZoomMax)) };
    }

    public void ResetAdjust() => Adjust = PhotoAdjust.None;

    public void SetSignature(Signature? signature)
    {
        Example = false;
        Touched.Add(Field.Signature);
        Signature = signature is { HasInk: true } ? signature : null;
    }

    /* ---- the print job -------------------------------------------- */

    /// <summary>Anything at all typed, picked or added to the card on screen?</summary>
    public bool StartedCard =>
        Last.Trim().Length > 0 || First.Trim().Length > 0 || Middle.Trim().Length > 0 || Suffix.Trim().Length > 0
        || Seq.Trim().Length > 0 || Role1.Pick.Length > 0 || Photo is not null || Signature is { HasInk: true };

    /// <summary>Is there a card in progress that a change of design would spoil?</summary>
    public bool DesignAllowed => StepIndex == 0 && !StartedCard && Queue.Count == 0;

    private RegisterEntry MakeEntry()
    {
        var data = CardData();
        // SAFETY: an entry is only made for a card that is Ready, and a card with no number is not.
        var seq = data.Seq!.Value;
        return RegisterEntry.Issue(
            Year, seq, Naming.CollapseSpaces(data.Name), Naming.CollapseSpaces(data.Title),
            new NameParts(Naming.CollapseSpaces(Last), Naming.CollapseSpaces(First), Naming.CollapseSpaces(Middle), Naming.CollapseSpaces(Suffix)),
            Role1.Value, Role2Value, Kind, replacement: Duplicate() is not null, _now());
    }

    /// <summary>Called on arriving at the last step: fixes what this card will be recorded as.</summary>
    public void BuildReview() => ReviewEntry = Ready() ? MakeEntry() : null;

    /// <summary>Everyone in this print: the queued cards, then the one on screen if it is finished.</summary>
    public List<JobCard> JobCards()
    {
        var list = new List<JobCard>(Queue);
        if (Measurer is not null && Ready())
        {
            var data = CardData();
            // SAFETY: Ready() is true, and a card with no number is not ready.
            list.Add(new JobCard(Naming.EmployeeId(Year, data.Seq!.Value), Naming.CollapseSpaces(data.Name), data.Title, data, ReviewEntry ?? MakeEntry(), true));
        }
        return list;
    }

    /// <summary>Put the finished card in the queue and start a fresh one. False when there is nothing to bank yet.</summary>
    public bool AddAnother()
    {
        if (!Ready()) { GoStep(0); return false; }
        var data = CardData();
        // SAFETY: Ready() was true just above, and a card with no number is not ready.
        Queue.Add(new JobCard(Naming.EmployeeId(Year, data.Seq!.Value), Naming.CollapseSpaces(data.Name), data.Title, data, ReviewEntry ?? MakeEntry(), false));
        ClearCard(keepQueue: true);
        GoStep(0);
        return true;
    }

    /// <summary>
    /// Give up on the card being filled in and go back to the print. The
    /// queue and the printing options are left exactly as they were, so this
    /// undoes "Add another ID" rather than the whole job.
    /// </summary>
    public void CancelAdding()
    {
        if (Queue.Count == 0) return;
        ClearCard(keepQueue: true);
        // the card on screen is blank now, so the cap would send us to step
        // 1: but the print this returns to is already made and waiting
        GoStep(LastStep, force: true);
    }

    public void RemoveFromQueue(int index)
    {
        if (index < 0 || index >= Queue.Count) return;
        Queue.RemoveAt(index);
        RefreshDuplicate();
        // with nothing queued and nothing on screen there is no print left to look at
        if (StepIndex == LastStep && Queue.Count == 0 && !Ready()) GoStep(0);
    }

    public void ClearCard(bool keepQueue = false)
    {
        CardSerial++;
        Last = First = Middle = Suffix = "";
        Kind = "";
        Role1.Clear();
        Role2.Clear();
        ShowRole2 = false;
        Seq = "";
        Photo = null; PhotoError = ""; PhotoFixIsPrompt = false; Signature = null;
        Adjust = PhotoAdjust.None;
        Year = _now().Year;
        Reissue = false;
        Touched.Clear();
        Example = false;
        ReviewEntry = null;
        BlockedAbout = null; BlockedText = "";
        if (!keepQueue) { Queue.Clear(); Copies = null; }
        PreviewPage = 0;
        RefreshDuplicate();
    }

    public void NewId() { ClearCard(); GoStep(0); }

    /// <summary>
    /// Fill the form with the sample card and jump to the end. It has no
    /// button: HR has no use for a fake card in a tool that issues real
    /// ones. The render tests drive it, because they need one card they can
    /// reproduce exactly. An example is never written to the register.
    /// </summary>
    public void LoadExample(PhotoResult? photo, int year)
    {
        ClearCard();
        Last = "Aureo"; First = "Christopher Joseph"; Middle = "Villanueva"; Suffix = "";
        Kind = PersonKind.Employee;
        Role1.Pick = "IT Associate";
        Seq = "122";
        Year = year;
        RefreshDuplicate();
        Photo = photo is { Check.Ok: true } ? photo : null;
        Signature = ExampleSignature();
        Example = true;
        GoStep(LastStep, force: true);
    }

    public static DrawnSignature ExampleSignature()
    {
        var first = new List<InkPoint>();
        for (var i = 0; i < 36; i++) first.Add(new InkPoint(20 + i * 7, 70 + Math.Sin(i / 3.2) * 26 - i * 0.6));
        return new DrawnSignature(new IReadOnlyList<InkPoint>[]
        {
            first,
            new InkPoint[] { new(92, 18), new(98, 60), new(108, 112) },
            new InkPoint[] { new(210, 92), new(290, 88), new(330, 86) },
        });
    }

    /* ---- printing options ----------------------------------------- */

    public SheetPlan? CurrentPlan() => Sheets.Plan(Paper, Layout);

    /// <summary>
    /// Copies of each card. One each by default: a sheet of nine identical
    /// IDs is nine ID cards for one person, which is a security problem
    /// rather than a convenience. Ask for more and you get more.
    /// </summary>
    public int PrintCopies(SheetPlan? plan)
    {
        if (plan is null || Copies is null) return 1;
        return Math.Clamp(Copies.Value, 1, MostCopies(plan));
    }

    /// <summary>However many copies are asked for, one card never fills more sheets than this.</summary>
    public const int MostSheetsOfOneCard = 20;

    /// <summary>The most copies of each card this paper and layout allow.</summary>
    public static int MostCopies(SheetPlan plan) => plan.PerSheet * MostSheetsOfOneCard;

    public void SetPaper(string paper) { Paper = CardConfig.PaperOrDefault(paper).Key; Copies = null; PreviewPage = 0; }
    public void SetCopies(int? copies) => Copies = copies;

    public void NudgeCopies(int by)
    {
        var plan = CurrentPlan();
        var max = plan is null ? 200 : MostCopies(plan);
        Copies = Math.Clamp(PrintCopies(plan) + by, 1, max);
    }

    /// <summary>Switch between one sheet of pairs and two sides of the paper.</summary>
    public void SetLayout(SheetMode mode)
    {
        if (Layout == mode) return;
        Layout = mode;
        PreviewPage = 0;
        Copies = null;                      // the two layouts hold different numbers
        _settings.Set(LayoutKey, mode == SheetMode.Duplex ? LayoutDuplex : LayoutPairs);
    }

    public void SetRotateBack(bool on) { RotateBack = on; _settings.Set(RotateBackKey, on ? On : Off); }
    public void SetFlip(DuplexFlip flip) { Flip = flip; _settings.Set(FlipKey, flip == DuplexFlip.ShortEdge ? FlipShort : FlipLong); }

    /// <summary>
    /// Which way up the backs are drawn. A short-edge turn lands the back of
    /// the cut card upside down, so it is drawn the other way up to cancel that.
    /// </summary>
    public bool BackUpsideDown(SheetPlan plan) =>
        plan.Mode == SheetMode.Duplex && Flip == DuplexFlip.ShortEdge ? !RotateBack : RotateBack;

    /// <summary>The cards to print, each repeated by the number of copies, and the pages they fall on.</summary>
    public (SheetPlan Plan, List<CardData> Entries, IReadOnlyList<SheetPage> Pages)? PrintPages() => PrintPages(JobCards());

    // For a print about to be saved: its pages are laid out from the very cards it will issue.
    private (SheetPlan Plan, List<CardData> Entries, IReadOnlyList<SheetPage> Pages)? PrintPages(IReadOnlyList<JobCard> cards)
    {
        var plan = CurrentPlan();
        if (plan is null) return null;
        var copies = PrintCopies(plan);
        var entries = new List<CardData>();
        foreach (var c in cards) for (var n = 0; n < copies; n++) entries.Add(c.Data);
        return (plan, entries, Sheets.Pages(entries.Count, plan, Flip));
    }

    /// <summary>Look at the sheet before or after the one on screen. Which pages exist is settled by SheetPreview.</summary>
    public void TurnPreviewPage(int by) => PreviewPage += by;

    /// <summary>
    /// The page of the print the stage shows on the last step, and what is
    /// said about it. Null when there is nothing to print. PreviewPage is
    /// brought back inside the pages there are.
    /// </summary>
    public SheetPreview? SheetPreview()
    {
        if (PrintPages() is not { } print || print.Pages.Count == 0) return null;
        PreviewPage = Math.Clamp(PreviewPage, 0, print.Pages.Count - 1);
        var page = print.Pages[PreviewPage];
        var side = print.Plan.Mode != SheetMode.Duplex ? "" : page.Backs ? "Backs, reversed to match. " : "Fronts. ";
        return new SheetPreview(
            print.Plan, print.Entries, page, BackUpsideDown(print.Plan),
            Says: $"Preview of page {PreviewPage + 1} of the {Paper} sheet. {side}".Trim(),
            Caption: $"{side}Page {PreviewPage + 1} of {print.Pages.Count} · {Paper}",
            NavShown: print.Pages.Count > 1,
            PreviousEnabled: PreviewPage > 0,
            NextEnabled: PreviewPage < print.Pages.Count - 1);
    }

    /// <summary>The file this print is saved as, and the title inside it.</summary>
    public (string FileName, string Title) PrintFile()
    {
        var cards = JobCards();
        if (cards.Count > 1)
        {
            var stamp = _now().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return ($"apg-ids-{cards.Count}-{stamp}.pdf", $"Alpha Premier IDs — {cards.Count} cards");
        }
        if (cards.Count == 1)
        {
            var only = cards[0];
            return (Naming.PdfFileName(only.Entry.Year, only.Entry.Seq, only.Entry.Last), $"{only.Id} {only.Label}");
        }
        return ("apg-id.pdf", CardConfig.AppName);
    }

    /// <summary>Just how much paper this will be.</summary>
    public string PrintFileLine() => PrintFileLine(JobCards().Count);

    /// <summary>The same, for a caller that has already counted the cards in this print.</summary>
    public string PrintFileLine(int cards)
    {
        var plan = CurrentPlan();
        if (plan is null) return "2 pages";
        var sheets = (int)Math.Ceiling(cards * PrintCopies(plan) / (double)plan.PerSheet);
        var pages = plan.Mode == SheetMode.Pairs ? sheets : sheets * 2;
        return $"{pages} page{Words.S(pages)} of {Paper}";
    }

    /// <summary>
    /// The print file has been saved: every card in it is now issued.
    /// Records them on this computer at once and returns the entries, for
    /// the caller to send on to the shared register. An example is never recorded.
    /// </summary>
    public IReadOnlyList<RegisterEntry> MarkIssued() => MarkIssued(Issuing());

    /// <summary>What a print of the form as it stands would issue.</summary>
    private PrintIssue Issuing()
    {
        var typed = new List<string>();
        if (Role1.IsOther) typed.Add(Role1.Value);
        if (ShowRole2 && Role2.IsOther) typed.Add(Role2Value);
        return new PrintIssue(Example, JobCards(), typed);
    }

    /// <summary>
    /// Record the cards of a print that has been saved: those cards, whatever
    /// the form holds by now. A queued card that was in the print leaves the
    /// queue; one that was not (there is none today: nothing can be added
    /// while a print is being written) would stay, unissued.
    /// </summary>
    private IReadOnlyList<RegisterEntry> MarkIssued(PrintIssue issue)
    {
        if (issue.Example) return Array.Empty<RegisterEntry>();
        foreach (var title in issue.TypedTitles) _titles.Remember(title);
        var issued = issue.Cards.Select(c => c.Entry).ToList();
        foreach (var e in issued) { _register.Record(e); _register.NoteShared(e); }
        DupLocal = issued.LastOrDefault() ?? DupLocal;
        // by reference: JobCard is a record, and two cards that read the same are still two cards
        Queue.RemoveAll(queued => issue.Cards.Any(card => ReferenceEquals(card, queued)));
        return issued;
    }

    /// <summary>
    /// Write issued cards to the shared register. Says which are still
    /// waiting to get there, and which it refused because the number already
    /// belongs to somebody else: those cards are duplicates and must not be used.
    /// </summary>
    public async Task<SharedReport> RecordShared(IReadOnlyList<RegisterEntry> issued)
    {
        var waiting = new List<RegisterEntry>();
        var refused = new List<RefusedCard>();
        if (!_register.Shared) return new SharedReport(waiting, refused);
        foreach (var e in issued)
        {
            var write = await _register.RecordShared(e).ConfigureAwait(false);
            if (write.Outcome == SharedOutcome.Waiting) waiting.Add(e);
            if (write is { Outcome: SharedOutcome.Refused, Holder: { } holder }) refused.Add(new RefusedCard(e, holder));
        }
        return new SharedReport(waiting, refused);
    }
}

/// <summary>One page of the print as the stage shows it: what to draw, and the words and buttons around it.</summary>
public sealed record SheetPreview(
    SheetPlan Plan, IReadOnlyList<CardData> Entries, SheetPage Page, bool BackUpsideDown,
    string Says, string Caption, bool NavShown, bool PreviousEnabled, bool NextEnabled);

/// <summary>What became of the cards just sent to the shared register.</summary>
public sealed record SharedReport(IReadOnlyList<RegisterEntry> Waiting, IReadOnlyList<RefusedCard> Refused)
{
    /// <summary>What to tell the operator, or "" when every card got there.</summary>
    public string Message()
    {
        var parts = new List<string>();
        foreach (var r in Refused)
        {
            parts.Add($"{r.Card.Id} was issued to {r.Holder.Name} on another computer{(r.Holder.IssuedOn.Length > 0 ? " on " + r.Holder.IssuedOn : "")}, "
                + $"so the card just made for {r.Card.Name} is a duplicate. Do not hand it out: make it again with a number that is free.");
        }
        if (Waiting.Count > 0)
        {
            parts.Add($"{(Waiting.Count == 1 ? Waiting[0].Id + " was" : Waiting.Count + " cards were")} not written to the shared register, "
                + "because it could not be reached. They are recorded on this computer and will be sent the next time the app starts with the register in reach. "
                + "Until then, other computers will not know these numbers are taken.");
        }
        return string.Join(" ", parts);
    }
}
