// The window: it shows what the Session holds and passes on what the
// operator does. It decides nothing about the card or the steps itself.
// Render() redraws everything from the Session each time something changes,
// as the Windows window does, so no two parts of the screen can disagree.
//
// Built from AppKit's own controls, in code. It does what the Windows
// window does (windows/IdMaker.App/MainWindow.xaml.cs), step for step and
// tool for tool; the tools beyond the register and the printer setting are
// in MainWindow.Tools.cs.

using System.Globalization;
using IdMaker.Core;
using IdMaker.Vision;

namespace IdMaker.Mac;

public sealed partial class MainWindow
{
    private readonly FileStore _files;
    private readonly Register _register;
    private readonly Session _session;
    private readonly TemplateStore _templates;
    private readonly DesignDesk _design;

    private MacCardRenderer? _renderer;
    private CardArt? _art;
    private string _loadError = "";
    private OnnxBackgroundRemover? _remover;
    private string _removerProblem = "";

    private bool _front = true;                 // which side the preview shows
    private bool _syncing;                      // true while Render() writes to controls, so their handlers stand down
    private string? _tool;                      // which tool drawer is open, if any
    private bool _saved;                        // the print on screen has been saved
    private bool _saving;
    private int _savedCount;
    private string _savedPath = "";
    private string _sigError = "";
    private string _reviewError = "";
    private string _reviewFileNote = "";
    private string _savedTrouble = "";
    private string _registerMessage = "";
    private string _copyStatus = "";
    private string _titlesBuiltFor = "";
    private string _titles2BuiltFor = "";
    private string _registerShown = "";
    private string _jobShown = "";
    private Signature? _sigShown;
    private PhotoResult? _thumbOf;

    /// <summary>Asks a yes/no question. Replaced in the window test, where nobody is there to answer.</summary>
    internal Func<string, string, bool> Confirm { get; set; }
    /// <summary>Opens the full-screen signing window. Replaced in the window test.</summary>
    internal Func<string, DrawnSignature?, DrawnSignature?> AskForSignature { get; set; } = SignWindow.Ask;
    /// <summary>Arriving at the signature step with nothing signed opens the signing screen by itself.</summary>
    internal bool OpenSigningOnArrival { get; set; } = true;

    internal Session Session => _session;
    internal Register Register => _register;
    public NSWindow Window { get; }

    /* ---- the controls --------------------------------------------- */

    private readonly NSImageView _logo = Ui.Picture(56, 56);
    private readonly NSButton _railRegister;
    private readonly NSButton _railBatch;
    private readonly NSButton _railPrinter;
    private readonly NSButton _railDesign;
    private readonly NSStackView _toolBatch;
    private readonly NSStackView _toolDesign;
    private readonly NSView _cardBox = new() { TranslatesAutoresizingMaskIntoConstraints = false };
    private const double CardPointsWide = 400;

    private readonly NSStackView _drawer;
    private readonly NSTextField _drawerTitle = Ui.Label("", 16, bold: true, maxWidth: 280);
    private readonly NSStackView _toolRegister;
    private readonly NSStackView _toolPrinter;
    private readonly NSTextField _refusedNote = Ui.Error();
    private readonly NSStackView _refusedPanel;
    private readonly NSButton _btnRefusedDone;
    private readonly NSTextField _registerNote = Ui.Note();
    private readonly NSStackView _registerList = Ui.Column(8);
    private readonly NSButton _btnRegisterCsv;
    private readonly NSTextField _registerAccount = Ui.Note();
    private readonly NSStackView _signInForm;
    private readonly NSTextField _txtSignInEmail = Ui.Field("Email", 280);
    private readonly NSSecureTextField _txtSignInPassword = new() { TranslatesAutoresizingMaskIntoConstraints = false, AccessibilityLabel = "Password" };
    private readonly NSButton _btnSignIn;
    private readonly NSStackView _signedInActions;
    private readonly NSButton _btnRegisterTest;
    private readonly NSTextField _registerMsg = Ui.Note();
    private readonly NSPopUpButton _cmbFlip;
    private readonly NSButton _chkRotate;

    private readonly NSSegmentedControl _sideTabs = new() { SegmentCount = 2, TranslatesAutoresizingMaskIntoConstraints = false };
    private readonly NSImageView _cardImage = Ui.Picture(CardPointsWide, 634);
    private readonly NSTextField _cardLoading = Ui.Note("Loading the card…");
    private readonly NSTextField _caption = Ui.Note();
    private readonly NSStackView _sheetNav;
    private readonly NSButton _prevPage;
    private readonly NSButton _nextPage;
    private readonly NSTextField _previewCaption = Ui.Note();
    private readonly NSStackView _queueStrip;
    private readonly NSTextField _jobCount = Ui.Note();
    private readonly NSStackView _jobList = Ui.Column(4);
    private readonly NSTextField _jobNote = Ui.Note();
    private readonly NSButton _btnCancelAdd;

    private readonly List<NSButton> _stepChips = new();
    private readonly NSStackView[] _steps = new NSStackView[5];
    private readonly NSButton _btnBack;
    private readonly NSButton _btnNext;
    private readonly NSTextField _blocked = Ui.Error();
    private readonly NSTextField _status = Ui.Note();

    // step 1
    private readonly NSTextField _txtLast = Ui.Field("Last name", 400);
    private readonly NSTextField _txtFirst = Ui.Field("First name", 400);
    private readonly NSTextField _txtMiddle = Ui.Field("Middle name", 400);
    private readonly NSTextField _txtSuffix = Ui.Field("Suffix (Jr., III)", 160);
    private readonly NSTextField _msgLast = Ui.Error(), _msgFirst = Ui.Error(), _msgMiddle = Ui.Error(), _msgSuffix = Ui.Error();
    private readonly NSTextField _namePreview = Ui.Label("");
    // step 2
    private readonly NSPopUpButton _cmbKind;
    private readonly NSTextField _msgKind = Ui.Error();
    private readonly NSPopUpButton _cmbTitle;
    private readonly NSTextField _txtTitleCustom = Ui.Field("Job title", 400);
    private readonly NSTextField _msgTitle = Ui.Error();
    private readonly NSButton _btnForgetTitle;
    private readonly NSStackView _fieldTitle2;
    private readonly NSPopUpButton _cmbTitle2;
    private readonly NSTextField _txtTitle2Custom = Ui.Field("Second role", 400);
    private readonly NSTextField _msgTitle2 = Ui.Error();
    private readonly NSButton _btnRoleAdd;
    private readonly NSTextField _titlePreview = Ui.Error();
    private List<string> _titleValues = new();
    private List<string> _title2Values = new();
    // step 2, continued: the number
    private readonly NSPopUpButton _cmbYear;
    private readonly List<int> _years = new();
    private readonly NSTextField _txtSeq = Ui.Field("Number", 110);
    private readonly NSTextField _msgSeq = Ui.Note();
    private readonly NSStackView _reissueWrap;
    private readonly NSButton _chkReissue;
    // step 3
    private readonly NSImageView _photoThumb = Ui.Picture(138, 184);
    private readonly NSButton _btnPhoto;
    private readonly NSTextField _photoName = Ui.Note();
    private readonly NSTextField _photoMsg = Ui.Note();
    private readonly NSStackView _adjustBox;
    private readonly NSSlider _sldZoom = new() { MinValue = 50, MaxValue = 150, DoubleValue = 100, TranslatesAutoresizingMaskIntoConstraints = false };
    private readonly NSTextField _zoomOut = Ui.Label("100 %");
    private readonly NSButton _btnAdjustReset;
    private readonly NSTextField _guideLabel = Ui.Label("If the cut-out looks wrong", bold: true);
    private readonly NSTextField _copyStatusLabel = Ui.Note();
    // step 4
    private readonly NSImageView _sigImage = Ui.Picture(400, 130);
    private readonly NSButton _btnSign;
    private readonly NSButton _btnClearSig;
    private readonly NSTextField _sigHelp = Ui.Note();
    private readonly NSTextField _sigMsg = Ui.Error();
    private readonly NSTextField _tabletNote = Ui.Note();
    // step 5
    private readonly NSTextField _checkHeading = Ui.Heading("Is everything correct?");
    private readonly NSTextField _reviewSub = Ui.Note();
    private readonly NSStackView _reviewStage;
    private readonly NSStackView _savedStage;
    private readonly NSStackView _summary;
    private readonly NSImageView _summaryImage = Ui.Picture(88, 140);
    private readonly NSTextField _sumName = Ui.Label("", bold: true, maxWidth: 220), _sumTitle = Ui.Label("", bold: true, maxWidth: 220), _sumNumber = Ui.Label("", bold: true, maxWidth: 220);
    private readonly NSTextField _reviewWarning = Ui.Warning();
    private readonly NSSegmentedControl _layout = new() { SegmentCount = 2, TranslatesAutoresizingMaskIntoConstraints = false };
    private readonly NSPopUpButton _cmbPaper;
    private readonly NSTextField _copiesLabel = Ui.Label("Copies of this card");
    private readonly NSTextField _txtCopies = Ui.Field("Copies", 60);
    private readonly NSButton _copiesDown, _copiesUp;
    private readonly NSTextField _printNote = Ui.Note();
    private readonly NSButton _btnAddAnother;
    private readonly NSTextField _reviewErrorLabel = Ui.Error();
    private readonly NSButton _btnDownload;
    private readonly NSButton _btnPng;
    private readonly NSTextField _reviewFile = Ui.Note();
    private readonly NSTextField _savedLine = Ui.Label("");
    private readonly NSTextField _savedNote = Ui.Warning();
    private readonly NSButton _btnNewId;

    public MainWindow() : this(new FileStore(DefaultDirectory), null) { }

    /// <summary>Where this Mac's user keeps the app's list of issued IDs and settings.</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FileStore.CompanyFolder, FileStore.AppFolder);

    internal MainWindow(FileStore files, Register? register)
    {
        _files = files;
        _register = register ?? new Register(files, keeper: new MacKeeper());
        _templates = new TemplateStore(files);
        _session = new Session(_register, new TitleList(files), files, post: action => NSApplication.SharedApplication.BeginInvokeOnMainThread(action));
        _session.Changed += Render;
        _design = new DesignDesk(_session, _templates, files.Directory);
        Confirm = AskYesNo;

        /* ---- rail ---- */
        _railRegister = Ui.Button("Issued IDs", () => ToggleTool(ToolKey.Register));
        _railBatch = Ui.Button("Batch from a list", () => ToggleTool(ToolKey.Batch));
        _railPrinter = Ui.Button("Printer setting", () => ToggleTool(ToolKey.Printer));
        _railDesign = Ui.Button("Card design", () => ToggleTool(ToolKey.Design));
        var rail = Ui.Column(10,
            _logo,
            Ui.Label("Alpha Premier", 12, muted: true, maxWidth: 150),
            Ui.Label("ID Maker", 16, bold: true, maxWidth: 150),
            Ui.Note("Tools"),
            _railRegister,
            _railBatch,
            _railPrinter,
            _railDesign,
            Ui.Note($"Version {CardConfig.AppVersion}"));
        rail.WidthAnchor.ConstraintEqualTo(160).Active = true;

        /* ---- the tool drawer ---- */
        _btnRefusedDone = Ui.Button("These cards are dealt with", () => { _session.ClearRefused(); Render(); });
        _refusedPanel = Ui.Column(8, _refusedNote, _btnRefusedDone);
        _btnRegisterCsv = Ui.Button("Save the list as CSV", SaveRegisterCsv);
        _txtSignInPassword.WidthAnchor.ConstraintEqualTo(280).Active = true;
        _btnSignIn = Ui.Button("Sign in", () => _ = SignIn());
        _signInForm = Ui.Column(6, Ui.Label("Email", maxWidth: 280), _txtSignInEmail, Ui.Label("Password", maxWidth: 280), _txtSignInPassword, _btnSignIn);
        _btnRegisterTest = Ui.Button("Check the register", () => _ = CheckRegister());
        _signedInActions = Ui.Row(8, _btnRegisterTest, Ui.Button("Sign out", SignOut));
        _toolRegister = Ui.Column(10,
            _refusedPanel, _registerNote, _registerList,
            Ui.Row(8, _btnRegisterCsv, Ui.Button("Bring in a saved list", ImportRegisterCsv)),
            Ui.Label("Shared register", bold: true, maxWidth: 280), _registerAccount, _signInForm, _signedInActions, _registerMsg);
        _cmbFlip = Ui.Popup("How your printer turns the paper over", new[] { "On the long edge (usual)", "On the short edge" }, index =>
        {
            if (_syncing) return;
            _session.SetFlip(index == 1 ? DuplexFlip.ShortEdge : DuplexFlip.LongEdge);
            Render();
        });
        _chkRotate = Ui.Check("Turn the back upside down", on => { _session.SetRotateBack(on); Render(); });
        _toolPrinter = Ui.Column(10,
            Ui.Label("How your printer turns the paper over", bold: true, maxWidth: 280), _cmbFlip,
            Ui.Label("Set this once. It decides how the sheet of backs is reversed, so each back lands behind the right front. If the backs come out behind the wrong people, change it and print again. It only applies when printing both sides.", 12, muted: true, maxWidth: 280),
            _chkRotate,
            Ui.Label("Only if printed cards come out with the back upside down. Remembered on this computer.", 12, muted: true, maxWidth: 280));
        _toolBatch = BuildBatchTool();
        _toolDesign = BuildDesignTool();
        _drawer = Ui.Column(12, Ui.Row(12, _drawerTitle, Ui.Button("Close", CloseTool)), _toolRegister, _toolBatch, _toolPrinter, _toolDesign);
        _drawer.WidthAnchor.ConstraintEqualTo(300).Active = true;
        foreach (var label in new[] { _refusedNote, _registerNote, _registerAccount, _registerMsg }) label.PreferredMaxLayoutWidth = 280;

        /* ---- the stage ---- */
        _sideTabs.SetLabel("Front", 0);
        _sideTabs.SetLabel("Back", 1);
        _sideTabs.Activated += (_, _) => { if (_syncing) return; _front = _sideTabs.SelectedSegment == 0; Render(); };
        _prevPage = Ui.Button("Previous page", () => { _session.TurnPreviewPage(-1); Render(); });
        _nextPage = Ui.Button("Next page", () => { _session.TurnPreviewPage(1); Render(); });
        _sheetNav = Ui.Row(12, _prevPage, _previewCaption, _nextPage);
        _btnCancelAdd = Ui.Button("Stop adding and go back to the print", CancelAdding);
        _queueStrip = Ui.Column(6, Ui.Row(12, Ui.Label("In this print", 14, bold: true, maxWidth: 200), _jobCount), _jobList, _jobNote, _btnCancelAdd);
        // the card, and over it whatever takes the mouse: the photo while a card is made, the design's boxes while the map is open
        foreach (var layer in new NSView[] { _cardImage, _photoDrag, _map })
        {
            _cardBox.AddSubview(layer);
            NSLayoutConstraint.ActivateConstraints(new[]
            {
                layer.LeadingAnchor.ConstraintEqualTo(_cardBox.LeadingAnchor),
                layer.TrailingAnchor.ConstraintEqualTo(_cardBox.TrailingAnchor),
                layer.TopAnchor.ConstraintEqualTo(_cardBox.TopAnchor),
                layer.BottomAnchor.ConstraintEqualTo(_cardBox.BottomAnchor),
            });
        }
        WireCard();
        var stage = Ui.Column(10, _sideTabs, _cardLoading, _cardBox, _caption, _sheetNav, _queueStrip);
        stage.Alignment = NSLayoutAttribute.CenterX;
        stage.WidthAnchor.ConstraintEqualTo(420).Active = true;

        /* ---- the form ---- */
        var chips = Ui.Row(4);
        for (var i = 0; i < Session.Steps.Length; i++)
        {
            var index = i;
            var chip = new NSButton { Title = Session.Steps[i].Label, BezelStyle = NSBezelStyle.Recessed, TranslatesAutoresizingMaskIntoConstraints = false };
            chip.SetButtonType(NSButtonType.MomentaryPushIn);
            chip.Activated += (_, _) => AfterMove(_session.StepIndex, _session.ChipPressed(index));
            chips.AddArrangedSubview(chip);
            _stepChips.Add(chip);
        }

        // step 1: name
        Wire(_txtLast, Field.Last);
        Wire(_txtFirst, Field.First);
        Wire(_txtMiddle, Field.Middle);
        Wire(_txtSuffix, Field.Suffix);
        _steps[0] = Ui.Column(4,
            Ui.Heading("Who is this ID for?"),
            Ui.Label("Last name", bold: true), _txtLast, _msgLast,
            Ui.Label("First name", bold: true), _txtFirst, _msgFirst,
            Ui.Label("Middle name", bold: true), _txtMiddle, _msgMiddle,
            Ui.Label("Suffix (Jr., III)", bold: true), _txtSuffix, _msgSuffix,
            _namePreview);

        // step 2: who it is for, the job title and, on the same page, the employee number
        _cmbKind = Ui.Popup("This ID is for", new[] { "Choose…", "An employee", "An intern" }, index =>
        {
            if (_syncing || index < 1) return;
            _session.PickKind(index == 2 ? PersonKind.Intern : PersonKind.Employee);
            Render();
        });
        _cmbTitle = Ui.Popup("Job title", Array.Empty<string>(), index =>
        {
            if (_syncing || index < 0 || index >= _titleValues.Count) return;
            _session.PickRole1(_titleValues[index]);
            Render();
            if (_titleValues[index] == RoleChoice.Other) Window!.MakeFirstResponder(_txtTitleCustom);       // SAFETY: the window exists before anything in it can be pressed
        });
        _txtTitleCustom.Changed += (_, _) => OnTitleCustomChanged();
        _txtTitleCustom.EditingEnded += (_, _) => { _session.Touch(Field.Title); Render(); };
        _btnForgetTitle = Ui.Button("Remove this title from the list", () => { _session.ForgetTitle(_session.Role1.Pick); Render(); });
        _cmbTitle2 = Ui.Popup("Second role", Array.Empty<string>(), index =>
        {
            if (_syncing || index < 0 || index >= _title2Values.Count) return;
            _session.PickRole2(_title2Values[index]);
            Render();
            if (_title2Values[index] == RoleChoice.Other) Window!.MakeFirstResponder(_txtTitle2Custom);     // SAFETY: as above
        });
        _txtTitle2Custom.Changed += (_, _) => { if (_syncing) return; _session.TypeRole2(_txtTitle2Custom.StringValue); Render(); };
        _txtTitle2Custom.EditingEnded += (_, _) => { _session.Touch(Field.Title2); Render(); };
        _fieldTitle2 = Ui.Column(4,
            Ui.Row(12, Ui.Label("Second role", bold: true, maxWidth: 160), Ui.Button("Remove the second role", () => { _session.RemoveSecondRole(); Render(); })),
            _cmbTitle2, _txtTitle2Custom, _msgTitle2);
        _btnRoleAdd = Ui.Button("Add a second role", () => { _session.AddSecondRole(); Render(); });

        // the number shares the job's step (Session.Steps): on its own it left a page nearly empty
        foreach (var year in _session.YearsOffered()) _years.Add(year);
        _cmbYear = Ui.Popup("Year of issue", _years.Select(y => y.ToString(CultureInfo.InvariantCulture)), index =>
        {
            if (_syncing || index < 0 || index >= _years.Count) return;
            _session.SetYear(_years[index]);
            Render();
        });
        _txtSeq.Changed += (_, _) => OnSeqChanged();
        _txtSeq.EditingEnded += (_, _) => { _session.Touch(Field.Seq); Render(); };
        _chkReissue = Ui.Check("Replacement card", on => { _session.SetReissue(on); Render(); });
        _reissueWrap = Ui.Column(4, _chkReissue,
            Ui.Note("This number is already issued. Tick only if you are printing the same person’s card again (lost, damaged, name change)."));
        var numberPart = Ui.Column(6,
            Ui.Label("Which employee number?", 16, bold: true),
            Ui.Row(8, Ui.Label($"{CardConfig.EmployeePrefix} -", 16, bold: true, maxWidth: 80), _cmbYear, Ui.Label("-", 16, bold: true, maxWidth: 20), _txtSeq),
            _msgSeq, _reissueWrap);
        _steps[1] = Ui.Column(4,
            Ui.Heading("What do they do?"),
            Ui.Label("This ID is for", bold: true), _cmbKind, _msgKind,
            Ui.Label("Job title", bold: true), _cmbTitle, _txtTitleCustom, _msgTitle, _btnForgetTitle,
            _fieldTitle2, _btnRoleAdd, _titlePreview,
            numberPart);

        // step 3: photo
        _btnPhoto = Ui.Button("Choose a photo", ChoosePhoto);
        _sldZoom.WidthAnchor.ConstraintEqualTo(220).Active = true;
        _sldZoom.Activated += (_, _) =>
        {
            if (_syncing) return;
            _session.SetZoom(Math.Round(_sldZoom.DoubleValue / 5) * 5 / 100);
            _front = true;
            Render();
        };
        _btnAdjustReset = Ui.Button("Put it back where the app placed it", () => { _session.ResetAdjust(); Render(); });
        NSButton Move(string title, string says, int dx, int dy)
        {
            var button = Ui.Button(title, () =>
            {
                var step = CardConfig.Photo.NudgeStepMm;
                _session.Nudge(dx * step, dy * step);
                _front = true;
                Render();
            });
            Ui.Says(button, says);
            return button;
        }
        _adjustBox = Ui.Column(6,
            Ui.Label("Move or resize it", bold: true),
            Ui.Row(6, Move("Left", "Move the photo left", -1, 0), Move("Up", "Move the photo up", 0, -1), Move("Down", "Move the photo down", 0, 1), Move("Right", "Move the photo right", 1, 0)),
            Ui.Row(10, Ui.Label("Size", maxWidth: 40), _sldZoom, _zoomOut),
            _btnAdjustReset);
        _steps[2] = Ui.Column(8,
            Ui.Heading("Their photo"),
            new FileDropView(Ui.Row(14, _photoThumb, Ui.Column(6, _btnPhoto, Ui.Note("or drop a picture here"), _photoName))) { Dropped = path => _ = AcceptPhotoFile(path) },
            _photoMsg, _adjustBox,
            _guideLabel,
            Ui.Note("The app removes the background itself, on this computer. If it leaves bits of background or cuts into the person, a photo against a plain wall usually fixes it. Failing that, an AI tool can make a green-background copy: open Gemini or ChatGPT signed in to the company’s enterprise account, attach the photo, paste the prompt and send it, then save the result and choose it here."),
            Ui.Row(10, Ui.Button("Copy the prompt", CopyPrompt), _copyStatusLabel),
            Ui.Note("Employee photos are personal data. Use the company’s approved enterprise AI account, never a personal login, and check the face still looks like the person."));

        // step 4: signature
        _btnSign = Ui.Button("Sign now", SignNow);
        _btnClearSig = Ui.Button("Clear", () => { _sigError = ""; _session.SetSignature(null); Render(); });
        _steps[3] = Ui.Column(8,
            Ui.Heading("Their signature"),
            _sigImage,
            Ui.Row(8, _btnSign, Ui.Button("Use a scan instead", ChooseSignatureScan), _btnClearSig),
            _sigHelp, _sigMsg, _tabletNote, _btnDriver, _driverMsg);
        _btnDriver.Activated += (_, _) => _ = RunDriverInstall();
        _driverMsg.PreferredMaxLayoutWidth = (nfloat)Ui.TextWidth;

        // step 5: check and print
        _summary = Ui.Row(14, _summaryImage, Ui.Column(2,
            Ui.Note("Name"), Ui.Row(8, _sumName, Ui.Button("Edit", () => EditStep(0))),
            Ui.Note("Job title"), Ui.Row(8, _sumTitle, Ui.Button("Edit", () => EditStep(1))),
            // the number is on the job's page: its Edit goes straight to the number box
            Ui.Note("Employee number"), Ui.Row(8, _sumNumber, Ui.Button("Edit", () => { EditStep((int)Step.Title); Window!.MakeFirstResponder(_txtSeq); }))));   // SAFETY: the window exists before anything in it can be pressed
        _layout.SetLabel("Side by side", 0);
        _layout.SetLabel("Both sides of the paper", 1);
        _layout.Activated += (_, _) =>
        {
            if (_syncing) return;
            _session.SetLayout(_layout.SelectedSegment == 1 ? SheetMode.Duplex : SheetMode.Pairs);
            Render();
        };
        _cmbPaper = Ui.Popup("Paper", CardConfig.Papers.Select(p => p.Label), index =>
        {
            if (_syncing || index < 0 || index >= CardConfig.Papers.Length) return;
            _session.SetPaper(CardConfig.Papers[index].Key);
            Render();
        });
        _txtCopies.Changed += (_, _) => OnCopiesChanged();
        _copiesDown = Ui.Button("−", () => { _session.NudgeCopies(-1); Render(); });
        Ui.Says(_copiesDown, "One fewer copy");
        _copiesUp = Ui.Button("+", () => { _session.NudgeCopies(1); Render(); });
        Ui.Says(_copiesUp, "One more copy");
        _btnAddAnother = Ui.Button("Add another ID to this print", () => { var from = _session.StepIndex; _session.AddAnother(); AfterMove(from); });
        _btnDownload = Ui.Button("Save the PDF", ChooseWhereToSave, primary: true);
        _btnPng = Ui.Button("Save as pictures instead", SavePictures);
        _reviewStage = Ui.Column(8,
            _summary, _reviewWarning,
            Ui.Label("Printing", 15, bold: true), _layout,
            Ui.Row(10, Ui.Label("Paper", bold: true, maxWidth: 60), _cmbPaper),
            Ui.Row(6, _copiesLabel, _copiesDown, _txtCopies, _copiesUp),
            _printNote, _btnAddAnother, _reviewErrorLabel,
            Ui.Row(10, _btnDownload, _btnPng), _reviewFile);
        _btnNewId = Ui.Button("Start a new ID", StartNewId, primary: true);
        _savedStage = Ui.Column(8, _savedLine, _savedNote, Ui.Row(10, _btnNewId, Ui.Button("Show the file", () => ShowInFinder(_savedPath))));
        _steps[4] = Ui.Column(8, _checkHeading, _reviewSub, _reviewStage, _savedStage);

        _btnBack = Ui.Button("Back", () => { var from = _session.StepIndex; _session.Back(); AfterMove(from); });
        _btnNext = Ui.Button("Next", () => AfterMove(_session.StepIndex, _session.NextStep()), primary: true);
        var form = Ui.Column(12, chips);
        foreach (var step in _steps) form.AddArrangedSubview(step);
        form.AddArrangedSubview(Ui.Row(8, _btnBack, _btnNext));
        form.AddArrangedSubview(_blocked);
        form.AddArrangedSubview(_status);
        form.AddArrangedSubview(Ui.Spacer());
        form.WidthAnchor.ConstraintEqualTo(440).Active = true;

        /* ---- the window ---- */
        var columns = Ui.Row(20, rail, _drawer, stage, form);
        columns.Alignment = NSLayoutAttribute.Top;
        Window = new NSWindow(
            new CGRect(0, 0, 1120, 860),
            NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Miniaturizable | NSWindowStyle.Resizable,
            NSBackingStore.Buffered,
            false)
        {
            Title = CardConfig.AppName,
        };
        var content = Window.ContentView!;
        content.AddSubview(columns);
        NSLayoutConstraint.ActivateConstraints(new[]
        {
            columns.LeadingAnchor.ConstraintEqualTo(content.LeadingAnchor, 20),
            columns.TopAnchor.ConstraintEqualTo(content.TopAnchor, 20),
            columns.TrailingAnchor.ConstraintLessThanOrEqualTo(content.TrailingAnchor, -20),
            columns.BottomAnchor.ConstraintLessThanOrEqualTo(content.BottomAnchor, -20),
        });
        Window.Center();

        Boot();
    }

    private void Wire(NSTextField box, Field part)
    {
        box.Changed += (_, _) => OnNameChanged(part);
        box.EditingEnded += (_, _) => { _session.LeaveNamePart(part); Render(); };
    }

    /* ---- starting up ---------------------------------------------- */

    private static string InBundle(params string[] parts) =>
        Path.Combine(new[] { NSBundle.MainBundle.ResourcePath ?? AppContext.BaseDirectory }.Concat(parts).ToArray());

    private void Boot()
    {
        try
        {
            var measurer = MacTextMeasurer.Load(InBundle("Fonts"));
            _renderer = new MacCardRenderer(measurer);
            var front = MacPictures.Load(InBundle("Assets", CardConfig.FrontArtFile));
            var back = MacPictures.Load(InBundle("Assets", CardConfig.BackArtFile));
            if (front is null || back is null) throw new IOException("the card artwork is missing.");
            _builtInArt = new CardArt(front, back);
            _art = _builtInArt;
            using var logo = MacPictures.Load(InBundle("Assets", CardConfig.LogoFile));
            if (logo is not null) _logo.Image = new NSImage(logo, new CGSize(56, 56));

            // a design put in earlier is still the one in use
            if (_templates.Restore() && _templates.HasArtwork)
            {
                var storedFront = MacPictures.Load(StoredArtwork("front"));
                var storedBack = MacPictures.Load(StoredArtwork("back"));
                if (storedFront is not null && storedBack is not null) _art = new CardArt(storedFront, storedBack);
                else
                {
                    // the pictures are gone: the record of them is no use without them
                    _templates.Clear();
                }
            }
            ApplyGeometry();
            ShowContrastWarnings();
            // Only now: whether a name fits the card cannot be answered without the fonts.
            _session.Measurer = measurer;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _loadError = $"The card could not be loaded: {e.Message} Install the app again to put its files back.";
        }

        // start reading the register now rather than on the first keystroke
        if (_session.HasSharedRegister) _ = WarmRegister();
        // the model takes a moment to load; the form does not wait for it
        _ = LoadRemover();
        Render();
        Window.MakeFirstResponder(_txtLast);
        // after the window is up, so the question has something to sit over
        NSApplication.SharedApplication.BeginInvokeOnMainThread(OfferDriver);
    }

    private async Task WarmRegister()
    {
        // Cards saved while the register was out of reach are sent now that it answers. One of them may
        // have been refused: somebody else took its number meanwhile. That cannot wait to be found.
        if (await _session.WarmRegister()) _tool = ToolKey.Register;
        Render();
    }

    private async Task LoadRemover()
    {
        var (remover, problem) = await Task.Run(() =>
        {
            // the model sits with the app's other resources, not beside the program as on Windows
            var made = OnnxBackgroundRemover.TryCreate(out var why, InBundle(OnnxBackgroundRemover.ModelFolder, OnnxBackgroundRemover.ModelFile));
            return (made, why);
        });
        _remover = remover;
        _removerProblem = problem;
        Render();
    }

    internal bool Loaded => _art is not null && _renderer is not null;
    internal string LoadError => _loadError;
    internal bool RemoverReady => _remover is not null;
    internal string RemoverProblem => _removerProblem;
    internal OnnxBackgroundRemover? Remover => _remover;

    /* ---- drawing the screen --------------------------------------- */

    // An error under a field: the words, and the same words where VoiceOver looks for them.
    private static void FieldMessage(NSTextField message, NSView input, string error)
    {
        Ui.SetText(message, error);
        Ui.Show(message, error.Length > 0);
        input.AccessibilityHelp = error;
    }

    /// <summary>What only the window knows. Core decides the rest of the screen from it (Screen.From).</summary>
    private ScreenInput ScreenState() => new(HostPlatform.Mac)
    {
        Front = _front,
        Loaded = _art is not null && _renderer is not null,
        LoadError = _loadError,
        Saved = _saved,
        SavedCount = _savedCount,
        Saving = _saving,
        CheckingPhoto = _session.CheckingPhoto,
        RemoverReady = _remover is not null,
        RemoverProblem = _removerProblem,
        SignatureError = _sigError,
        TabletDriver = PenDriver.Installed,
        EditingMap = EditingMap,
        Tool = _tool,
    };

    private static NSColor ToneColor(Tone tone) => tone switch { Tone.Error => NSColor.SystemRed, Tone.Warning => NSColor.SystemOrange, _ => NSColor.SecondaryLabel };

    // Core says what the screen shows (Screen.From); this copies it onto the controls.
    internal void Render()
    {
        _syncing = true;
        try
        {
            var s = _session;
            var m = Screen.From(s, ScreenState());

            // the stage shows the card while it is being made, the sheet at the end: here, in the one picture
            Ui.Show(_sideTabs, m.Stage.SideTabsShown);
            _sideTabs.SelectedSegment = _front ? 0 : 1;
            Ui.Show(_cardLoading, m.Stage.LoadingShown);
            Ui.SetText(_cardLoading, m.Stage.LoadingText);
            Ui.Show(_cardBox, m.Stage.CardShown || m.Stage.SheetShown);
            if (s.StepIndex != Session.LastStep) Ui.Show(_sheetNav, false);
            Ui.SetText(_caption, m.Stage.Caption);
            Ui.Show(_caption, m.Stage.Caption.Length > 0);

            // name
            FieldMessage(_msgLast, _txtLast, m.Name.LastError);
            FieldMessage(_msgFirst, _txtFirst, m.Name.FirstError);
            FieldMessage(_msgMiddle, _txtMiddle, m.Name.MiddleError);
            FieldMessage(_msgSuffix, _txtSuffix, m.Name.SuffixError);
            Ui.SetText(_txtLast, s.Last);
            Ui.SetText(_txtFirst, s.First);
            Ui.SetText(_txtMiddle, s.Middle);
            Ui.SetText(_txtSuffix, s.Suffix);
            _namePreview.TextColor = m.Name.PreviewIsError ? NSColor.SystemRed : NSColor.Label;
            Ui.SetText(_namePreview, m.Name.Preview);

            // who the ID is for
            _cmbKind.SelectItem(s.Kind == PersonKind.Intern ? 2 : s.Kind == PersonKind.Employee ? 1 : 0);
            FieldMessage(_msgKind, _cmbKind, m.Title.KindError);

            // job title
            BuildTitles();
            _cmbTitle.SelectItem(Math.Max(0, _titleValues.IndexOf(s.Role1.Pick)));
            Ui.Show(_txtTitleCustom, m.Title.CustomShown);
            Ui.SetText(_txtTitleCustom, s.Role1.Custom);
            FieldMessage(_msgTitle, _txtTitleCustom, m.Title.TitleError);
            // a title this computer remembered can be dropped again
            Ui.Show(_btnForgetTitle, m.Title.ForgetShown);
            Ui.Show(_fieldTitle2, m.Title.SecondShown);
            Ui.Show(_btnRoleAdd, m.Title.AddSecondShown);
            _cmbTitle2.SelectItem(Math.Max(0, _title2Values.IndexOf(s.Role2.Pick)));
            Ui.Show(_txtTitle2Custom, m.Title.SecondCustomShown);
            Ui.SetText(_txtTitle2Custom, s.Role2.Custom);
            FieldMessage(_msgTitle2, _txtTitle2Custom, m.Title.SecondError);
            Ui.Show(_titlePreview, m.Title.FitError.Length > 0);
            Ui.SetText(_titlePreview, m.Title.FitError);

            // number: the error while there is one, otherwise the standing note about what was checked
            _msgSeq.TextColor = m.Number.LineIsError ? NSColor.SystemRed : NSColor.SecondaryLabel;
            Ui.SetText(_msgSeq, m.Number.Line);
            _txtSeq.AccessibilityHelp = m.Number.Error;
            Ui.SetText(_txtSeq, s.Seq);
            _cmbYear.SelectItem(Math.Max(0, _years.IndexOf(s.Year)));
            Ui.Show(_reissueWrap, m.Number.ReissueShown);
            _chkReissue.State = s.Reissue ? NSCellStateValue.On : NSCellStateValue.Off;

            RenderPhotoStep(m.Photo);
            RenderSignStep(m.Sign);
            if (m.Check is { } check) RenderCheckStep(check);

            // one step at a time
            for (var i = 0; i < _steps.Length; i++) Ui.Show(_steps[i], i == m.StepIndex);
            RenderStepChips(m.Chips);

            // the line under the buttons: a refusal while there is one, otherwise the quiet standing hint
            Ui.Show(_blocked, m.Footer.BlockedShown);
            Ui.SetText(_blocked, m.Footer.BlockedText);
            Ui.Show(_status, m.Footer.StatusShown);
            Ui.SetText(_status, m.Footer.StatusText);

            Ui.Show(_btnBack, m.Footer.BackShown);
            Ui.Show(_btnNext, m.Footer.NextShown);
            _btnNext.Title = m.Footer.NextTitle;
            _btnNext.Enabled = m.Footer.NextEnabled;

            RenderJob(m.Job);
            RenderTools(m);
            DrawPreview();
            if (m.Stage.CardShown) Ui.Says(_cardImage, m.Stage.CardSays);
        }
        finally { _syncing = false; }
    }

    private void RenderStepChips(IReadOnlyList<StepChip> states)
    {
        for (var i = 0; i < _stepChips.Count; i++)
        {
            var state = states[i];
            var chip = _stepChips[i];
            if ((state.Current ? NSFont.BoldSystemFontOfSize(13) : NSFont.SystemFontOfSize(13)) is { } font) chip.Font = font;
            chip.ContentTintColor = state.Current ? NSColor.ControlAccent : state.Done ? NSColor.Label : NSColor.SecondaryLabel;
            Ui.Says(chip, state.Says);
            chip.ToolTip = state.ToolTip;
        }
    }

    // The lists are rebuilt only when the titles change, not on every keystroke.
    private void BuildTitles()
    {
        var titles = _session.TitleOptions();
        var key = string.Join("\n", titles);
        if (key != _titlesBuiltFor)
        {
            _titlesBuiltFor = key;
            _titleValues = new List<string> { "" };
            _titleValues.AddRange(titles);
            _titleValues.Add(RoleChoice.Other);
            Fill(_cmbTitle, titles);
        }
        // whatever the main job title is disappears from the second list
        var hide = _session.Role1.Value;
        var second = titles.Where(t => !Session.SecondRoleHides(t, hide)).ToList();
        var key2 = string.Join("\n", second);
        if (key2 != _titles2BuiltFor)
        {
            _titles2BuiltFor = key2;
            _title2Values = new List<string> { "" };
            _title2Values.AddRange(second);
            _title2Values.Add(RoleChoice.Other);
            Fill(_cmbTitle2, second);
        }

        static void Fill(NSPopUpButton popup, IEnumerable<string> titles)
        {
            popup.RemoveAllItems();
            popup.AddItem("Choose…");
            foreach (var title in titles) popup.AddItem(title);
            popup.AddItem("Other…");
        }
    }

    /* ---- moving between steps ------------------------------------- */

    // Core names where the cursor goes (Screen.AfterMove); this is which control that is here.
    private NSView ControlFor(FocusTarget target) => target switch
    {
        FocusTarget.Last => _txtLast,
        FocusTarget.First => _txtFirst,
        FocusTarget.Middle => _txtMiddle,
        FocusTarget.Suffix => _txtSuffix,
        FocusTarget.Kind => _cmbKind,
        FocusTarget.TitleChoice or FocusTarget.TitlePicked => _cmbTitle,
        FocusTarget.TitleCustom => _txtTitleCustom,
        FocusTarget.Title2Choice => _cmbTitle2,
        FocusTarget.Title2Custom => _txtTitle2Custom,
        FocusTarget.Seq => _txtSeq,
        FocusTarget.PhotoButton => _btnPhoto,
        FocusTarget.SignButton => _btnSign,
        FocusTarget.NewIdButton => _btnNewId,
        _ => _btnDownload,
    };

    /// <param name="result">What Next or a step chip answered; nothing for a move that cannot be refused.</param>
    private void AfterMove(int from, MoveResult? result = null)
    {
        var after = Screen.AfterMove(_session, from, result, _saved, OpenSigningOnArrival);
        if (after.ShowFront) _front = true;
        _saved = after.Saved;
        Render();
        if (after.Focus is { } focus) Window.MakeFirstResponder(ControlFor(focus));
        if (after.OpenSigning) NSApplication.SharedApplication.BeginInvokeOnMainThread(SignNow);
    }

    private void EditStep(int step)
    {
        var from = _session.StepIndex;
        _session.GoStep(step);
        AfterMove(from);
    }

    /* ---- what the operator types ----------------------------------- */

    internal void OnNameChanged(Field part)
    {
        if (_syncing) return;
        var box = part switch { Field.Last => _txtLast, Field.First => _txtFirst, Field.Middle => _txtMiddle, _ => _txtSuffix };
        _session.SetNamePart(part, box.StringValue);
        Render();
    }

    internal void OnTitleCustomChanged()
    {
        if (_syncing) return;
        _session.TypeRole1(_txtTitleCustom.StringValue);
        Render();
    }

    internal void OnSeqChanged()
    {
        if (_syncing) return;
        _session.SetSeq(_txtSeq.StringValue);
        Render();
    }

    internal void OnCopiesChanged()
    {
        if (_syncing) return;
        _session.SetCopies(int.TryParse(_txtCopies.StringValue, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null);
        Render();
    }

    /* ---- step 4: photo -------------------------------------------- */

    private void RenderPhotoStep(PhotoView v)
    {
        var photo = _session.Photo;
        _photoMsg.TextColor = ToneColor(v.Tone);
        Ui.SetText(_photoMsg, v.Message);
        Ui.Show(_photoMsg, v.Message.Length > 0);
        _btnPhoto.Title = v.ButtonTitle;
        _btnPhoto.Enabled = v.ButtonEnabled;
        Ui.SetText(_photoName, v.FileName);
        Ui.Show(_photoName, v.HasPhoto);
        if (!ReferenceEquals(_thumbOf, photo))
        {
            _thumbOf = photo;
            if (photo is null) _photoThumb.Image = null;
            else
            {
                var k = Math.Min(1.0, 368.0 / photo.Image.Height);
                var small = photo.Image.Shrink(Math.Max(1, (int)(photo.Image.Width * k)), Math.Max(1, (int)(photo.Image.Height * k)));
                _photoThumb.Image = Ui.ToNsImage(small, 138);
            }
        }
        Ui.Show(_adjustBox, v.HasPhoto);
        _btnAdjustReset.Enabled = v.ResetEnabled;
        if (Math.Abs(_sldZoom.DoubleValue - v.ZoomPercent) > 0.01) _sldZoom.DoubleValue = v.ZoomPercent;
        Ui.SetText(_zoomOut, v.ZoomText);
        // When the photo was turned away for something the prompt fixes, say so above it.
        Ui.SetText(_guideLabel, v.GuideLabel);
        Ui.SetText(_copyStatusLabel, _copyStatus);
    }

    private void ChoosePhoto()
    {
        var path = AskForFile("Choose the photo");
        if (path is not null) _ = AcceptPhotoFile(path);
    }

    /// <summary>Open a picture file and put it through the photo pipeline.</summary>
    internal async Task AcceptPhotoFile(string path)
    {
        // Core looks at the photo (Session.TakePhoto), and redraws through Session.Changed while it does.
        // It answers false when the card the photo was for was put aside meanwhile: nothing landed.
        if (await _session.TakePhoto(path, MacPictures.Decode, _remover)) _front = true;
        Render();
    }

    private void CopyPrompt()
    {
        var board = NSPasteboard.GeneralPasteboard;
        board.ClearContents();
        _copyStatus = board.SetStringForType(CardConfig.PhotoPrompt, "public.utf8-plain-text")      /* plain text, as the pasteboard names it */ ? "Copied." : "The prompt could not be copied.";
        Render();
    }

    /* ---- step 5: signature ---------------------------------------- */

    private void RenderSignStep(SignView v)
    {
        var sig = _session.Signature;
        if (!ReferenceEquals(_sigShown, sig))
        {
            _sigShown = sig;
            _sigImage.Image = SignaturePicture(sig);
        }
        _btnSign.Title = v.ButtonTitle;
        _btnClearSig.Enabled = v.HasSignature;
        Ui.SetText(_sigHelp, v.Help);
        Ui.Show(_sigMsg, v.MessageShown);
        Ui.SetText(_sigMsg, v.Message);
        RenderDriver(v.TabletNote);
    }

    // The signature as dark ink on white, the way the pad shows it.
    private static NSImage SignaturePicture(Signature? signature)
    {
        const int w = 800, h = 260;
        var (context, pixels) = MacPictures.Surface(w, h);
        using (context)
        {
            context.SetFillColor(new CGColor(1, 1, 1, 1));
            context.FillRect(new CGRect(0, 0, w, h));
            var ink = new CGColor(0.11f, 0.11f, 0.10f, 1);
            if (signature is DrawnSignature drawn && drawn.HasInk)
            {
                var (minX, minY, inkW, inkH) = SignatureInk.Bounds(drawn);
                var scale = Math.Min((w - 60) / Math.Max(1, inkW), (h - 60) / Math.Max(1, inkH));
                var segments = SignatureInk.Segments(drawn, 2.5, 7.8);
                MacCardRenderer.DrawInk(context, segments, ink, (w - inkW * scale) / 2 - minX * scale, (h - inkH * scale) / 2 - minY * scale, scale, scale);
            }
            else if (signature is ScannedSignature scan)
            {
                using var picture = MacCardRenderer.TintedScan(scan, CardConfig.ScreenInk);
                var scale = Math.Min((w - 40.0) / scan.Width, (h - 40.0) / scan.Height);
                MacPictures.Draw(context, picture, new CGRect((w - scan.Width * scale) / 2, (h - scan.Height * scale) / 2, scan.Width * scale, scan.Height * scale));
            }
        }
        return Ui.ToNsImage(MacPictures.ToRgba(pixels, w, h), 400);
    }

    private void SignNow()
    {
        if (_session.StepIndex != (int)Step.Sign) return;
        var result = AskForSignature(_session.First.Trim(), _session.Signature as DrawnSignature);
        if (result is null) { Render(); return; }               // cancelled: whatever was there stays
        _sigError = "";
        _session.SetSignature(result);
        _front = true;
        Render();
        // signed: the next thing to do is move on
        Window.MakeFirstResponder(_btnNext);
    }

    private void ChooseSignatureScan()
    {
        var path = AskForFile("Choose the scanned signature");
        if (path is not null) _ = AcceptSignatureFile(path);
    }

    internal async Task AcceptSignatureFile(string path)
    {
        var scan = await _session.TakeSignatureScan(path, MacPictures.Decode);
        if (scan.Landed)
        {
            // a scan that cannot be read leaves whatever signature was there
            _sigError = scan.Error;
            if (scan.Error.Length == 0) _front = true;
        }
        Render();
    }

    /* ---- step 6: check and print ---------------------------------- */

    private void RenderCheckStep(CheckView v)
    {
        var s = _session;
        if (s.StepIndex != Session.LastStep) return;

        Ui.Show(_reviewStage, !v.Saved);
        Ui.Show(_savedStage, v.Saved);
        Ui.SetText(_checkHeading, v.Heading);
        Ui.Show(_reviewSub, v.SubShown);
        Ui.SetText(_reviewSub, v.Sub);

        // a plain summary of the card on screen, each line one press from being edited
        Ui.Show(_summary, v.SummaryShown);
        if (v.SummaryShown && _art is not null && _renderer is not null)
        {
            _summaryImage.Image = Ui.ToNsImage(_renderer.RenderFront(_art, s.Geometry, s.CardData(), 176 / CardConfig.CardWidthMm), 88);
            Ui.SetText(_sumName, v.SummaryName);
            Ui.SetText(_sumTitle, v.SummaryTitle);
            Ui.SetText(_sumNumber, v.SummaryNumber);
        }
        Ui.Show(_reviewWarning, v.WarningShown);
        Ui.SetText(_reviewWarning, v.Warning);

        _layout.SelectedSegment = s.Layout == SheetMode.Duplex ? 1 : 0;
        _cmbPaper.SelectItem(Math.Max(0, Array.FindIndex(CardConfig.Papers, paper => paper.Key == s.Paper)));
        if (v.HasPlan)
        {
            if (!ReferenceEquals(Window.FirstResponder, _txtCopies.CurrentEditor)) Ui.SetText(_txtCopies, v.CopiesText);
            _copiesDown.Enabled = v.CopiesDownEnabled;
            _copiesUp.Enabled = v.CopiesUpEnabled;
            Ui.SetText(_copiesLabel, v.CopiesLabel);
            Ui.SetText(_printNote, v.PrintNote);
        }
        // "saved as pictures" stays under the buttons until a new ID is started
        Ui.SetText(_reviewFile, _reviewFileNote.Length > 0 ? _reviewFileNote : v.FileLine);
        Ui.Show(_reviewErrorLabel, _reviewError.Length > 0);
        Ui.SetText(_reviewErrorLabel, _reviewError);
        _btnDownload.Enabled = v.SaveEnabled;
        _btnDownload.Title = v.SaveTitle;
        _btnPng.Enabled = v.PicturesEnabled;
        _btnAddAnother.Enabled = v.AddAnotherEnabled;

        Ui.SetText(_savedLine, $"Saved {Path.GetFileName(_savedPath)}. Print it at 100% (not “fit to page”).");
        Ui.Show(_savedNote, _savedTrouble.Length > 0);
        Ui.SetText(_savedNote, _savedTrouble);
    }

    private void CancelAdding()
    {
        if (_session.StartedCard && !Confirm("Discard the ID you have started? The cards already in this print are kept.", "Discard")) return;
        var from = _session.StepIndex;
        _session.CancelAdding();
        AfterMove(from);
    }

    private void RenderJob(JobView v)
    {
        Ui.Show(_queueStrip, v.Shown);
        if (!v.Shown) return;
        if (v.Key != _jobShown)
        {
            _jobShown = v.Key;
            foreach (var old in _jobList.ArrangedSubviews) { _jobList.RemoveArrangedSubview(old); old.RemoveFromSuperview(); }
            for (var i = 0; i < v.Rows.Count; i++)
            {
                var card = v.Rows[i];
                var who = Ui.Label(card.Text, maxWidth: 260);
                if (card.Current)
                {
                    _jobList.AddArrangedSubview(Ui.Row(10, who, Ui.Note("on screen")));
                    continue;
                }
                var index = i;
                var remove = Ui.Button("Remove", () => { var from = _session.StepIndex; _session.RemoveFromQueue(index); AfterMove(from); });
                Ui.Says(remove, card.RemoveSays);
                _jobList.AddArrangedSubview(Ui.Row(10, who, remove));
            }
        }
        Ui.SetText(_jobCount, v.Count);
        Ui.SetText(_jobNote, v.Note);
        Ui.Show(_jobNote, v.NoteShown);
        Ui.Show(_btnCancelAdd, v.CancelAddShown);
    }

    private static string DocumentsFolder => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    private void ChooseWhereToSave()
    {
        var (fileName, _) = _session.PrintFile();
        var path = AskWhereToSave("Save the print file", fileName);
        if (path is not null) _ = SavePrint(path);
    }

    /// <summary>
    /// Write the PDF, and only once it is safely on disk mark its cards as
    /// issued. A print that failed to save has issued nothing.
    /// </summary>
    internal async Task SavePrint(string path)
    {
        if (_saving || _art is null || _renderer is null) return;
        if (_session.Print() is not { } job) return;
        var pages = _renderer.SheetPdfPages(_art, _session.Geometry, job.Plan, job.Entries, job.Pages, job.BackUpsideDown);
        _saving = true;
        _reviewError = "";
        Render();
        // Core writes the PDF, and only once it is whole issues its cards (Session.SavePrint)
        var saved = await _session.SavePrint(path, job, OnWindow(pages));
        _saving = false;
        if (saved.Error.Length > 0)
        {
            _reviewError = saved.Error;
            Render();
            return;
        }
        _saved = true;
        _savedCount = saved.Cards;
        _savedPath = path;
        _savedTrouble = "";
        Render();
        Window.MakeFirstResponder(_btnNewId);
        // the shared register has been told meanwhile
        if (await saved.Trouble is { Length: > 0 } trouble)
        {
            _savedTrouble = trouble;
            Render();
        }
    }

    /// <summary>
    /// Pages are drawn on the window's own thread, one at a time as the
    /// writer asks for them (the measurer is not shared between threads);
    /// the compressing and writing happen off it (PrintFiles.Write).
    /// </summary>
    private static List<Func<PdfPage>> OnWindow(List<Func<PdfPage>> pages) => pages.Select(page => (Func<PdfPage>)(() =>
    {
        PdfPage? drawn = null;
        NSApplication.SharedApplication.InvokeOnMainThread(() => drawn = page());
        return drawn ?? throw new InvalidOperationException("A page of the print could not be drawn.");
    })).ToList();

    private void SavePictures()
    {
        if (_art is null || _renderer is null || !_session.Ready()) return;
        var stem = Path.GetFileNameWithoutExtension(_session.PrintFile().FileName);
        var front = AskWhereToSave("Save the front as a picture", stem + "-front.png");
        if (front is null) return;
        var back = PrintFiles.BackPictureFor(front, DocumentsFolder);
        try
        {
            var ppm = CardConfig.PrintPxPerMm;
            MacPictures.SavePng(_renderer.RenderFront(_art, _session.Geometry, _session.CardData(), ppm), front);
            MacPictures.SavePng(_renderer.RenderBack(_art, ppm, _session.RotateBack), back);
            // pictures are a proof to look at, not a print: the card is issued when the PDF is saved
            _reviewFileNote = PrintFiles.PicturesSaved(front, back);
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException)
        {
            _reviewError = PrintFiles.PicturesNotSaved(x);
        }
        Render();
    }

    private void StartNewId()
    {
        var from = _session.StepIndex;
        _saved = false;
        _reviewFileNote = "";
        _reviewError = "";
        _session.NewId();
        AfterMove(from);
    }

    // Saving a file silently would leave the operator with no idea where the print went.
    private static void ShowInFinder(string path)
    {
        if (File.Exists(path)) NSWorkspace.SharedWorkspace.ActivateFileViewer(new[] { NSUrl.FromFilename(path) });
    }

    /* ---- the preview ---------------------------------------------- */

    private const double PreviewPpm = 2 * CardPointsWide / CardConfig.CardWidthMm;         // twice the points it is shown at, for a Retina screen
    private const double SheetPreviewPpm = 96 / 25.4;

    private void DrawPreview()
    {
        if (_art is null || _renderer is null) return;
        var s = _session;
        if (s.StepIndex != Session.LastStep)
        {
            var side = _front
                ? _renderer.RenderFront(_art, s.Geometry, s.CardData(), PreviewPpm)
                : _renderer.RenderBack(_art, PreviewPpm, s.RotateBack);
            _cardImage.Image = Ui.ToNsImage(side, CardPointsWide);
            if (EditingMap) _map.Refresh();
            return;
        }
        // which page there is to show, and what is said about it, is Core's (Session.SheetPreview)
        if (s.SheetPreview() is not { } sheet) { _cardImage.Image = null; Ui.Show(_sheetNav, false); return; }
        var art = _art;
        var renderer = _renderer;
        // every card on a sheet has the same back; draw it once
        RgbaImage? back = null;
        var picture = renderer.RenderSheet(sheet.Plan, sheet.Page, SheetPreviewPpm,
            i => renderer.RenderFront(art, s.Geometry, sheet.Entries[i], SheetPreviewPpm),
            _ => back ??= renderer.RenderBack(art, SheetPreviewPpm, sheet.BackUpsideDown));
        PaperEdge(picture);
        _cardImage.Image = Ui.ToNsImage(picture, CardPointsWide);
        Ui.Says(_cardImage, sheet.Says);
        Ui.SetText(_previewCaption, sheet.Caption);
        Ui.Show(_sheetNav, sheet.NavShown);
        _prevPage.Enabled = sheet.PreviousEnabled;
        _nextPage.Enabled = sheet.NextEnabled;
    }

    // White paper on a pale window: a grey hairline round the preview (never the print) shows where the paper ends.
    private static void PaperEdge(RgbaImage sheet)
    {
        void Grey(int x, int y)
        {
            var i = (y * sheet.Width + x) * 4;
            sheet.Data[i] = sheet.Data[i + 1] = sheet.Data[i + 2] = 170;
            sheet.Data[i + 3] = 255;
        }
        for (var x = 0; x < sheet.Width; x++) { Grey(x, 0); Grey(x, sheet.Height - 1); }
        for (var y = 0; y < sheet.Height; y++) { Grey(0, y); Grey(sheet.Width - 1, y); }
    }

    /* ---- the tool rail -------------------------------------------- */

    internal void ToggleTool(string tool)
    {
        _tool = _tool == tool ? null : tool;
        Render();
    }

    private void CloseTool()
    {
        _tool = null;
        Render();
    }

    private void RenderTools(Screen m)
    {
        // the design can only be changed before an ID is started: Core takes
        // its button away and shuts its drawer once one is
        Ui.Show(_railDesign, m.Tools.DesignOffered);
        _tool = m.Tools.Open;

        // Editing the map and having the Card design drawer open are the
        // same state, and this is where that is enforced rather than in each
        // of the handlers that can close it: the Keep button lives in that
        // drawer, so any route that closed the drawer without it would leave
        // the boxes on the card with no button anywhere to finish with.
        if (EditingMap && _tool != ToolKey.Design) StopAdjusting();

        Ui.Show(_drawer, _tool is not null);
        _railRegister.Title = m.Tools.RegisterButton;
        if (_tool is null) return;
        Ui.SetText(_drawerTitle, m.Tools.DrawerTitle);
        Ui.Show(_toolRegister, _tool == ToolKey.Register);
        Ui.Show(_toolBatch, _tool == ToolKey.Batch);
        Ui.Show(_toolPrinter, _tool == ToolKey.Printer);
        Ui.Show(_toolDesign, _tool == ToolKey.Design);
        if (m.Register is { } register) RenderRegister(register);
        if (_tool == ToolKey.Batch) RenderBatch();
        if (_tool == ToolKey.Design) RenderDesign();
        if (_tool == ToolKey.Printer)
        {
            _cmbFlip.SelectItem(_session.Flip == DuplexFlip.ShortEdge ? 1 : 0);
            _chkRotate.State = _session.RotateBack ? NSCellStateValue.On : NSCellStateValue.Off;
        }
    }

    /// <summary>The issued IDs, who is signed in to the shared register on this computer, and what the register has refused.</summary>
    private void RenderRegister(RegisterView v)
    {
        _btnRegisterCsv.Enabled = v.ExportEnabled;
        Ui.SetText(_registerAccount, v.Account);
        Ui.Show(_signInForm, v.SignInShown);
        Ui.Show(_signedInActions, v.SignedInShown);
        Ui.Show(_refusedPanel, v.RefusedShown);
        Ui.SetText(_refusedNote, v.RefusedNote);
        Ui.SetText(_registerNote, v.Note);
        Ui.SetText(_registerMsg, _registerMessage);
        Ui.Show(_registerMsg, _registerMessage.Length > 0);
        if (v.Key == _registerShown) return;
        _registerShown = v.Key;
        foreach (var old in _registerList.ArrangedSubviews) { _registerList.RemoveArrangedSubview(old); old.RemoveFromSuperview(); }
        foreach (var entry in v.Rows)
        {
            _registerList.AddArrangedSubview(Ui.Column(1,
                Ui.Label($"{entry.Head} · {entry.When}", bold: true, maxWidth: 280),
                Ui.Label(entry.Detail, 12, muted: true, maxWidth: 280)));
        }
        if (v.More.Length > 0) _registerList.AddArrangedSubview(Ui.Label(v.More, 12, muted: true, maxWidth: 280));
    }

    private void SaveRegisterCsv()
    {
        var path = AskWhereToSave("Save the list of issued IDs", $"apg-id-register-{DateTime.Now:yyyy-MM-dd}.csv");
        if (path is null) return;
        try
        {
            File.WriteAllText(path, _session.RegisterCsv());
            _registerMessage = $"Saved {Path.GetFileName(path)}.";
            ShowInFinder(path);
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException)
        {
            _registerMessage = $"The list couldn’t be saved there ({x.Message.TrimEnd('.')}).";
        }
        Render();
    }

    private void ImportRegisterCsv()
    {
        var path = AskForFile("Choose a saved list of issued IDs");
        if (path is null) return;
        try
        {
            _registerMessage = _session.ImportRegister(File.ReadAllText(path));
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException)
        {
            _registerMessage = $"That file couldn’t be read ({x.Message.TrimEnd('.')}).";
        }
        Render();
    }

    internal async Task SignIn()
    {
        if (!_btnSignIn.Enabled) return;
        _btnSignIn.Enabled = false;
        _registerMessage = "Signing in…";
        Render();
        try
        {
            // Core signs in, reads the register and sends the cards saved before anyone signed in (Session.SignIn)
            var signingIn = _session.SignIn(_txtSignInEmail.StringValue, _txtSignInPassword.StringValue);
            // the password is not kept anywhere, this box included: it has been handed over
            _txtSignInPassword.StringValue = "";
            var result = await signingIn;
            _registerMessage = result.Message;
            Render();
            if (!result.Ok) Window.MakeFirstResponder(_txtSignInPassword);
        }
        finally { _btnSignIn.Enabled = true; }
    }

    private void SignOut()
    {
        _registerMessage = _session.SignOut();
        Render();
    }

    private async Task CheckRegister()
    {
        _btnRegisterTest.Enabled = false;
        _registerMessage = "Asking the register…";
        Render();
        try
        {
            _registerMessage = await _session.CheckRegister();
            Render();
        }
        finally { _btnRegisterTest.Enabled = true; }
    }

    /* ---- asking the operator --------------------------------------- */

    private static bool AskYesNo(string question, string yes)
    {
        using var alert = new NSAlert { MessageText = CardConfig.AppName, InformativeText = question };
        alert.AddButton(yes);
        alert.AddButton("Cancel");
        return alert.RunModal() == 1000;      // NSAlertFirstButtonReturn
    }

    /// <summary>Asks for a file to open. Replaced in the window test.</summary>
    internal Func<string, string?> AskForFile { get; set; } = title =>
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.Title = title;
        panel.CanChooseFiles = true;
        panel.CanChooseDirectories = false;
        panel.AllowsMultipleSelection = false;
        return panel.RunModal() == 1 ? panel.Url?.Path : null;
    };

    /// <summary>Asks where to save a file. Replaced in the window test.</summary>
    internal Func<string, string, string?> AskWhereToSave { get; set; } = (title, suggested) =>
    {
        var panel = NSSavePanel.SavePanel;
        panel.Title = title;
        panel.NameFieldStringValue = suggested;
        panel.DirectoryUrl = NSUrl.FromFilename(DocumentsFolder);
        return panel.RunModal() == 1 ? panel.Url?.Path : null;
    };

    /* ---- for the window test --------------------------------------- */

    internal NSTextField LastBox => _txtLast;
    internal NSTextField FirstBox => _txtFirst;
    internal NSTextField SeqBox => _txtSeq;
    internal NSPopUpButton KindBox => _cmbKind;
    internal NSPopUpButton TitleBox => _cmbTitle;
    internal string BlockedLine => _blocked.Hidden ? "" : _blocked.StringValue;
    internal string SeqLine => _msgSeq.StringValue;
    internal string ReviewErrorLine => _reviewErrorLabel.Hidden ? "" : _reviewErrorLabel.StringValue;
    internal string SavedLine => _savedStage.Hidden ? "" : _savedLine.StringValue;
    internal bool Saved => _saved;
    internal NSButton SignButton => _btnSign;
    internal NSButton NewIdButton => _btnNewId;
    internal NSButton NextButton => _btnNext;
    internal string AccountLine => _registerAccount.StringValue;
    internal NSTextField SignInEmailBox => _txtSignInEmail;
    internal NSSecureTextField SignInPasswordBox => _txtSignInPassword;
    internal string RegisterMessage => _registerMessage;
    internal bool RefusedShown => !_refusedPanel.Hidden && !_drawer.Hidden && !_toolRegister.Hidden;
    internal string RefusedLine => _refusedNote.StringValue;
    internal NSButton RefusedDoneButton => _btnRefusedDone;
    internal bool SignInFormShown => !_signInForm.Hidden && !_drawer.Hidden;
}
