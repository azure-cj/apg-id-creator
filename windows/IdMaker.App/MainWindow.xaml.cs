// The window: it shows what the Session holds and passes on what the
// operator does. It decides nothing about the card or the steps itself —
// Render() redraws everything from the Session each time something changes,
// the way the form has always worked, so no two parts of the screen can
// disagree about where things stand.

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IdMaker.Core;
using IdMaker.Vision;
using Microsoft.Win32;

namespace IdMaker.App;

public partial class MainWindow : Window
{
    private const string PictureFilter = "Pictures (PNG, JPG, WebP)|*.png;*.jpg;*.jpeg;*.webp";

    private readonly FileStore _files;
    private readonly Session _session;
    private readonly TemplateStore _templates;
    private readonly DesignDesk _design;

    private CardRenderer? _renderer;
    private CardArt? _builtInArt;
    private CardArt? _art;
    private string _loadError = "";
    private OnnxBackgroundRemover? _remover;
    private string _removerProblem = "";

    private bool _front = true;                 // which side the preview shows
    private bool _guides;                       // maintainer overlay (Ctrl+Shift+G)
    private bool _built;                        // false while the markup is still loading: controls announce their starting values before their neighbours exist
    private bool _syncing;                      // true while Render() writes to controls, so their handlers stand down
    private string? _tool;                      // which tool drawer is open, if any
    private bool _saved;                        // the print on screen has been saved
    private string _savedPath = "";
    private readonly DispatcherTimer _sheetTimer;
    private (Point Start, double Dx, double Dy)? _photoDrag;
    private readonly List<Button> _stepChips = new();
    private string _chipsBuiltFor = "";

    /// <summary>Asks a yes/no question. Replaced in tests, where nobody is there to answer.</summary>
    internal Func<string, string, bool> Confirm { get; set; } = (question, yes) =>
        MessageBox.Show(question, CardConfig.AppName, MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) == MessageBoxResult.OK;

    /// <summary>Opens the full-screen signing window. Replaced in tests.</summary>
    internal Func<string, DrawnSignature?, DrawnSignature?> AskForSignature { get; set; }

    /// <summary>Arriving at the signature step with nothing signed opens the signing screen by itself.</summary>
    internal bool OpenSigningOnArrival { get; set; } = true;

    internal Session Session => _session;
    internal TemplateStore Templates => _templates;
    internal CardRenderer? Renderer => _renderer;
    internal CardArt? Art => _art;

    public MainWindow() : this(new FileStore(FileStore.DefaultDirectory), null) { }

    internal MainWindow(FileStore files, Register? register)
    {
        _files = files;
        _templates = new TemplateStore(files);
        // the window never talks to the register itself: every job that does is the Session's (Core/Jobs.cs)
        _session = new Session(register ?? new Register(files, keeper: new WindowsKeeper()), new TitleList(files), files, post: action => Dispatcher.BeginInvoke(action));
        _session.Changed += Render;
        _design = new DesignDesk(_session, _templates, files.Directory);
        AskForSignature = (name, existing) =>
        {
            var window = new SignWindow(name, existing) { Owner = this };
            window.ShowDialog();
            return window.Result;
        };

        InitializeComponent();
        _built = true;
        FitScreen();
        GiveInfoMarksTheirWords(this);
        // the operator's own choice of light or dark, if one was made on this computer;
        // otherwise the window stays as Windows is set (App.OnStartup)
        if (files.Get(ThemeManager.ChoiceKey) is (ThemeManager.AlwaysLight or ThemeManager.AlwaysDark) and var chosen) ThemeManager.Choose(chosen);
        ShowAppearanceChoice();
        MapOverlay.Store = _templates;
        MapOverlay.Changed += moved => { if (moved) { ApplyGeometry(); DrawPreview(); } DesignReadout.Text = MapOverlay.Readout(); };
        // Still needed: Render runs on every keystroke and every change of the window's size, and a
        // sheet is every card in the print drawn again. The sheet is drawn once nothing has asked for
        // it for 120 ms. (The Mac draws it at once; AppTests waits for this timer.)
        _sheetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _sheetTimer.Tick += (_, _) => { _sheetTimer.Stop(); DrawSheetPreview(); };

        AppVersion.Text = $"Version {CardConfig.AppVersion}";
        TxtPrompt.Text = CardConfig.PhotoPrompt;
        SeqPrefix.Text = $"{CardConfig.EmployeePrefix} -";
        foreach (var y in _session.YearsOffered()) CmbYear.Items.Add(y);
        foreach (var p in CardConfig.Papers)
        {
            CmbPaper.Items.Add(new ComboBoxItem { Content = p.Label, Tag = p.Key });
            CmbBatchPaper.Items.Add(new ComboBoxItem { Content = p.Label, Tag = p.Key, IsSelected = p.Key == CardConfig.DefaultPaper });
        }
        BuildStepChips();
        RenderBatchLayout();
        UpdateBatchButton();

        PreviewKeyDown += Window_PreviewKeyDown;
        CardSurface.SizeChanged += (_, _) => DrawPreview();
        CardBox.SizeChanged += (_, _) => DrawPreview();
        Columns.SizeChanged += (_, _) => FitColumns();
        Loaded += (_, _) => Boot();
        // light or dark, as Windows is set, and follow it if it changes while the app is open
        SourceInitialized += (_, _) => ThemeManager.Follow(this);
        ThemeManager.Changed += OnThemeChanged;
        Closed += (_, _) => ThemeManager.Changed -= OnThemeChanged;
        Closed += (_, _) => _remover?.Dispose();
        Render();
    }

    /// <summary>
    /// Each information mark ("i") shows its words in a note box (ToolTip.Note);
    /// a screen reader gets the same words as its help text, so they are
    /// written once, in the markup.
    /// </summary>
    private static void GiveInfoMarksTheirWords(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is Button { ToolTip: string words } info && ReferenceEquals(info.Style, Theme.Style("Button.Info")))
            {
                AutomationProperties.SetHelpText(info, words);
                info.ToolTip = Note(words);
            }
            GiveInfoMarksTheirWords(child);
        }
    }

    /// <summary>Words for an "i" or a warning mark, in the note box they open in.</summary>
    private static ToolTip Note(string words) => new() { Style = Theme.Style("ToolTip.Note"), Content = words };

    /// <summary>
    /// Open no bigger than the screen's working area, so the whole window shows
    /// on a 1920 × 1080 laptop at 150% (about 1280 × 672 left once the taskbar
    /// is counted); the card shrinks to fit, the form keeps its size.
    /// </summary>
    private void FitScreen()
    {
        var work = SystemParameters.WorkArea;
        if (work.Width <= 0 || work.Height <= 0) return;
        MinWidth = Math.Min(MinWidth, work.Width);
        MinHeight = Math.Min(MinHeight, work.Height);
        Width = Math.Min(Width, work.Width);
        Height = Math.Min(Height, work.Height);
    }

    /* ---- light or dark ------------------------------------------- */

    private RadioButton[] AppearanceOptions => new[] { AppearSystem, AppearLight, AppearDark };

    private void ShowAppearanceChoice(bool glide = false)
    {
        // SAFETY: each option carries "system", "light" or "dark" as a string Tag in the markup.
        var chosen = Array.FindIndex(AppearanceOptions, o => (string)o.Tag == ThemeManager.Choice);
        for (var i = 0; i < AppearanceOptions.Length; i++) AppearanceOptions[i].IsChecked = i == chosen;
        SlideThumb(AppearanceThumb, AppearanceChoices, chosen, glide);
    }

    private void AppearanceChoices_SizeChanged(object sender, SizeChangedEventArgs e) => ShowAppearanceChoice();

    /// <summary>
    /// Put a switch's gold thumb under the chosen option, an equal share of the
    /// track wide: gliding there when asked (Motion), straight there otherwise.
    /// </summary>
    private static void SlideThumb(Border thumb, Panel options, int chosen, bool glide)
    {
        var share = options.ActualWidth / Math.Max(1, options.Children.Count);
        if (share <= 0) return;                                   // not laid out yet: SizeChanged comes back here
        thumb.Width = share;
        Motion.Glide(thumb, Canvas.LeftProperty, Math.Max(0, chosen) * share, glide);
    }

    // a switch's track changed size (the window, or the panel it is in): its thumb follows, without gliding
    private void SwitchChoices_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        SlideThumb(LayoutThumb, LayoutChoices, _session.Layout == SheetMode.Duplex ? 1 : 0, glide: false);
    }

    private void Appearance_Click(object sender, RoutedEventArgs e)
    {
        // SAFETY: only the three Appearance options use this handler, each with a string Tag.
        var choice = (string)((FrameworkElement)sender).Tag;
        ThemeManager.Choose(choice);
        // following Windows is the default, and needs nothing kept
        _files.Set(ThemeManager.ChoiceKey, choice == ThemeManager.MatchWindows ? null : choice);
        ShowAppearanceChoice(glide: true);
    }

    /* ---- starting up ---------------------------------------------- */

    private static string BesideProgram(params string[] parts) => Path.Combine(new[] { AppContext.BaseDirectory }.Concat(parts).ToArray());

    private string StoredArtwork(string side) => _design.StoredArtwork(side);

    private void Boot()
    {
        try
        {
            var measurer = WpfTextMeasurer.Load(BesideProgram("Fonts"));
            _renderer = new CardRenderer(measurer);
            _builtInArt = new CardArt(ImageFiles.Load(BesideProgram("Assets", CardConfig.FrontArtFile)), ImageFiles.Load(BesideProgram("Assets", CardConfig.BackArtFile)));
            BrandLogo.Source = ImageFiles.Load(BesideProgram("Assets", CardConfig.LogoFile));
            _art = _builtInArt;

            // a design put in earlier is still the one in use
            if (_templates.Restore() && _templates.HasArtwork)
            {
                try { _art = new CardArt(ImageFiles.Load(StoredArtwork("front")), ImageFiles.Load(StoredArtwork("back"))); }
                catch (Exception e) when (e is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException)
                {
                    // the pictures are gone: the record of them is no use without them
                    _templates.Clear();
                    _art = _builtInArt;
                }
            }
            ApplyGeometry();
            // Only now: whether a name fits the card cannot be answered
            // without the fonts, and until it is, an unfittable name counts
            // as finished.
            _session.Measurer = measurer;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException or InvalidOperationException)
        {
            _loadError = $"The card could not be loaded: {e.Message} Install the app again to put its files back.";
        }

        // start reading the register now rather than on the first keystroke
        if (_session.HasSharedRegister) _ = WarmRegister();
        // the model takes a moment to load; the form does not wait for it
        _ = LoadRemover();
        Render();
        TxtLast.Focus();
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
            var r = OnnxBackgroundRemover.TryCreate(out var why);
            return (r, why);
        });
        _remover = remover;
        _removerProblem = problem;
        Render();
    }

    private void ApplyGeometry() => _design.Apply();

    // Everything named by DynamicResource has already changed colour. What is
    // left are the few pictures drawn from the window's colours — none at the
    // moment, but the preview is cheap to redraw and keeps that true.
    private void OnThemeChanged() => Render();

    /* ---- drawing the screen --------------------------------------- */

    private static void Show(UIElement element, bool visible) => element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private static void SetText(TextBlock block, string text)
    {
        if (block.Text != text) block.Text = text;
    }

    // An error under a field: the words, and the same words where a screen reader looks for them.
    private static void FieldMessage(TextBlock message, Control input, string error)
    {
        SetText(message, error);
        AutomationProperties.SetHelpText(input, error);
        Theme.Paint(input, Control.BorderBrushProperty, error.Length > 0 ? "Color.Danger" : "Color.ControlBorder");
    }

    /// <summary>What only the window knows. Core decides the rest of the screen from it (Screen.From).</summary>
    private ScreenInput ScreenState() => new(HostPlatform.Windows)
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
        TabletDriver = PenTablet.DriverInstalled,
        EditingMap = EditingMap,
        Tool = _tool,
    };

    private static string TextStyle(Tone tone) => tone switch { Tone.Error => "Text.Error", Tone.Warning => "Text.Warning", _ => "Text.Note" };

    // Core says what the screen shows (Screen.From); this copies it onto the controls.
    internal void Render()
    {
        if (!_built) return;
        _syncing = true;
        try
        {
            var s = _session;
            var m = Screen.From(s, ScreenState());

            // the stage shows the card while it is being made, the sheet at the end
            Show(SideTabs, m.Stage.SideTabsShown);
            SideFront.IsChecked = _front;
            SideBack.IsChecked = !_front;
            // the flip switch: on for the back; its knob slides to the side shown
            SideFlip.IsChecked = !_front;
            AutomationProperties.SetName(SideFlip, _front ? "Show the back of the card" : "Show the front of the card");
            Motion.Glide(SideKnob, Canvas.LeftProperty, _front ? 4 : 28, glide: true);
            Show(CardLoading, m.Stage.LoadingShown);
            SetText(CardLoading, m.Stage.LoadingText);
            Show(CardBox, m.Stage.CardShown);
            Show(SheetImage, m.Stage.SheetShown);
            // the page buttons' row is kept from the moment the check step opens, so the sheet never
            // appears at one size and then shrinks when the row arrives
            if (s.StepIndex != Session.LastStep) Show(SheetNav, false);
            else if (SheetNav.Visibility == Visibility.Collapsed) SheetNav.Visibility = Visibility.Hidden;
            SetText(Caption, m.Stage.Caption);

            // name
            FieldMessage(MsgLast, TxtLast, m.Name.LastError);
            FieldMessage(MsgFirst, TxtFirst, m.Name.FirstError);
            FieldMessage(MsgMiddle, TxtMiddle, m.Name.MiddleError);
            FieldMessage(MsgSuffix, TxtSuffix, m.Name.SuffixError);
            SyncText(TxtLast, s.Last);
            SyncText(TxtFirst, s.First);
            SyncText(TxtMiddle, s.Middle);
            SyncText(TxtSuffix, s.Suffix);
            NamePreview.Style = Theme.Style(m.Name.PreviewIsError ? "Text.Error" : "Text.Body");
            SetText(NamePreview, m.Name.Preview);
            // the name as it prints is on the card, outlined; this line is only for one that will not fit
            Show(NamePreview, m.Name.Preview.Length > 0);

            // who the ID is for
            // SAFETY: CmbKind's items are the two ComboBoxItems in the markup, each with its kind as a string Tag.
            CmbKind.SelectedItem = CmbKind.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == s.Kind);
            FieldMessage(MsgKind, CmbKind, m.Title.KindError);

            // job title
            BuildTitleChips();
            SyncChips(TitleChips, s.Role1.Pick, null);
            Show(TxtTitleCustom, m.Title.CustomShown);
            SyncText(TxtTitleCustom, s.Role1.Custom);
            FieldMessage(MsgTitle, TxtTitleCustom, m.Title.TitleError);
            Show(FieldTitle2, m.Title.SecondShown);
            Show(BtnRoleAdd, m.Title.AddSecondShown);
            // whatever the main job title is disappears from the second row
            SyncChips(Title2Chips, s.Role2.Pick, s.Role1.Value);
            Show(TxtTitle2Custom, m.Title.SecondCustomShown);
            SyncText(TxtTitle2Custom, s.Role2.Custom);
            FieldMessage(MsgTitle2, TxtTitle2Custom, m.Title.SecondError);
            Show(TitlePreview, m.Title.FitError.Length > 0);
            SetText(TitlePreview, m.Title.FitError);

            // number: the error while there is one, otherwise the standing note about what was checked
            MsgSeq.Style = Theme.Style(m.Number.LineIsError ? "Text.Error" : "Text.Note");
            FieldMessage(MsgSeq, TxtSeq, m.Number.Error);
            if (!m.Number.LineIsError) SetText(MsgSeq, m.Number.Help);
            SyncText(TxtSeq, s.Seq);
            if (!Equals(CmbYear.SelectedItem, s.Year)) CmbYear.SelectedItem = s.Year;
            Show(ReissueWrap, m.Number.ReissueShown);
            ChkReissue.IsChecked = s.Reissue;

            RenderPhotoStep(m.Photo);
            RenderSignStep(m.Sign);
            if (m.Check is { } check) RenderCheckStep(check);

            // one step at a time
            for (var i = 0; i < StepPanels.Length; i++) Show(StepPanels[i], i == m.StepIndex);
            RenderStepChips(m.Chips);

            // the line under the buttons: a refusal while there is one,
            // otherwise the quiet standing hint
            Show(Blocked, m.Footer.BlockedShown);
            SetText(Blocked, m.Footer.BlockedText);
            Show(Status, m.Footer.StatusShown);
            SetText(Status, m.Footer.StatusText);

            Show(BtnBack, m.Footer.BackShown);
            Show(BtnNext, m.Footer.NextShown);
            BtnNext.Content = m.Footer.NextTitle;
            BtnNext.IsEnabled = m.Footer.NextEnabled;
            // on the check step the gold button at the right edge is Save the PDF, then Start a new ID
            var reviewing = m.Check is { Saved: false };
            var saved = m.Check is { Saved: true };
            Show(BtnDownload, reviewing);
            Show(BtnNewId, saved);
            Show(BtnOpenFolder, saved);

            RenderJob(m.Job);
            RenderTools(m);
            DrawPreview();
            RenderCardFocus(m.Stage.CardShown);
            if (m.Stage.CardShown) AutomationProperties.SetName(CardImage, m.Stage.CardSays);
        }
        finally { _syncing = false; }
    }

    /* ---- where the step prints on the card ------------------------ */

    // The step's own box on the card and the words on its tag. The check
    // step shows the whole sheet instead, so it has none. The job step holds
    // two boxes: the job until it is settled, then the number.
    private (BoxKey Box, string Label)? FocusFor(int step) => step switch
    {
        (int)Step.Name => (BoxKey.Name, "Name"),
        (int)Step.Title => Screen.LandingFor(_session, step) == FocusTarget.Seq || TxtSeq.IsKeyboardFocusWithin || CmbYear.IsKeyboardFocusWithin
            ? (BoxKey.EmployeeNo, "Employee number") : (BoxKey.Title, "Job position"),
        (int)Step.Photo => (BoxKey.Photo, "Photo"),
        (int)Step.Sign => (BoxKey.Signature, "Signature"),
        _ => null,
    };

    /// <summary>
    /// Outline where the step being filled in prints, so the card itself is
    /// the preview of what is typed. Only on the front, and never while the
    /// map is being dragged: then the map's own boxes are on the card.
    /// </summary>
    private void RenderCardFocus(bool cardShown)
    {
        var focus = FocusFor(_session.StepIndex);
        var shown = cardShown && _front && !EditingMap && focus is not null;
        // it glides from the last field to the next only when it was already on the card
        var glide = shown && FocusLayer.Visibility == Visibility.Visible;
        Show(FocusLayer, shown);
        if (!shown || focus is not { } f) { _focusAt = null; return; }

        // CardSurface is drawn at this many units to the millimetre (540 × 856 for 54 × 85.6 mm)
        var unit = CardSurface.Width / CardConfig.CardWidthMm;
        const double ring = 5;                       // the two rings sit just outside the box
        var box = _templates.Box(f.Box);
        var ringAt = new Rect(box.X * unit - ring, box.Y * unit - ring, box.W * unit + 2 * ring, box.H * unit + 2 * ring);
        // Render runs on every keystroke: start a glide only when the place has changed
        if (_focusAt == ringAt && CardFocusText.Text == f.Label) return;
        _focusAt = ringAt;

        Motion.Glide(CardFocus, Canvas.LeftProperty, ringAt.X, glide);
        Motion.Glide(CardFocus, Canvas.TopProperty, ringAt.Y, glide);
        Motion.Glide(CardFocus, WidthProperty, ringAt.Width, glide);
        Motion.Glide(CardFocus, HeightProperty, ringAt.Height, glide);

        if (CardFocusText.Text != f.Label && glide) Motion.FadeIn(CardFocusTag, 0.2, 240);
        SetText(CardFocusText, f.Label);
        CardFocusTag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var tagHeight = CardFocusTag.DesiredSize.Height;
        // the tag sits on the box's top edge, or inside it when the box is at the top of the card
        var above = ringAt.Y - tagHeight - 4;
        Motion.Glide(CardFocusTag, Canvas.LeftProperty, Math.Max(4, ringAt.X), glide);
        Motion.Glide(CardFocusTag, Canvas.TopProperty, above >= 4 ? above : box.Y * unit + 4, glide);
    }

    private Rect? _focusAt;

    // one panel per step, in Session.Steps order
    private FrameworkElement[] StepPanels => new FrameworkElement[] { StepName, StepTitle, StepPhoto, StepSign, StepCheck };                     // where the outline was last sent, so a keystroke does not restart its glide

    private static void SyncText(TextBox box, string value)
    {
        if (box.Text == value) return;
        var caret = box.CaretIndex;
        box.Text = value;
        box.CaretIndex = Math.Min(caret, value.Length);
    }

    /* ---- the step chips ------------------------------------------- */

    // Five equal shares of the bar, so the rules over the steps line up evenly;
    // each step shows its short name (Session.Steps), which fits one share with its lock.
    private void BuildStepChips()
    {
        for (var i = 0; i < Session.Steps.Length; i++)
        {
            var index = i;
            StepsBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var chip = new Button { Style = Theme.Style("Step") };
            Grid.SetColumn(chip, i);
            chip.Click += (_, _) => AfterMove(_session.StepIndex, _session.ChipPressed(index));
            StepsBar.Children.Add(chip);
            _stepChips.Add(chip);
        }
    }

    private void RenderStepChips(IReadOnlyList<StepChip> states)
    {
        for (var i = 0; i < _stepChips.Count; i++)
        {
            var state = states[i];
            var (locked, current, done) = (state.Locked, state.Current, state.Done);
            var chip = _stepChips[i];
            // the short name on the bar; the whole name is what a screen reader says (Says, below)
            chip.Content = ChipContent(state.Short, locked);
            // The rule over the word carries the state in its thickness as well
            // as its colour: thick for finished and for the step you are on,
            // a hairline for the ones to come. Gold marks only where you are.
            chip.BorderThickness = new Thickness(0, done || current ? 3 : 1, 0, 0);
            // A locked step keeps readable words; only its rule fades to the panel's own line.
            Theme.Paint(chip, Control.BorderBrushProperty, current ? "Color.AccentSolid" : done ? "Color.Strong" : locked ? "Color.Line" : "Color.ControlBorder");
            Theme.Paint(chip, Control.ForegroundProperty, current || done ? "Color.Text" : "Color.TextMuted");
            chip.FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal;
            AutomationProperties.SetName(chip, state.Says);
            chip.ToolTip = state.ToolTip.Length > 0 ? state.ToolTip : null;
        }
    }

    // A step that cannot be opened yet carries a small lock before its name,
    // so "not yet" shows in a shape and not only in a fainter grey.
    private static object ChipContent(string label, bool locked)
    {
        if (!locked) return label;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var lockMark = new System.Windows.Shapes.Path
        {
            // a shackle and a body, drawn rather than taken from a font that may not be there
            Data = System.Windows.Media.Geometry.Parse("M2.5,5 L2.5,3.5 A2.5,2.5 0 0 1 7.5,3.5 L7.5,5 M1,5 L9,5 L9,11 L1,11 Z"),
            StrokeThickness = 1.3, Width = 10, Height = 12, Margin = new Thickness(0, 1, 5, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        lockMark.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Color.TextMuted");
        row.Children.Add(lockMark);
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    /* ---- moving between steps ------------------------------------- */

    // Core names where the cursor goes (Screen.AfterMove); this is which control that is here.
    private Control? ControlFor(FocusTarget target) => target switch
    {
        FocusTarget.Last => TxtLast,
        FocusTarget.First => TxtFirst,
        FocusTarget.Middle => TxtMiddle,
        FocusTarget.Suffix => TxtSuffix,
        FocusTarget.Kind => CmbKind,
        FocusTarget.TitleChoice => FirstChip(TitleChips),
        FocusTarget.TitlePicked => TitleChips.Children.OfType<RadioButton>().FirstOrDefault(c => c.IsChecked == true) ?? FirstChip(TitleChips),
        FocusTarget.TitleCustom => TxtTitleCustom,
        FocusTarget.Title2Choice => FirstChip(Title2Chips),
        FocusTarget.Title2Custom => TxtTitle2Custom,
        FocusTarget.Seq => TxtSeq,
        FocusTarget.PhotoButton => BtnPhoto,
        FocusTarget.SignButton => BtnSign,
        FocusTarget.SaveButton => BtnDownload,
        FocusTarget.NewIdButton => BtnNewId,
        _ => null,
    };

    private static Control? FirstChip(Panel chips) => chips.Children.OfType<RadioButton>().FirstOrDefault(c => c.IsVisible);

    /// <param name="result">What Next or a step chip answered; nothing for a move that cannot be refused.</param>
    private void AfterMove(int from, MoveResult? result = null)
    {
        var after = Screen.AfterMove(_session, from, result, _saved, OpenSigningOnArrival);
        if (after.ShowFront) _front = true;
        _saved = after.Saved;
        if (after.StepChanged) StepScroll.ScrollToTop();
        Render();
        // the new page comes in from the side you are heading: from the right going on, from the left going back
        if (after.StepChanged) Motion.SlideIn(StepPanels[_session.StepIndex], _session.StepIndex > from ? 18 : -18);
        // the card gives way to the sheet (or back): a short fade rather than a swap
        if ((from == Session.LastStep) != (_session.StepIndex == Session.LastStep)) Motion.FadeIn(StageView, 0, 220);
        var target = after.Focus is { } focus ? ControlFor(focus) : null;
        // after the layout pass, so a control that has only just appeared can take the focus
        if (target is not null) Dispatcher.BeginInvoke(() => target.Focus(), DispatcherPriority.Input);
        if (after.OpenSigning) Dispatcher.BeginInvoke(SignNow, DispatcherPriority.ApplicationIdle);
    }

    private void BtnNext_Click(object sender, RoutedEventArgs e) => AfterMove(_session.StepIndex, _session.NextStep());

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        var from = _session.StepIndex;
        _session.Back();
        AfterMove(from);
    }

    private void SummaryEdit_Click(object sender, RoutedEventArgs e)
    {
        // SAFETY: the three Edit buttons carry "0", "1" and "1" in the markup.
        var step = int.Parse((string)((FrameworkElement)sender).Tag, CultureInfo.InvariantCulture);
        var from = _session.StepIndex;
        _session.GoStep(step);
        AfterMove(from);
        // the number shares the job's page: its Edit goes straight to the number
        if (ReferenceEquals(sender, EditNumber) && _session.StepIndex == step) Dispatcher.BeginInvoke(() => TxtSeq.Focus(), DispatcherPriority.Input);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _tool is not null) { CloseTool(); e.Handled = true; return; }
        if (e.Key == Key.G && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _guides = !_guides;
            Render();
            e.Handled = true;
            return;
        }
        // Enter in a text box of the form moves on, rather than doing nothing
        if (e.Key == Key.Enter && Keyboard.FocusedElement is TextBox { AcceptsReturn: false } box && IsInForm(box) && _session.StepIndex != Session.LastStep)
        {
            e.Handled = true;
            AfterMove(_session.StepIndex, _session.NextStep());
        }
    }

    private bool IsInForm(DependencyObject element)
    {
        for (DependencyObject? d = element; d is not null; d = VisualTreeHelper.GetParent(d)) if (ReferenceEquals(d, FormPanel)) return true;
        return false;
    }


    /* ---- step 1: name --------------------------------------------- */

    private static Field PartOf(object sender) =>
        // SAFETY: only the four name boxes use these handlers, and each carries its Field's name as its Tag.
        Enum.Parse<Field>((string)((FrameworkElement)sender).Tag);

    private void Name_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        // SAFETY: wired only to TextBox elements in the markup.
        _session.SetNamePart(PartOf(sender), ((TextBox)sender).Text);
        Render();
    }

    private void Name_LostFocus(object sender, RoutedEventArgs e)
    {
        _session.LeaveNamePart(PartOf(sender));
        Render();
    }

    /* ---- step 2: job and number ----------------------------------- */

    // The chip rows are rebuilt only when the list of titles changes — not
    // on every keystroke — so focus and the arrow keys' place in the row survive.
    private void BuildTitleChips()
    {
        var titles = _session.TitleOptions();
        var key = string.Join("\n", titles);
        if (key == _chipsBuiltFor) return;
        _chipsBuiltFor = key;
        Fill(TitleChips, "role1", pick =>
        {
            _session.PickRole1(pick);
            Render();
            if (pick == RoleChoice.Other) Dispatcher.BeginInvoke(() => TxtTitleCustom.Focus(), DispatcherPriority.Input);
        });
        Fill(Title2Chips, "role2", pick =>
        {
            _session.PickRole2(pick);
            Render();
            if (pick == RoleChoice.Other) Dispatcher.BeginInvoke(() => TxtTitle2Custom.Focus(), DispatcherPriority.Input);
        });

        void Fill(Panel panel, string group, Action<string> onPick)
        {
            panel.Children.Clear();
            foreach (var title in titles) panel.Children.Add(Chip(title, title, group, onPick, removable: !TitleList.IsStandard(title)));
            panel.Children.Add(Chip(RoleChoice.Other, "Other…", group, onPick, removable: false));
        }
    }

    private RadioButton Chip(string value, string label, string group, Action<string> onPick, bool removable)
    {
        var chip = new RadioButton { Style = Theme.Style("Chip"), GroupName = group, Tag = value, Content = label };
        chip.Click += (_, _) => { if (!_syncing) onPick(value); };
        if (removable)
        {
            // a title this computer remembered can be dropped again
            var menu = new ContextMenu();
            var item = new MenuItem { Header = $"Remove “{label}” from the list" };
            item.Click += (_, _) => { _session.ForgetTitle(value); Render(); };
            menu.Items.Add(item);
            chip.ContextMenu = menu;
            chip.ToolTip = "Right-click to remove this title from the list";
        }
        return chip;
    }

    // Tick the chosen chip; `hide` removes one option from the row.
    private static void SyncChips(Panel panel, string selected, string? hide)
    {
        foreach (var chip in panel.Children.OfType<RadioButton>())
        {
            // SAFETY: every chip's Tag is the string value given to Chip().
            var value = (string)chip.Tag;
            var hidden = Session.SecondRoleHides(value, hide ?? "");
            Show(chip, !hidden);
            chip.IsChecked = !hidden && value == selected;
        }
    }

    private void CmbKind_Changed(object sender, SelectionChangedEventArgs e)
    {
        // SAFETY: CmbKind's items are the two ComboBoxItems in the markup, each with its kind as a string Tag.
        if (_syncing || CmbKind.SelectedItem is not ComboBoxItem { Tag: string kind }) return;
        _session.PickKind(kind);
        Render();
    }

    private void TitleCustom_TextChanged(object sender, TextChangedEventArgs e) { if (_syncing) return; _session.TypeRole1(TxtTitleCustom.Text); Render(); }
    private void Title2Custom_TextChanged(object sender, TextChangedEventArgs e) { if (_syncing) return; _session.TypeRole2(TxtTitle2Custom.Text); Render(); }
    private void TitleCustom_LostFocus(object sender, RoutedEventArgs e) { _session.Touch(Field.Title); Render(); }
    private void Title2Custom_LostFocus(object sender, RoutedEventArgs e) { _session.Touch(Field.Title2); Render(); }

    private void BtnRoleAdd_Click(object sender, RoutedEventArgs e)
    {
        _session.AddSecondRole();
        Render();
        Dispatcher.BeginInvoke(() => FirstChip(Title2Chips)?.Focus(), DispatcherPriority.Input);
    }

    private void BtnRoleRemove_Click(object sender, RoutedEventArgs e)
    {
        _session.RemoveSecondRole();
        Render();
        Dispatcher.BeginInvoke(() => BtnRoleAdd.Focus(), DispatcherPriority.Input);
    }

    /* ---- step 2, continued: the number ---------------------------- */

    private void TxtSeq_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        _session.SetSeq(TxtSeq.Text);
        Render();
    }

    private void TxtSeq_LostFocus(object sender, RoutedEventArgs e) { _session.Touch(Field.Seq); Render(); }

    // the outline on the card follows the cursor between the job and the number
    private void Number_FocusMoved(object sender, KeyboardFocusChangedEventArgs e) { if (!_syncing) RenderCardFocus(CardBox.Visibility == Visibility.Visible); }

    private void CmbYear_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || CmbYear.SelectedItem is not int year) return;
        _session.SetYear(year);
        Render();
    }

    private void ChkReissue_Click(object sender, RoutedEventArgs e) { _session.SetReissue(ChkReissue.IsChecked == true); Render(); }

    /* ---- step 3: photo -------------------------------------------- */

    private void RenderPhotoStep(PhotoView v)
    {
        var photo = _session.Photo;
        PhotoMsg.Style = Theme.Style(TextStyle(v.Tone));
        SetText(PhotoMsg, v.Message);
        BtnPhoto.Content = v.ButtonTitle;
        BtnPhoto.IsEnabled = v.ButtonEnabled;
        // it stands out until there is a photo; after that the way forward is Next
        BtnPhoto.Style = Theme.Style(photo is null ? "Button.Strong" : "Button.Ghost");
        SetText(PhotoName, v.FileName);
        Show(PhotoName, v.HasPhoto);
        PhotoThumb.Source = photo is null ? null : ThumbFor(photo);

        Show(AdjustBox, v.HasPhoto);
        BtnAdjustReset.IsEnabled = v.ResetEnabled;
        if (Math.Abs(SldZoom.Value - v.ZoomPercent) > 0.01) SldZoom.Value = v.ZoomPercent;
        SetText(ZoomOut, v.ZoomText);
        CardSurface.Cursor = photo is not null && _front && !EditingMap ? Cursors.SizeAll : null;

        // When the photo was turned away for something the prompt fixes,
        // put the fix in front of the operator instead of leaving it folded away.
        SetText(GuideLabel, v.GuideLabel);
        if (v.WantsPrompt && !GuideBox.IsExpanded) GuideBox.IsExpanded = true;
    }

    private PhotoResult? _thumbOf;
    private BitmapSource? _thumb;

    private BitmapSource ThumbFor(PhotoResult photo)
    {
        if (!ReferenceEquals(_thumbOf, photo) || _thumb is null)
        {
            _thumbOf = photo;
            var k = Math.Min(1.0, 184.0 / photo.Image.Height);
            _thumb = CardRenderer.ToBitmap(photo.Image.Shrink(Math.Max(1, (int)(photo.Image.Width * k)), Math.Max(1, (int)(photo.Image.Height * k))));
        }
        return _thumb;
    }

    private void BtnPhoto_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose the photo", Filter = PictureFilter };
        if (dialog.ShowDialog(this) == true) _ = AcceptPhotoFile(dialog.FileName);
    }

    private void Photo_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Photo_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths) _ = AcceptPhotoFile(paths[0]);
    }

    /// <summary>Open a picture file and put it through the photo pipeline.</summary>
    internal async Task AcceptPhotoFile(string path)
    {
        // Core looks at the photo (Session.TakePhoto), and redraws through Session.Changed while it does.
        // It answers false when the card the photo was for was put aside meanwhile: nothing landed.
        if (await _session.TakePhoto(path, ImageFiles.Decode, _remover)) _front = true;
        Render();
    }

    private void Nudge_Click(object sender, RoutedEventArgs e)
    {
        // SAFETY: the four move buttons carry "dx,dy" as their Tag in the markup.
        var parts = ((string)((FrameworkElement)sender).Tag).Split(',');
        var step = CardConfig.Photo.NudgeStepMm;
        _session.Nudge(int.Parse(parts[0], CultureInfo.InvariantCulture) * step, int.Parse(parts[1], CultureInfo.InvariantCulture) * step);
        _front = true;
        Render();
    }

    private void SldZoom_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing) return;
        _session.SetZoom(SldZoom.Value / 100);
        _front = true;
        Render();
    }

    private void BtnAdjustReset_Click(object sender, RoutedEventArgs e) { _session.ResetAdjust(); Render(); }

    // dragging the photo on the card itself, and the wheel to resize it
    private bool CanDragPhoto => Screen.CanDragPhoto(_session, _front, EditingMap);

    private void Card_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!CanDragPhoto) return;
        _photoDrag = (e.GetPosition(CardSurface), _session.Adjust.Dx, _session.Adjust.Dy);
        CardSurface.CaptureMouse();
        e.Handled = true;
    }

    private void Card_MouseMove(object sender, MouseEventArgs e)
    {
        if (_photoDrag is not { } d) return;
        if (e.LeftButton != MouseButtonState.Pressed) { EndPhotoDrag(); return; }
        var now = e.GetPosition(CardSurface);
        var mmPerUnit = CardConfig.CardWidthMm / CardSurface.Width;
        _session.SetAdjustOffset(d.Dx + (now.X - d.Start.X) * mmPerUnit, d.Dy + (now.Y - d.Start.Y) * mmPerUnit);
        DrawPreview();
    }

    private void Card_MouseUp(object sender, MouseButtonEventArgs e) => EndPhotoDrag();

    private void EndPhotoDrag()
    {
        if (_photoDrag is null) return;
        _photoDrag = null;
        CardSurface.ReleaseMouseCapture();
        Render();
    }

    private void Card_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!CanDragPhoto) return;
        var step = CardConfig.Photo.ZoomStep;
        _session.SetZoom(_session.Adjust.Zoom * (e.Delta > 0 ? 1 + step : 1 - step));
        e.Handled = true;
        Render();
    }

    private void BtnCopyPrompt_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(CardConfig.PhotoPrompt);
            CopyStatus.Text = "Copied.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // another program is holding the clipboard: show and select it so Ctrl+C still works
            ShowPrompt(true);
            TxtPrompt.Focus();
            TxtPrompt.SelectAll();
            CopyStatus.Text = "Selected. Press Ctrl+C to copy.";
        }
    }

    // The prompt is for copying; reading it is the exception, one press away.
    private void BtnShowPrompt_Click(object sender, RoutedEventArgs e) => ShowPrompt(TxtPrompt.Visibility != Visibility.Visible);

    private void ShowPrompt(bool show)
    {
        Show(TxtPrompt, show);
        BtnShowPrompt.Content = show ? "Hide the prompt" : "Show the prompt";
    }

    /* ---- step 4: signature ---------------------------------------- */

    private ScannedSignature? _scanShown;

    private void RenderSignStep(SignView v)
    {
        var sig = _session.Signature;
        var scan = sig as ScannedSignature;
        Theme.Paint(SigFrame, Border.BorderBrushProperty, v.Error.Length > 0 ? "Color.Danger" : "Color.ControlBorder");
        Show(SigPreview, scan is null);
        Show(SigImage, scan is not null);
        if (scan is not null && !ReferenceEquals(_scanShown, scan))
        {
            _scanShown = scan;
            SigImage.Source = CardRenderer.TintedScan(scan, SignaturePad.InkColor);
        }
        if (scan is null) { _scanShown = null; SigImage.Source = null; }
        if (!ReferenceEquals(_padShows, sig)) { _padShows = sig; SigPreview.Value = sig as DrawnSignature; }

        BtnSign.Content = v.ButtonTitle;
        BtnSign.Style = Theme.Style(v.HasSignature ? "Button.Ghost" : "Button.Strong");
        BtnClearSig.IsEnabled = v.HasSignature;
        SetText(SigHelp, v.Help);
        Show(SigMsg, v.MessageShown);
        SetText(SigMsg, v.Message);
        SetText(TabletNote, v.TabletNote);
    }

    private Signature? _padShows;
    private string _sigError = "";

    private void BtnSign_Click(object sender, RoutedEventArgs e) => SignNow();

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
        Dispatcher.BeginInvoke(() => BtnNext.Focus(), DispatcherPriority.Input);
    }

    private void BtnClearSig_Click(object sender, RoutedEventArgs e)
    {
        _sigError = "";
        _session.SetSignature(null);
        Render();
        BtnSign.Focus();
    }

    private void BtnSigScan_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose the scanned signature", Filter = PictureFilter };
        if (dialog.ShowDialog(this) == true) _ = AcceptSignatureFile(dialog.FileName);
    }

    internal async Task AcceptSignatureFile(string path)
    {
        SigHelp.Text = "Reading the signature…";
        var scan = await _session.TakeSignatureScan(path, ImageFiles.Decode);
        if (scan.Landed)
        {
            // a scan that cannot be read leaves whatever signature was there
            _sigError = scan.Error;
            if (scan.Error.Length == 0) _front = true;
        }
        Render();
    }

    /* ---- the preview ---------------------------------------------- */

    private void DrawPreview()
    {
        if (_art is null || _renderer is null) return;
        if (_session.StepIndex == Session.LastStep)
        {
            _sheetTimer.Stop();
            // Arriving at the check step, the sheet is drawn at once: waiting for the timer left
            // the stage empty for a moment, then the sheet popped in. After that, a change
            // (typing a number of copies, resizing) waits for the timer as before.
            if (!_sheetShowing) { _sheetShowing = true; DrawSheetPreview(); }
            else _sheetTimer.Start();
            return;
        }
        _sheetShowing = false;

        // as many pixels as the card really occupies on this monitor
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var onScreen = CardBox.ActualWidth > 0 ? CardBox.ActualWidth : CardSurface.Width;
        var ppm = Math.Clamp(onScreen * dpi / CardConfig.CardWidthMm, 4, CardConfig.PrintPxPerMm);
        CardImage.Source = _front
            ? _renderer.RenderFront(_art, _session.Geometry, _session.CardData(), ppm, _guides)
            : _renderer.RenderBack(_art, ppm, _session.RotateBack);
    }

    private const double SheetPreviewPpm = 96 / 25.4;
    private bool _sheetShowing;                 // the check step's sheet is on the stage (drawn once on arrival)
    private bool _sheetChanged;                 // the print layout was just switched: the next sheet fades in

    private void DrawSheetPreview()
    {
        if (_art is null || _renderer is null || _session.StepIndex != Session.LastStep) return;
        // which page there is to show, and what is said about it, is Core's (Session.SheetPreview)
        if (_session.SheetPreview() is not { } sheet) { SheetImage.Source = null; Show(SheetNav, false); return; }
        var art = _art;
        var renderer = _renderer;
        var geo = _session.Geometry;
        // every card on a sheet has the same back; draw it once
        BitmapSource? back = null;
        SheetImage.Source = renderer.RenderSheet(sheet.Plan, sheet.Page, SheetPreviewPpm,
            i => renderer.RenderFront(art, geo, sheet.Entries[i], SheetPreviewPpm),
            _ => back ??= renderer.RenderBack(art, SheetPreviewPpm, sheet.BackUpsideDown));

        AutomationProperties.SetName(SheetImage, sheet.Says);
        SetText(PreviewCaption, sheet.Caption);
        // Hidden, not collapsed: the row is kept, so switching between one page and two
        // does not make the sheet above it grow or shrink
        SheetNav.Visibility = sheet.NavShown ? Visibility.Visible : Visibility.Hidden;
        if (_sheetChanged) { _sheetChanged = false; Motion.FadeIn(SheetImage); }
        PrevPage.IsEnabled = sheet.PreviousEnabled;
        NextPage.IsEnabled = sheet.NextEnabled;
    }

    private void Side_Click(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        FlipTo(SideFront.IsChecked == true);
    }

    // the flip switch itself: each press turns the card over
    private void SideFlip_Click(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        FlipTo(SideFlip.IsChecked != true);
    }

    private void FlipTo(bool wantFront)
    {
        if (wantFront == _front) return;
        // the knob moves at once; the card turns about its own centre, its size and place unchanged (Motion.Flip)
        Motion.Glide(SideKnob, Canvas.LeftProperty, wantFront ? 4 : 28, glide: true);
        Motion.Flip(CardBox, () => { _front = wantFront; Render(); });
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e) { _session.TurnPreviewPage(-1); DrawSheetPreview(); }
    private void NextPage_Click(object sender, RoutedEventArgs e) { _session.TurnPreviewPage(1); DrawSheetPreview(); }
}
