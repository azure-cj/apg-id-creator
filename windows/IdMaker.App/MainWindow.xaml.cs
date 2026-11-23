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
            Show(CardLoading, m.Stage.LoadingShown);
            SetText(CardLoading, m.Stage.LoadingText);
            Show(CardBox, m.Stage.CardShown);
            Show(SheetImage, m.Stage.SheetShown);
            if (s.StepIndex != Session.LastStep) Show(SheetNav, false);
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
            var steps = new FrameworkElement[] { StepName, StepTitle, StepNumber, StepPhoto, StepSign, StepCheck };
            for (var i = 0; i < steps.Length; i++) Show(steps[i], i == m.StepIndex);
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

            RenderJob(m.Job);
            RenderTools(m);
            DrawPreview();
            if (m.Stage.CardShown) AutomationProperties.SetName(CardImage, m.Stage.CardSays);
        }
        finally { _syncing = false; }
    }

    private static void SyncText(TextBox box, string value)
    {
        if (box.Text == value) return;
        var caret = box.CaretIndex;
        box.Text = value;
        box.CaretIndex = Math.Min(caret, value.Length);
    }

    /* ---- the step chips ------------------------------------------- */

    private void BuildStepChips()
    {
        for (var i = 0; i < Session.Steps.Length; i++)
        {
            var index = i;
            var chip = new Button { Style = Theme.Style("Step") };
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
            chip.Content = state.Label;
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
        // SAFETY: the three Edit buttons carry "0", "1" and "2" in the markup.
        var step = int.Parse((string)((FrameworkElement)sender).Tag, CultureInfo.InvariantCulture);
        var from = _session.StepIndex;
        _session.GoStep(step);
        AfterMove(from);
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

    /* ---- step 2: job title ---------------------------------------- */

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

    /* ---- step 3: number ------------------------------------------- */

    private void TxtSeq_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        _session.SetSeq(TxtSeq.Text);
        Render();
    }

    private void TxtSeq_LostFocus(object sender, RoutedEventArgs e) { _session.Touch(Field.Seq); Render(); }

    private void CmbYear_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || CmbYear.SelectedItem is not int year) return;
        _session.SetYear(year);
        Render();
    }

    private void ChkReissue_Click(object sender, RoutedEventArgs e) { _session.SetReissue(ChkReissue.IsChecked == true); Render(); }

    /* ---- step 4: photo -------------------------------------------- */

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
            // another program is holding the clipboard: select it so Ctrl+C still works
            TxtPrompt.Focus();
            TxtPrompt.SelectAll();
            CopyStatus.Text = "Selected. Press Ctrl+C to copy.";
        }
    }

    /* ---- step 5: signature ---------------------------------------- */

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
        if (_session.StepIndex == Session.LastStep) { _sheetTimer.Stop(); _sheetTimer.Start(); return; }

        // as many pixels as the card really occupies on this monitor
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var onScreen = CardBox.ActualWidth > 0 ? CardBox.ActualWidth : CardSurface.Width;
        var ppm = Math.Clamp(onScreen * dpi / CardConfig.CardWidthMm, 4, CardConfig.PrintPxPerMm);
        CardImage.Source = _front
            ? _renderer.RenderFront(_art, _session.Geometry, _session.CardData(), ppm, _guides)
            : _renderer.RenderBack(_art, ppm, _session.RotateBack);
    }

    private const double SheetPreviewPpm = 96 / 25.4;

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
        Show(SheetNav, sheet.NavShown);
        PrevPage.IsEnabled = sheet.PreviousEnabled;
        NextPage.IsEnabled = sheet.NextEnabled;
    }

    private void Side_Click(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _front = SideFront.IsChecked == true;
        Render();
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e) { _session.TurnPreviewPage(-1); DrawSheetPreview(); }
    private void NextPage_Click(object sender, RoutedEventArgs e) { _session.TurnPreviewPage(1); DrawSheetPreview(); }
}
