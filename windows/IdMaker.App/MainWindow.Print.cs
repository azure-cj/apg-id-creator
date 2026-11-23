// The last step and the four tools: checking and saving the print, batches
// from a list, the register of issued IDs, the printer setting and the card
// design.

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using IdMaker.Core;
using Microsoft.Win32;

namespace IdMaker.App;

public partial class MainWindow
{
    /* ---- step 6: check and print ---------------------------------- */

    private void RenderCheckStep(CheckView v)
    {
        var s = _session;
        if (s.StepIndex != Session.LastStep) return;

        Show(ReviewStage, !v.Saved);
        Show(SavedStage, v.Saved);
        SetText(StepCheckHeading, v.Heading);
        Show(ReviewSub, v.SubShown);
        SetText(ReviewSub, v.Sub);

        // a plain summary of the card on screen, each line one press from being edited
        Show(Summary, v.SummaryShown);
        if (v.SummaryShown && _art is not null && _renderer is not null)
        {
            SummaryImage.Source = _renderer.RenderFront(_art, s.Geometry, s.CardData(), 132 * 2 / CardConfig.CardWidthMm);
            SetText(SumName, v.SummaryName);
            SetText(SumTitle, v.SummaryTitle);
            SetText(SumNumber, v.SummaryNumber);
        }
        // the warning is a mark with a few words; the whole of it opens on hover or focus
        Show(ReviewWarning, v.WarningShown);
        if (v.WarningShown && !Equals(System.Windows.Automation.AutomationProperties.GetHelpText(ReviewWarning), v.Warning))
        {
            ReviewWarning.ToolTip = Note(v.Warning);
            System.Windows.Automation.AutomationProperties.SetHelpText(ReviewWarning, v.Warning);
        }

        LayPairs.IsChecked = s.Layout == SheetMode.Pairs;
        LayDuplex.IsChecked = s.Layout == SheetMode.Duplex;
        SlideThumb(LayoutThumb, LayoutChoices, s.Layout == SheetMode.Duplex ? 1 : 0, glide: true);
        // SAFETY: CmbPaper's items are made in the constructor, each with its paper's key as a string Tag.
        foreach (var item in CmbPaper.Items.OfType<ComboBoxItem>()) if ((string)item.Tag == s.Paper && !item.IsSelected) item.IsSelected = true;

        if (v.HasPlan)
        {
            if (!TxtCopies.IsKeyboardFocusWithin) SyncText(TxtCopies, v.CopiesText);
            CopiesDown.IsEnabled = v.CopiesDownEnabled;
            CopiesUp.IsEnabled = v.CopiesUpEnabled;
            SetText(CopiesLabel, v.CopiesLabel);
            SetText(PrintNote, v.PrintNote);
        }
        SetText(ReviewFile, v.FileLine);
        BtnDownload.IsEnabled = v.SaveEnabled;
        // "Save" and a small arrow, as a menu button; while the PDF is written, Core's words ("Saving the PDF…")
        BtnDownload.Content = _saving ? v.SaveTitle : SaveLabel;
        MenuPdf.IsEnabled = v.SaveEnabled;
        MenuPng.IsEnabled = v.PicturesEnabled;
        BtnAddAnother.IsEnabled = v.AddAnotherEnabled;
    }

    private bool _saving;
    private int _savedCount;

    private void Layout_Click(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        var layout = LayDuplex.IsChecked == true ? SheetMode.Duplex : SheetMode.Pairs;
        _sheetChanged = layout != _session.Layout;
        _session.SetLayout(layout);
        Render();
    }

    private void CmbPaper_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || CmbPaper.SelectedItem is not ComboBoxItem { Tag: string key }) return;
        _session.SetPaper(key);
        Render();
    }

    private void TxtCopies_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        _session.SetCopies(int.TryParse(TxtCopies.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null);
        Render();
    }

    private void TxtCopies_LostFocus(object sender, RoutedEventArgs e) => Render();     // show what was understood
    private void CopiesDown_Click(object sender, RoutedEventArgs e) { _session.NudgeCopies(-1); Render(); }
    private void CopiesUp_Click(object sender, RoutedEventArgs e) { _session.NudgeCopies(1); Render(); }

    private void BtnAddAnother_Click(object sender, RoutedEventArgs e)
    {
        var from = _session.StepIndex;
        _session.AddAnother();
        AfterMove(from);
    }

    private void BtnCancelAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_session.StartedCard && !Confirm("Discard the ID you have started? The cards already in this print are kept.", "Discard")) return;
        var from = _session.StepIndex;
        _session.CancelAdding();
        AfterMove(from);
    }

    private void RenderJob(JobView v)
    {
        Show(QueueStrip, v.Shown);
        if (QueueStrip.Visibility != Visibility.Visible) return;

        if (v.Key != _jobShown)
        {
            _jobShown = v.Key;
            JobList.Children.Clear();
            for (var i = 0; i < v.Rows.Count; i++)
            {
                var c = v.Rows[i];
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                if (c.Current)
                {
                    var tag = new TextBlock { Text = "on screen", FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
                    Theme.Paint(tag, TextBlock.ForegroundProperty, "Color.StageMuted");
                    DockPanel.SetDock(tag, Dock.Right);
                    row.Children.Add(tag);
                }
                else
                {
                    var index = i;
                    var remove = new Button { Style = Theme.Style("Button.Ghost"), Content = "Remove", MinHeight = 32, Padding = new Thickness(10, 4, 10, 4), FontSize = 13 };
                    System.Windows.Automation.AutomationProperties.SetName(remove, c.RemoveSays);
                    remove.Click += (_, _) => { var from = _session.StepIndex; _session.RemoveFromQueue(index); AfterMove(from); };
                    DockPanel.SetDock(remove, Dock.Right);
                    row.Children.Add(remove);
                }
                var who = new TextBlock
                {
                    Text = c.Text, FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = c.ToolTip,
                };
                Theme.Paint(who, TextBlock.ForegroundProperty, "Color.StageInk");
                row.Children.Add(who);
                JobList.Children.Add(row);
            }
        }
        SetText(JobCount, v.Count);
        SetText(JobNote, v.Note);
        Show(JobNote, v.NoteShown);
        Show(BtnCancelAdd, v.CancelAddShown);
    }

    private string _jobShown = "";

    private static string DocumentsFolder => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    private StackPanel? _saveLabel;
    private StackPanel SaveLabel => _saveLabel ??= MakeSaveLabel();

    private static StackPanel MakeSaveLabel()
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        label.Children.Add(new TextBlock { Text = "Save", VerticalAlignment = VerticalAlignment.Center });
        var arrow = new System.Windows.Shapes.Path
        {
            Data = System.Windows.Media.Geometry.Parse("M0,5 L5,0 L10,5"), StrokeThickness = 1.6, Width = 10, Height = 6,
            Margin = new Thickness(10, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        // the arrow points up: the choices open above the button, at the foot of the form
        arrow.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Color.OnAccent");
        label.Children.Add(arrow);
        return label;
    }

    // Save opens its two choices just above itself, as wide as it at least
    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        SaveMenu.PlacementTarget = BtnDownload;
        SaveMenu.MinWidth = BtnDownload.ActualWidth;
        SaveMenu.IsOpen = true;
    }

    private void BtnDownload_Click(object sender, RoutedEventArgs e)
    {
        var (fileName, _) = _session.PrintFile();
        var dialog = new SaveFileDialog { Title = "Save the print file", FileName = fileName, Filter = "PDF|*.pdf", DefaultExt = ".pdf", InitialDirectory = DocumentsFolder };
        if (dialog.ShowDialog(this) == true) _ = SavePrint(dialog.FileName);
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
        Show(ReviewError, false);
        Render();
        // Core writes the PDF, and only once it is whole issues its cards (Session.SavePrint)
        var saved = await _session.SavePrint(path, job, OnWindow(pages));
        _saving = false;
        if (saved.Error.Length > 0)
        {
            ReviewError.Text = saved.Error;
            Show(ReviewError, true);
            Render();
            return;
        }

        _saved = true;
        _savedCount = saved.Cards;
        _savedPath = path;
        SavedName.Text = Path.GetFileName(path);
        Show(SavedNote, false);
        Render();
        BtnNewId.Focus();

        // the shared register has been told meanwhile
        if (await saved.Trouble is { Length: > 0 } trouble)
        {
            SavedNote.Text = trouble;
            Show(SavedNote, true);
            Render();
        }
    }

    /// <summary>
    /// Pages are drawn here, on the window's own thread, one at a time as
    /// the writer asks for them; the compressing and writing happen off it
    /// (PrintFiles.Write), so the window stays alive through a long batch.
    /// </summary>
    private List<Func<PdfPage>> OnWindow(List<Func<PdfPage>> pages) =>
        pages.Select(p => (Func<PdfPage>)(() => Dispatcher.Invoke(p, DispatcherPriority.Background))).ToList();

    private void BtnPng_Click(object sender, RoutedEventArgs e)
    {
        if (_art is null || _renderer is null || !_session.Ready()) return;
        var stem = Path.GetFileNameWithoutExtension(_session.PrintFile().FileName);
        var dialog = new SaveFileDialog { Title = "Save the front as a picture", FileName = stem + "-front.png", Filter = "PNG picture|*.png", DefaultExt = ".png", InitialDirectory = DocumentsFolder };
        if (dialog.ShowDialog(this) != true) return;
        var front = dialog.FileName;
        var back = PrintFiles.BackPictureFor(front, DocumentsFolder);
        try
        {
            var ppm = CardConfig.PrintPxPerMm;
            ImageFiles.SavePng(_renderer.RenderFront(_art, _session.Geometry, _session.CardData(), ppm), front);
            ImageFiles.SavePng(_renderer.RenderBack(_art, ppm, _session.RotateBack), back);
            // pictures are a proof to look at, not a print: the card is issued when the PDF is saved
            SetText(ReviewFile, PrintFiles.PicturesSaved(front, back));
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ReviewError.Text = PrintFiles.PicturesNotSaved(x);
            Show(ReviewError, true);
        }
    }

    private void BtnNewId_Click(object sender, RoutedEventArgs e)
    {
        var from = _session.StepIndex;
        _saved = false;
        _session.NewId();
        AfterMove(from);
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e) => ShowInExplorer(_savedPath);

    // Saving a file silently would leave the operator with no idea where the print went.
    private static void ShowInExplorer(string path)
    {
        if (!File.Exists(path)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { /* the file is saved either way */ }
    }

    /* ---- the tool rail --------------------------------------------
       The register, batches, the printer setting and the card design are
       occasional, and they belong beside the work rather than under it.
       One opens at a time.
       ---------------------------------------------------------------- */

    private void Rail_Click(object sender, RoutedEventArgs e)
    {
        // SAFETY: the four rail buttons carry their tool's name as their Tag in the markup.
        var name = (string)((FrameworkElement)sender).Tag;
        _tool = _tool == name ? null : name;
        if (_tool == ToolKey.Register) RegisterUrlMsg.Text = "";
        Render();
    }

    // a click on the dimmed card puts the tool away
    private void Veil_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        CloseTool();
    }

    private void DrawerClose_Click(object sender, RoutedEventArgs e) => CloseTool();

    private void CloseTool()
    {
        var was = _tool;
        _tool = null;
        Render();
        // focus goes back to the button that opened the drawer
        (was switch { ToolKey.Register => RailRegister, ToolKey.Batch => RailBatch, ToolKey.Printer => RailPrinter, ToolKey.Design => RailDesign, _ => null })?.Focus();
    }

    private void RenderTools(Screen m)
    {
        // the design can only be changed before an ID is started: Core takes
        // its button away and shuts its drawer once one is
        Show(RailDesign, m.Tools.DesignOffered);
        _tool = m.Tools.Open;

        // Editing the map and having the Card design drawer open are the
        // same state, and this is where that is enforced rather than in each
        // of the handlers that can close it. The Keep button lives in that
        // drawer, so any route that closed the drawer without going through
        // it — Close, Escape, another tool in the rail — would otherwise
        // leave the boxes on the card with no button anywhere to finish with.
        if (EditingMap && _tool != ToolKey.Design) StopAdjusting();

        var opening = _tool is not null && (Drawer.Visibility != Visibility.Visible || _toolShown != _tool || _closingTool);
        // A tool that was just put away slides out still showing what it showed,
        // and only then leaves the page (Motion.SlideOut); the rail lets go of it at once.
        var closing = _tool is null && Drawer.Visibility == Visibility.Visible;
        var panel = _tool ?? (closing ? _toolShown : null);
        if (_tool is not null) { _toolShown = _tool; _closingTool = false; Show(Drawer, true); }
        if (_tool is not null) SetText(DrawerTitle, m.Tools.DrawerTitle);
        Show(ToolRegister, panel == ToolKey.Register);
        Show(ToolBatch, panel == ToolKey.Batch);
        Show(ToolPrinter, panel == ToolKey.Printer);
        Show(ToolDesign, panel == ToolKey.Design);
        RailRegister.IsChecked = _tool == ToolKey.Register;
        RailBatch.IsChecked = _tool == ToolKey.Batch;
        RailPrinter.IsChecked = _tool == ToolKey.Printer;
        RailDesign.IsChecked = _tool == ToolKey.Design;

        // a badge on the rail, and nothing at all when there is nothing to count
        SetText(RailRegisterCount, m.Tools.Issued.ToString(CultureInfo.InvariantCulture));
        Show(RailRegisterBadge, m.Tools.Issued > 0);

        if (m.Register is { } register) RenderRegister(register);
        if (_tool == ToolKey.Printer)
        {
            // SAFETY: CmbFlip's two items carry "long" and "short" as string Tags in the markup.
            foreach (var item in CmbFlip.Items.OfType<ComboBoxItem>())
                item.IsSelected = (string)item.Tag == (_session.Flip == DuplexFlip.ShortEdge ? "short" : "long");
            ChkRotate.IsChecked = _session.RotateBack;
        }
        if (_tool == ToolKey.Design) RenderDesign();
        // the form stands down while the boxes are being dragged
        FormPanel.IsEnabled = !EditingMap;
        FitColumns();
        // a tool that has just opened slides in from the rail's side, and the card dims behind it
        if (opening)
        {
            Motion.SlideIn(Drawer, -24);
            if (Veil.Visibility == Visibility.Visible) { Veil.Opacity = 0; Motion.Fade(Veil, VeilOpacity); }
        }
        if (closing && !_closingTool) PutToolAway();
    }

    private string? _toolShown;
    private bool _closingTool;                  // the drawer is sliding out
    private const double VeilOpacity = 0.72;

    private void PutToolAway()
    {
        // beside the card (while the map was dragged) it simply goes: sliding there would cross the card
        if (Grid.GetColumn(Drawer) == 1) { Show(Drawer, false); Show(Veil, false); return; }
        _closingTool = true;
        Motion.Fade(Veil, 0, 150, () => { if (_tool is null) Show(Veil, false); });
        Motion.SlideOut(Drawer, -24, () =>
        {
            // a tool opened again meanwhile keeps the drawer
            if (_tool is not null || !_closingTool) return;
            _closingTool = false;
            Show(Drawer, false);
        });
    }

    // What the four columns need side by side while the map is dragged: the
    // rail, the tool, the least the card can be shown in, and the form.
    private const double RoomForAllFour = 176 + 12 + 340 + 12 + 300 + 12 + 440;

    /// <summary>
    /// A tool opens over the card, on a veil that dims it, so nothing else on
    /// the page moves or changes size. While the map is being dragged the
    /// tool stands beside the card instead, so every box can be reached;
    /// then, in a window too narrow for all four columns, it takes the form's
    /// place until it is closed. The card is never the one to give way.
    /// </summary>
    private void FitColumns()
    {
        var beside = _tool is not null && EditingMap;
        Grid.SetColumn(Drawer, beside ? 1 : 2);
        Drawer.Margin = beside ? new Thickness(0, 0, 12, 0) : new Thickness(12);
        Drawer.HorizontalAlignment = beside ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        // the veil goes with the tool; while the tool slides out, PutToolAway fades it
        if (_tool is not null) Show(Veil, !beside);
        else if (!_closingTool && Drawer.Visibility != Visibility.Visible) Show(Veil, false);
        var crowded = beside && Columns.ActualWidth > 0 && Columns.ActualWidth < RoomForAllFour;
        Show(FormPanel, !crowded);
    }

    /* ---- issued IDs ----------------------------------------------- */

    private string _registerShown = "\u0000";
    private bool _registerOpen;                 // Issued IDs shows every row, not only the latest three

    private void BtnRegisterFold_Click(object sender, RoutedEventArgs e)
    {
        _registerOpen = !_registerOpen;
        Render();
        if (!_registerOpen) RegisterScroll.ScrollToTop();
    }

    /// <summary>The issued IDs, who is signed in to the shared register on this computer, and what the register has refused.</summary>
    private void RenderRegister(RegisterView v)
    {
        BtnRegisterCsv.IsEnabled = v.ExportEnabled;
        SetText(RegisterAccount, v.Account);
        Show(SignInForm, v.SignInShown);
        Show(SignedInActions, v.SignedInShown);
        Show(RefusedPanel, v.RefusedShown);
        SetText(RefusedNote, v.RefusedNote);
        SetText(RegisterNote, v.Note);
        // Core says how many show and what the button under them says (Screen.IssuedFold)
        var (shown, toggle) = Screen.IssuedFold(v.Rows.Count, _registerOpen);
        var key = v.Key + "|" + shown;
        if (key == _registerShown) return;
        _registerShown = key;
        RegisterList.Children.Clear();
        BtnRegisterFold.Content = toggle;
        Show(BtnRegisterFold, toggle.Length > 0);
        RegisterScroll.MaxHeight = _registerOpen ? 300 : double.PositiveInfinity;
        RegisterScroll.Margin = new Thickness(0, 0, _registerOpen ? -10 : 0, 0);
        // a table on the gold wash (RegisterTable): the number and its day, then who it is for
        Show(RegisterTable, v.Rows.Count > 0);
        for (var i = 0; i < shown; i++)
        {
            var e = v.Rows[i];
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var number = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            number.Children.Add(new TextBlock { Text = e.Head, Style = Theme.Style("Text.Body"), FontWeight = FontWeights.SemiBold });
            number.Children.Add(new TextBlock { Text = e.When, Style = Theme.Style("Text.Note") });
            // the name, and the position under it in the quieter colour: one line each reads at a glance
            var who = new StackPanel();
            var split = e.Detail.IndexOf(" · ", StringComparison.Ordinal);
            who.Children.Add(new TextBlock { Text = split < 0 ? e.Detail : e.Detail[..split], Style = Theme.Style("Text.Body") });
            if (split >= 0) who.Children.Add(new TextBlock { Text = e.Detail[(split + 3)..], Style = Theme.Style("Text.Note") });
            Grid.SetColumn(who, 1);
            row.Children.Add(number);
            row.Children.Add(who);
            // a white rule between rows on the wash, as in the Batch list; none under the last
            var line = new Border { BorderThickness = new Thickness(0, 0, 0, i < shown - 1 ? 2 : 0), Padding = new Thickness(0, 8, 0, 8), Child = row };
            Theme.Paint(line, Border.BorderBrushProperty, "Color.Surface");
            RegisterList.Children.Add(line);
        }
        if (_registerOpen && v.More.Length > 0) RegisterList.Children.Add(new TextBlock { Text = v.More, Style = Theme.Style("Text.Note"), Margin = new Thickness(0, 0, 0, 8) });
    }

    private void BtnRegisterCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save the list of issued IDs", FileName = $"apg-id-register-{DateTime.Now:yyyy-MM-dd}.csv", Filter = "CSV|*.csv", DefaultExt = ".csv", InitialDirectory = DocumentsFolder,
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, _session.RegisterCsv());
            RegisterUrlMsg.Text = $"Saved {Path.GetFileName(dialog.FileName)}.";
            ShowInExplorer(dialog.FileName);
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException)
        {
            RegisterUrlMsg.Text = $"The list couldn’t be saved there ({x.Message.TrimEnd('.')}).";
        }
    }

    private void BtnRegisterImport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose a saved list of issued IDs", Filter = "CSV|*.csv" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            RegisterUrlMsg.Text = _session.ImportRegister(File.ReadAllText(dialog.FileName));
            Render();
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException)
        {
            RegisterUrlMsg.Text = $"That file couldn’t be read ({x.Message.TrimEnd('.')}).";
        }
    }

    private void BtnRefusedDone_Click(object sender, RoutedEventArgs e)
    {
        _session.ClearRefused();
        Render();
    }

    private void TxtSignInPassword_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        e.Handled = true;
        BtnSignIn_Click(sender, e);
    }

    private async void BtnSignIn_Click(object sender, RoutedEventArgs e)
    {
        if (!BtnSignIn.IsEnabled) return;
        BtnSignIn.IsEnabled = false;
        RegisterUrlMsg.Text = "Signing in…";
        try
        {
            // Core signs in, reads the register and sends the cards saved before anyone signed in (Session.SignIn)
            var signingIn = _session.SignIn(TxtSignInEmail.Text, TxtSignInPassword.Password);
            // the password is not kept anywhere, this box included: it has been handed over
            TxtSignInPassword.Clear();
            var result = await signingIn;
            RegisterUrlMsg.Text = result.Message;
            if (!result.Ok)
            {
                TxtSignInPassword.Focus();
                return;
            }
            Render();
        }
        finally { BtnSignIn.IsEnabled = true; }
    }

    private void BtnSignOut_Click(object sender, RoutedEventArgs e)
    {
        RegisterUrlMsg.Text = _session.SignOut();
        Render();
    }

    private async void BtnRegisterTest_Click(object sender, RoutedEventArgs e)
    {
        BtnRegisterTest.IsEnabled = false;
        RegisterUrlMsg.Text = "Asking the register…";
        try
        {
            RegisterUrlMsg.Text = await _session.CheckRegister();
            Render();
        }
        finally { BtnRegisterTest.IsEnabled = true; }
    }

    /* ---- printer setting ------------------------------------------ */

    private void CmbFlip_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || CmbFlip.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        _session.SetFlip(tag == "short" ? DuplexFlip.ShortEdge : DuplexFlip.LongEdge);
        Render();
    }

    private void ChkRotate_Click(object sender, RoutedEventArgs e) { _session.SetRotateBack(ChkRotate.IsChecked == true); Render(); }

    /* ---- batch from CSV ------------------------------------------- */

    private string? _batchCsv;
    private string[] _batchPhotos = Array.Empty<string>();
    private string[] _batchSigs = Array.Empty<string>();
    private bool _batchRunning;

    private void UpdateBatchButton()
    {
        BtnBatch.IsEnabled = !_batchRunning && _batchCsv is not null && _batchPhotos.Length > 0 && _art is not null;
        var chosen = Screen.BatchChosen(_batchCsv, _batchPhotos.Length, _batchSigs.Length);
        SetText(CsvMsg, chosen.List);
        SetText(PhotosMsg, chosen.Photos);
        // "Optional" stands out in gold at the start of the line; the rest stays quiet
        const string optional = "Optional";
        if (chosen.Signatures.StartsWith(optional, StringComparison.Ordinal) && SigsMsg.Text != chosen.Signatures)
        {
            SigsMsg.Inlines.Clear();
            var word = new System.Windows.Documents.Run(optional) { FontWeight = FontWeights.SemiBold };
            word.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Color.Kicker");
            SigsMsg.Inlines.Add(word);
            SigsMsg.Inlines.Add(new System.Windows.Documents.Run(chosen.Signatures[optional.Length..]));
        }
        else if (!chosen.Signatures.StartsWith(optional, StringComparison.Ordinal)) SetText(SigsMsg, chosen.Signatures);
        // once a file is chosen its number becomes a tick, and its button offers to change it
        BatchMark(CsvMark, BtnPickCsv, _batchCsv is not null);
        BatchMark(PhotosMark, BtnPickPhotos, _batchPhotos.Length > 0);
        BatchMark(SigsMark, BtnPickSigs, _batchSigs.Length > 0);
    }

    private static void BatchMark(Border mark, Button pick, bool chosen)
    {
        var say = chosen ? "Change" : "Choose";
        if (mark.Child is not null && Equals(pick.Content, say)) return;        // drawn already
        // a white ring with its number, or a disc of the text colour with a tick
        Theme.Paint(mark, Border.BackgroundProperty, chosen ? "Color.Strong" : "Color.Surface");
        Theme.Paint(mark, Border.BorderBrushProperty, chosen ? "Color.Strong" : "Color.ControlBorder");
        if (chosen)
        {
            var tick = new System.Windows.Shapes.Path
            {
                Data = System.Windows.Media.Geometry.Parse("M1,6 L5,10 L12,2"), StrokeThickness = 2,
                Width = 13, Height = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            tick.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Color.OnStrong");
            mark.Child = tick;
        }
        else
        {
            // SAFETY: each mark carries its row's number as a string Tag in the markup.
            var number = new TextBlock { Text = (string)mark.Tag, FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Theme.Paint(number, TextBlock.ForegroundProperty, "Color.TextMuted");
            mark.Child = number;
        }
        pick.Content = say;
        Motion.FadeIn(mark, 0.3);
    }

    private void BtnPickCsv_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose the list", Filter = "CSV|*.csv" };
        if (dialog.ShowDialog(this) == true) { _batchCsv = dialog.FileName; UpdateBatchButton(); }
    }

    private void BtnPickPhotos_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose the photos", Filter = PictureFilter, Multiselect = true };
        if (dialog.ShowDialog(this) == true) { _batchPhotos = dialog.FileNames; UpdateBatchButton(); }
    }

    private void BtnPickSigs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose the scanned signatures", Filter = PictureFilter, Multiselect = true };
        if (dialog.ShowDialog(this) == true) { _batchSigs = dialog.FileNames; UpdateBatchButton(); }
    }

    private void BtnCsvTemplate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Save the example list", FileName = "apg-id-batch-template.csv", Filter = "CSV|*.csv", DefaultExt = ".csv", InitialDirectory = DocumentsFolder };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, Batch.CsvTemplate); BatchStatus.Text = $"Saved {Path.GetFileName(dialog.FileName)}. Fill it in with Excel and save it as CSV."; }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException) { BatchStatus.Text = $"The example couldn’t be saved there ({x.Message.TrimEnd('.')})."; }
    }

    private string BatchLayoutChoice => CmbBatchLayout.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : Batch.LayoutDuplex;
    private string BatchPaperChoice => CmbBatchPaper.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : CardConfig.DefaultPaper;

    private void BatchLayout_Changed(object sender, SelectionChangedEventArgs e) => RenderBatchLayout();

    private void RenderBatchLayout()
    {
        if (LayoutMsg is null || BatchPaperField is null || CmbBatchPaper is null) return;      // the markup is still loading
        var layout = BatchLayoutChoice;
        Show(BatchPaperField, layout != Batch.LayoutCards);
        LayoutMsg.Text = Screen.BatchLayoutLine(layout, BatchPaperChoice);
    }

    private void BtnBatch_Click(object sender, RoutedEventArgs e)
    {
        var sheets = BatchLayoutChoice != Batch.LayoutCards;
        var dialog = new SaveFileDialog
        {
            Title = "Save the batch", FileName = Batch.FileFor(1, sheets, DateTime.Now).FileName, Filter = "PDF|*.pdf", DefaultExt = ".pdf", InitialDirectory = DocumentsFolder,
        };
        if (dialog.ShowDialog(this) == true) _ = RunBatch(dialog.FileName);
    }

    internal async Task RunBatch(string savePath)
    {
        if (_batchRunning || _batchCsv is null || _art is null || _renderer is null) return;
        _batchRunning = true;
        UpdateBatchButton();
        BatchReport.Children.Clear();
        Show(BatchResult, false);
        BatchStatus.Text = "Reading the list…";
        try
        {
            string csv;
            try { csv = await File.ReadAllTextAsync(_batchCsv); }
            catch (Exception x) when (x is IOException or UnauthorizedAccessException) { BatchStatus.Text = $"The list couldn’t be read ({x.Message.TrimEnd('.')})."; return; }

            // duplicates across computers are only caught if the shared register has been read
            if (!await _session.ReadSharedRegister())
                BatchStatus.Text = "The shared register could not be reached, so numbers are checked against this computer only.";

            var geometry = _session.Geometry;
            // its own measurer: the batch runs off the window's thread, and the window keeps drawing with the other one
            var services = _session.BatchServices(WpfTextMeasurer.Load(BesideProgram("Fonts")), ImageFiles.Decode, _remover);
            var photos = _batchPhotos;
            var sigs = _batchSigs;
            var outcome = await Task.Run(() => Batch.Run(csv, photos, sigs, services,
                (i, n) => Dispatcher.BeginInvoke(() => BatchStatus.Text = $"Checking {i} of {n}…")));

            RenderBatchReport(outcome.Results);
            if (outcome.Error.Length > 0) { BatchStatus.Text = outcome.Error; return; }

            var good = outcome.Good;
            // SAFETY: Batch.PrepareRow gives every row that is Ok its Data and its Entry.
            var entries = good.Select(r => r.Data!).ToList();
            var layout = BatchLayoutChoice;
            List<Func<PdfPage>> pages;
            if (layout == Batch.LayoutCards) pages = _renderer.CardPdfPages(_art, geometry, entries, _session.RotateBack);
            else
            {
                // SAFETY: Sheets.Plan falls back to the default paper for a name it does not know, and one card fits every paper in CardConfig.Papers.
                var plan = Sheets.Plan(BatchPaperChoice, layout == Batch.LayoutPairs ? SheetMode.Pairs : SheetMode.Duplex)!;
                pages = _renderer.SheetPdfPages(_art, geometry, plan, entries, Sheets.Pages(entries.Count, plan, _session.Flip), _session.BackUpsideDown(plan));
            }
            var (_, title) = Batch.FileFor(good.Count, layout != Batch.LayoutCards, DateTime.Now);
            var error = await PrintFiles.Write(savePath, OnWindow(pages), title, DateTime.Now,
                (done, total) => Dispatcher.BeginInvoke(() => BatchStatus.Text = $"Building the PDF: page {done} of {total}…"));
            if (error.Length > 0) { BatchStatus.Text = error; return; }

            // only the rows that made it into the PDF are issued
            // SAFETY: as for Data above.
            var issued = good.Select(r => r.Entry!).ToList();
            // here at once; the shared register is told while the result is put on screen (Session.IssueBatch)
            var telling = _session.IssueBatch(issued);
            Render();
            var skipped = outcome.Results.Count - good.Count;
            BatchStatus.Text = $"{good.Count} card{(good.Count == 1 ? "" : "s")} saved as {Path.GetFileName(savePath)}"
                + (skipped > 0 ? $" · {skipped} row{(skipped == 1 ? "" : "s")} skipped, see below." : ". Print at 100%.");
            ShowInExplorer(savePath);

            if (await telling is { Length: > 0 } trouble)
            {
                BatchStatus.Text += " " + trouble;
                Render();
            }
        }
        finally
        {
            _batchRunning = false;
            UpdateBatchButton();
        }
    }

    private void RenderBatchReport(IReadOnlyList<BatchRowResult> results)
    {
        // Core counts and orders them (Screen.BatchSummary); this lays them out
        var sum = Screen.BatchSummary(results);
        BatchCounts.Children.Clear();
        BatchCounts.Children.Add(CountChip($"{sum.Made} made", "Color.Success"));
        if (sum.ToCheck > 0) BatchCounts.Children.Add(CountChip($"{sum.ToCheck} to check", "Color.Warning"));
        if (sum.Skipped > 0) BatchCounts.Children.Add(CountChip($"{sum.Skipped} skipped", "Color.Danger"));
        BatchDetailsLabel.Text = $"Each row ({results.Count})";
        BatchDetails.IsExpanded = false;          // folded: the counts say enough until a row is wanted

        BatchReport.Children.Clear();
        for (var i = 0; i < sum.Rows.Count; i++)
        {
            var (tone, head, text) = Screen.BatchRow(sum.Rows[i]);
            var row = new StackPanel();
            row.Children.Add(new TextBlock { Text = head, Style = Theme.Style("Text.Body"), FontWeight = FontWeights.SemiBold });
            // a coloured dot and the words: the words say which it is; the colour only agrees with them
            var status = new DockPanel();
            var dot = StatusDot(tone == Tone.Note ? "Color.Success" : tone == Tone.Warning ? "Color.Warning" : "Color.Danger");
            dot.Margin = new Thickness(0, 6, 8, 0);
            dot.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(dot, Dock.Left);
            status.Children.Add(dot);
            status.Children.Add(new TextBlock { Text = text, Style = Theme.Style(TextStyle(tone)) });
            row.Children.Add(status);
            var line = new Border { BorderThickness = new Thickness(0, 0, 0, i < sum.Rows.Count - 1 ? 2 : 0), Padding = new Thickness(0, 8, 0, 8), Child = row };
            Theme.Paint(line, Border.BorderBrushProperty, "Color.Surface");
            BatchReport.Children.Add(line);
        }
        Show(BatchResult, results.Count > 0);
    }

    // the rows just opened are brought into the panel's view, rather than left below its edge
    private void BatchDetails_Expanded(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(() => BatchDetails.BringIntoView(), DispatcherPriority.Loaded);

    // One count in the batch's outcome: a mark and a few words in a rounded outline, in the colour the words already say.
    // one count: a coloured dot and the words (a status is a dot and a word, never a capsule), counts in even-width figures
    private static FrameworkElement CountChip(string words, string colour)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 6) };
        var dot = StatusDot(colour);
        dot.Margin = new Thickness(0, 0, 7, 0);
        row.Children.Add(dot);
        var text = new TextBlock { Text = words, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Documents.Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
        Theme.Paint(text, TextBlock.ForegroundProperty, colour);
        row.Children.Add(text);
        return row;
    }

    private static System.Windows.Shapes.Ellipse StatusDot(string colour)
    {
        var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, colour);
        return dot;
    }

    /* ---- the card design -------------------------------------------
       Moving the map, or swapping the artwork, changes where everything on
       the card belongs, so both are offered on the first step of a blank
       card and nowhere else: once a photo has been placed against one map,
       the placement is already wrong for another.
       ---------------------------------------------------------------- */

    private string? _frontFile;
    private string? _backFile;

    // Asked of the overlay's own setting rather than of what is on screen:
    // "is it drawn right now" is also false while the window is hidden or minimised.
    private bool EditingMap => MapOverlay.Visibility == Visibility.Visible;

    private void RenderDesign()
    {
        var editing = EditingMap;
        DesignName.Text = _templates.DesignName;
        Show(DesignHome, !editing);
        Show(DesignAdjust, editing);
        Show(BtnTplReset, _templates.IsCustom && !editing);
        BtnTplUse.IsEnabled = _frontFile is not null && _backFile is not null;
        SetText(FrontFileMsg, _frontFile is null ? "No front chosen." : Path.GetFileName(_frontFile));
        SetText(BackFileMsg, _backFile is null ? "No back chosen." : Path.GetFileName(_backFile));
        if (editing) SetText(DesignReadout, MapOverlay.Readout());
    }

    private void BtnPickFront_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose the front artwork", Filter = PictureFilter };
        if (dialog.ShowDialog(this) == true) { _frontFile = dialog.FileName; Render(); }
    }

    private void BtnPickBack_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose the back artwork", Filter = PictureFilter };
        if (dialog.ShowDialog(this) == true) { _backFile = dialog.FileName; Render(); }
    }

    private void BtnTplUse_Click(object sender, RoutedEventArgs e)
    {
        if (_frontFile is not null && _backFile is not null) UseArtwork(_frontFile, _backFile);
    }

    internal void UseArtwork(string frontPath, string backPath)
    {
        try
        {
            var front = ImageFiles.Load(frontPath);
            var back = ImageFiles.Load(backPath);
            foreach (var (side, picture) in new[] { ("front", front), ("back", back) })
            {
                var problem = TemplateStore.ArtworkShapeProblem(side, picture.PixelWidth, picture.PixelHeight);
                if (problem.Length > 0) { DesignMsg.Text = problem; return; }
            }
            // stored at the printing size, so everything downstream agrees
            var art = new CardArt(
                ImageFiles.Resize(front, TemplateStore.ArtworkWidth, TemplateStore.ArtworkHeight),
                ImageFiles.Resize(back, TemplateStore.ArtworkWidth, TemplateStore.ArtworkHeight));
            Directory.CreateDirectory(_files.Directory);
            ImageFiles.SavePng(art.Front, StoredArtwork("front"));
            ImageFiles.SavePng(art.Back, StoredArtwork("back"));

            DesignMsg.Text = _design.Adopt(frontPath, (front.PixelWidth, front.PixelHeight), (back.PixelWidth, back.PixelHeight));
            _art = art;
            ShowContrastWarnings();
            // the map is wherever it was, which is unlikely to suit new artwork
            StartAdjusting();
        }
        catch (Exception x) when (x is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException or ArgumentException)
        {
            DesignMsg.Text = DesignDesk.ArtworkNotUsable(x);
        }
    }

    /// <summary>
    /// Text that will not read against the artwork. One sentence names the
    /// fields and the fix; the ratios are there for whoever wants to take
    /// them back to the designer.
    /// </summary>
    private void ShowContrastWarnings()
    {
        var warnings = _art is null ? Array.Empty<ContrastWarning>() : _templates.Warnings(CardRenderer.ToRgba(_art.Front));
        var (head, lines) = Screen.ContrastReport(warnings);
        Show(DesignWarn, head.Length > 0);
        DesignWarnList.Children.Clear();
        if (head.Length == 0) return;
        DesignWarnHead.Text = head;
        foreach (var line in lines) DesignWarnList.Children.Add(new TextBlock { Text = line, Style = Theme.Style("Text.Note") });
    }

    private void BtnTplMap_Click(object sender, RoutedEventArgs e) => StartAdjusting();

    internal void StartAdjusting()
    {
        // not once a card is started; and Core keeps the map as it is now, for Discard to put back
        if (!_design.Begin()) return;
        _front = true;
        _tool = ToolKey.Design;
        MapOverlay.Visibility = Visibility.Visible;
        MapOverlay.Refresh();
        Render();
    }

    // Leaving the editor by any route keeps the changes (DesignDesk.End).
    // Discard is right there for the other direction.
    private void StopAdjusting()
    {
        MapOverlay.Visibility = Visibility.Collapsed;
        _design.End();
    }

    private void BtnTplDone_Click(object sender, RoutedEventArgs e)
    {
        MapOverlay.Visibility = Visibility.Collapsed;
        var said = _design.Keep();
        ShowContrastWarnings();                             // the boxes may have been moved somewhere lighter
        DesignMsg.Text = said;
        Render();
        BtnTplMap.Focus();
    }

    private void BtnTplCancel_Click(object sender, RoutedEventArgs e)
    {
        MapOverlay.Visibility = Visibility.Collapsed;
        var said = _design.Discard();
        ShowContrastWarnings();
        DesignMsg.Text = said;
        Render();
        BtnTplMap.Focus();
    }

    private void BtnTplDefaults_Click(object sender, RoutedEventArgs e)
    {
        _design.ResetBoxes();
        MapOverlay.Refresh();
        Render();
    }

    private void BtnTplReset_Click(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Go back to the card the app was built with? The artwork and the map you set up here are removed from this computer.", "Go back")) return;
        MapOverlay.Visibility = Visibility.Collapsed;
        // the map and the stored artwork are removed from this computer (DesignDesk.BackToBuiltIn)
        var said = _design.BackToBuiltIn();
        _art = _builtInArt;
        _frontFile = _backFile = null;
        ShowContrastWarnings();
        DesignMsg.Text = said;
        Render();
    }
}
