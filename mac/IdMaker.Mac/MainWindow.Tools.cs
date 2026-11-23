// The rest of the tool rail: a batch from a list, the card design, moving
// the photo on the card itself, and the pen tablet driver. As in the rest
// of the window, the rules are in IdMaker.Core; this passes things on.

using IdMaker.Core;

namespace IdMaker.Mac;

public sealed partial class MainWindow
{
    private const double DrawerText = 280;
    private static NSTextField DrawerNote(string text = "") => Ui.Label(text, 12, muted: true, maxWidth: DrawerText);
    private static NSTextField DrawerHead(string text) => Ui.Label(text, 13, bold: true, maxWidth: DrawerText);

    /// <summary>Asks for several files to open. Replaced in the window test.</summary>
    internal Func<string, string[]> AskForFiles { get; set; } = title =>
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.Title = title;
        panel.CanChooseFiles = true;
        panel.CanChooseDirectories = false;
        panel.AllowsMultipleSelection = true;
        return panel.RunModal() == 1 ? panel.Urls.Select(u => u.Path).OfType<string>().ToArray() : Array.Empty<string>();
    };

    /* ---- batch from CSV ------------------------------------------- */

    private string? _batchCsv;
    private string[] _batchPhotos = Array.Empty<string>();
    private string[] _batchSigs = Array.Empty<string>();
    private bool _batchRunning;
    private string _batchStatus = "";
    private IReadOnlyList<BatchRowResult> _batchResults = Array.Empty<BatchRowResult>();
    private IReadOnlyList<BatchRowResult>? _batchReportShown;

    private readonly NSTextField _csvMsg = DrawerNote(), _photosMsg = DrawerNote(), _sigsMsg = DrawerNote(), _layoutMsg = DrawerNote(), _batchStatusLabel = DrawerNote();
    private readonly NSPopUpButton _cmbBatchLayout = Ui.Popup("Print layout", new[]
    {
        "Sheets, fronts then backs (print both sides)", "Sheets, front and back side by side (cut and stick)", "One card per page (card printer)",
    }, _ => { });
    private readonly NSPopUpButton _cmbBatchPaper = Ui.Popup("Paper", CardConfig.Papers.Select(p => p.Label), _ => { });
    private readonly NSStackView _batchPaperField = Ui.Column(4);
    private readonly NSButton _btnBatch = Ui.Button("Make the batch", () => { });
    private readonly NSStackView _batchReport = Ui.Column(8);

    private NSStackView BuildBatchTool()
    {
        _cmbBatchLayout.Activated += (_, _) => Render();
        _cmbBatchPaper.Activated += (_, _) => Render();
        _cmbBatchPaper.SelectItem(Math.Max(0, Array.FindIndex(CardConfig.Papers, p => p.Key == CardConfig.DefaultPaper)));
        _cmbBatchLayout.WidthAnchor.ConstraintLessThanOrEqualTo((nfloat)DrawerText).Active = true;
        _batchPaperField.AddArrangedSubview(DrawerHead("Paper"));
        _batchPaperField.AddArrangedSubview(_cmbBatchPaper);
        _btnBatch.Activated += (_, _) => ChooseBatchSave();
        return Ui.Column(8,
            DrawerNote("Make many cards in one go, from a list."),
            Ui.Button("Save an example list to fill in", SaveCsvTemplate),
            DrawerHead("1. The list (CSV)"),
            Ui.Button("Choose the list", () => { if (AskForFile("Choose the list") is { } path) { _batchCsv = path; Render(); } }),
            _csvMsg,
            DrawerHead("2. The photos"),
            Ui.Button("Choose the photos", () => { if (AskForFiles("Choose the photos") is { Length: > 0 } paths) { _batchPhotos = paths; Render(); } }),
            _photosMsg,
            DrawerHead("3. Scanned signatures (optional)"),
            Ui.Button("Choose the signatures", () => { if (AskForFiles("Choose the scanned signatures") is { Length: > 0 } paths) { _batchSigs = paths; Render(); } }),
            _sigsMsg,
            DrawerHead("Print layout"), _cmbBatchLayout, _layoutMsg,
            _batchPaperField,
            _btnBatch, _batchStatusLabel, _batchReport);
    }

    private string BatchLayoutChoice => (int)_cmbBatchLayout.IndexOfSelectedItem switch { 1 => Batch.LayoutPairs, 2 => Batch.LayoutCards, _ => Batch.LayoutDuplex };

    private string BatchPaperChoice
    {
        get
        {
            var at = (int)_cmbBatchPaper.IndexOfSelectedItem;
            return at >= 0 && at < CardConfig.Papers.Length ? CardConfig.Papers[at].Key : CardConfig.DefaultPaper;
        }
    }

    private void RenderBatch()
    {
        _btnBatch.Enabled = !_batchRunning && _batchCsv is not null && _batchPhotos.Length > 0 && _art is not null;
        var chosen = Screen.BatchChosen(_batchCsv, _batchPhotos.Length, _batchSigs.Length);
        Ui.SetText(_csvMsg, chosen.List);
        Ui.SetText(_photosMsg, chosen.Photos);
        Ui.SetText(_sigsMsg, chosen.Signatures);

        var layout = BatchLayoutChoice;
        Ui.Show(_batchPaperField, layout != Batch.LayoutCards);
        Ui.SetText(_layoutMsg, Screen.BatchLayoutLine(layout, BatchPaperChoice));
        Ui.SetText(_batchStatusLabel, _batchStatus);
        Ui.Show(_batchStatusLabel, _batchStatus.Length > 0);

        if (ReferenceEquals(_batchReportShown, _batchResults)) return;
        _batchReportShown = _batchResults;
        foreach (var old in _batchReport.ArrangedSubviews) { _batchReport.RemoveArrangedSubview(old); old.RemoveFromSuperview(); }
        foreach (var r in _batchResults)
        {
            var (tone, head, text) = Screen.BatchRow(r);
            // the words say which it is; the colour only agrees with them
            var line = Ui.Label(text, 12, maxWidth: DrawerText);
            line.TextColor = ToneColor(tone);
            _batchReport.AddArrangedSubview(Ui.Column(1, DrawerHead(head), line));
        }
    }

    private void SaveCsvTemplate()
    {
        var path = AskWhereToSave("Save the example list", "apg-id-batch-template.csv");
        if (path is null) return;
        try
        {
            File.WriteAllText(path, Batch.CsvTemplate);
            _batchStatus = $"Saved {Path.GetFileName(path)}. Fill it in with Excel or Numbers and save it as CSV.";
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException)
        {
            _batchStatus = $"The example couldn’t be saved there ({x.Message.TrimEnd('.')}).";
        }
        Render();
    }

    private void ChooseBatchSave()
    {
        var path = AskWhereToSave("Save the batch", Batch.FileFor(1, BatchLayoutChoice != Batch.LayoutCards, DateTime.Now).FileName);
        if (path is not null) _ = RunBatch(path);
    }

    private void BatchSays(string status)
    {
        _batchStatus = status;
        Render();
    }

    internal async Task RunBatch(string savePath)
    {
        if (_batchRunning || _batchCsv is null || _art is null || _renderer is null) return;
        _batchRunning = true;
        _batchResults = Array.Empty<BatchRowResult>();
        BatchSays("Reading the list…");
        try
        {
            string csv;
            try { csv = await File.ReadAllTextAsync(_batchCsv); }
            catch (Exception x) when (x is IOException or UnauthorizedAccessException) { BatchSays($"The list couldn’t be read ({x.Message.TrimEnd('.')})."); return; }

            // duplicates across computers are only caught if the shared register has been read
            if (!await _session.ReadSharedRegister())
                BatchSays("The shared register could not be reached, so numbers are checked against this computer only.");

            var geometry = _session.Geometry;
            // its own measurer: the batch runs off the window's thread, and the window keeps drawing with the other one
            var services = _session.BatchServices(MacTextMeasurer.Load(InBundle("Fonts")), MacPictures.Decode, _remover);
            var photos = _batchPhotos;
            var sigs = _batchSigs;
            var outcome = await Task.Run(() => Batch.Run(csv, photos, sigs, services,
                (i, n) => NSApplication.SharedApplication.BeginInvokeOnMainThread(() => BatchSays($"Checking {i} of {n}…"))));

            _batchResults = outcome.Results;
            if (outcome.Error.Length > 0) { BatchSays(outcome.Error); return; }

            var good = outcome.Good;
            var entries = good.Where(r => r.Data is not null).Select(r => r.Data!).ToList();       // SAFETY: filtered to the rows that have one on the same line
            var layout = BatchLayoutChoice;
            List<Func<PdfPage>> pages;
            if (layout == Batch.LayoutCards) pages = _renderer.CardPdfPages(_art, geometry, entries, _session.RotateBack);
            else
            {
                var plan = Sheets.Plan(BatchPaperChoice, layout == Batch.LayoutPairs ? SheetMode.Pairs : SheetMode.Duplex);
                if (plan is null) { BatchSays("That paper is not one this app prints on."); return; }
                pages = _renderer.SheetPdfPages(_art, geometry, plan, entries, Sheets.Pages(entries.Count, plan, _session.Flip), _session.BackUpsideDown(plan));
            }
            var (_, title) = Batch.FileFor(good.Count, layout != Batch.LayoutCards, DateTime.Now);
            BatchSays("Building the PDF…");
            var error = await PrintFiles.Write(savePath, OnWindow(pages), title, DateTime.Now);
            if (error.Length > 0) { BatchSays(error); return; }

            // only the rows that made it into the PDF are issued
            var issued = good.Where(r => r.Entry is not null).Select(r => r.Entry!).ToList();       // SAFETY: filtered to the rows that have one on the same line
            // here at once; the shared register is told while the result is put on screen (Session.IssueBatch)
            var telling = _session.IssueBatch(issued);
            var skipped = outcome.Results.Count - good.Count;
            BatchSays($"{good.Count} card{(good.Count == 1 ? "" : "s")} saved as {Path.GetFileName(savePath)}"
                + (skipped > 0 ? $" · {skipped} row{(skipped == 1 ? "" : "s")} skipped, see below." : ". Print at 100%."));
            ShowInFinder(savePath);

            if (await telling is { Length: > 0 } trouble) BatchSays(_batchStatus + " " + trouble);
        }
        finally
        {
            _batchRunning = false;
            Render();
        }
    }

    /* ---- the card design -------------------------------------------
       Moving the map, or swapping the artwork, changes where everything on
       the card belongs, so both are offered on the first step of a blank
       card and nowhere else: once a photo has been placed against one map,
       the placement is already wrong for another.
       ---------------------------------------------------------------- */

    private CardArt? _builtInArt;
    private string? _frontFile;
    private string? _backFile;
    private string _designMsg = "";
    private string _designWarnHead = "";
    private IReadOnlyList<string> _designWarnLines = Array.Empty<string>();

    private readonly MapEditorView _map = new() { Hidden = true };
    private readonly NSTextField _designName = DrawerNote();
    private readonly NSStackView _designHome = Ui.Column(8), _designAdjust = Ui.Column(8);
    private readonly NSButton _btnTplMap = Ui.Button("Show the map", () => { });
    private readonly NSButton _btnTplUse = Ui.Button("Use this artwork", () => { });
    private readonly NSButton _btnTplReset = Ui.Button("Go back to the built-in design", () => { });
    private readonly NSTextField _frontFileMsg = DrawerNote(), _backFileMsg = DrawerNote(), _designReadout = DrawerNote(), _designMsgLabel = DrawerNote();
    private readonly NSTextField _designWarnLabel = Ui.Label("", 12, maxWidth: DrawerText);

    private bool EditingMap => !_map.Hidden;

    private string StoredArtwork(string side) => _design.StoredArtwork(side);

    private void ApplyGeometry() => _design.Apply();

    private NSStackView BuildDesignTool()
    {
        _map.Store = _templates;
        _map.Changed += moved =>
        {
            if (moved) { ApplyGeometry(); DrawPreview(); }
            Ui.SetText(_designReadout, _map.Readout());
        };
        _btnTplMap.Activated += (_, _) => StartAdjusting();
        _btnTplUse.Activated += (_, _) => { if (_frontFile is not null && _backFile is not null) UseArtwork(_frontFile, _backFile); };
        _btnTplReset.Activated += (_, _) => ResetDesign();
        _designWarnLabel.TextColor = NSColor.SystemOrange;
        foreach (var view in new NSView[]
        {
            _btnTplMap,
            DrawerNote("The boxes for the photo, name, job title, number, signature and QR, drawn over the card. Drag them anywhere."),
            DrawerHead("Use different artwork"),
            Ui.Button("Choose the front", () => { if (AskForFile("Choose the front artwork") is { } path) { _frontFile = path; Render(); } }), _frontFileMsg,
            Ui.Button("Choose the back", () => { if (AskForFile("Choose the back artwork") is { } path) { _backFile = path; Render(); } }), _backFileMsg,
            DrawerNote($"Both at {TemplateStore.ArtworkWidth} × {TemplateStore.ArtworkHeight} pixels. TEMPLATE-SPEC.md has the full brief for the designer. The map stays where it is, so check it afterwards."),
            _btnTplUse,
        }) _designHome.AddArrangedSubview(view);
        foreach (var view in new NSView[]
        {
            DrawerNote("Drag a box to move it, its corner to resize it. With a box selected, the arrow keys move it by a quarter of a millimetre, and Shift with an arrow key resizes it. Tab moves between boxes."),
            _designReadout,
            Ui.Button("Keep this map", KeepMap, primary: false),
            Ui.Button("Discard changes", DiscardMap),
            Ui.Button("Put the boxes back where the built-in design has them", () => { _design.ResetBoxes(); _map.Refresh(); Render(); }),
        }) _designAdjust.AddArrangedSubview(view);
        return Ui.Column(8, _designName, _designHome, _designAdjust, _designMsgLabel, _designWarnLabel, _btnTplReset);
    }

    private void RenderDesign()
    {
        var editing = EditingMap;
        Ui.SetText(_designName, $"Now using {_templates.DesignName}.");
        Ui.Show(_designHome, !editing);
        Ui.Show(_designAdjust, editing);
        Ui.Show(_btnTplReset, _templates.IsCustom && !editing);
        _btnTplUse.Enabled = _frontFile is not null && _backFile is not null;
        Ui.SetText(_frontFileMsg, _frontFile is null ? "No front chosen." : Path.GetFileName(_frontFile));
        Ui.SetText(_backFileMsg, _backFile is null ? "No back chosen." : Path.GetFileName(_backFile));
        if (editing) Ui.SetText(_designReadout, _map.Readout());
        Ui.SetText(_designMsgLabel, _designMsg);
        Ui.Show(_designMsgLabel, _designMsg.Length > 0);
        var warning = _designWarnHead.Length > 0 ? _designWarnHead + "\n" + string.Join("\n", _designWarnLines) : "";
        Ui.SetText(_designWarnLabel, warning);
        Ui.Show(_designWarnLabel, warning.Length > 0);
    }

    internal void UseArtwork(string frontPath, string backPath)
    {
        try
        {
            using var front = MacPictures.Load(frontPath);
            using var back = MacPictures.Load(backPath);
            if (front is null || back is null)
            {
                _designMsg = "That artwork couldn’t be opened as a picture. Save it as PNG and try again.";
                Render();
                return;
            }
            foreach (var (side, picture) in new[] { ("front", front), ("back", back) })
            {
                var problem = TemplateStore.ArtworkShapeProblem(side, (int)picture.Width, (int)picture.Height);
                if (problem.Length > 0) { _designMsg = problem; Render(); return; }
            }
            // stored at the printing size, so everything downstream agrees
            var art = new CardArt(
                MacPictures.Resize(front, TemplateStore.ArtworkWidth, TemplateStore.ArtworkHeight),
                MacPictures.Resize(back, TemplateStore.ArtworkWidth, TemplateStore.ArtworkHeight));
            Directory.CreateDirectory(_files.Directory);
            MacPictures.SavePng(MacPictures.ToRgba(art.Front), StoredArtwork("front"));
            MacPictures.SavePng(MacPictures.ToRgba(art.Back), StoredArtwork("back"));

            _designMsg = _design.Adopt(frontPath, ((int)front.Width, (int)front.Height), ((int)back.Width, (int)back.Height));
            _art = art;
            ShowContrastWarnings();
            // the map is wherever it was, which is unlikely to suit new artwork
            StartAdjusting();
            Render();
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _designMsg = DesignDesk.ArtworkNotUsable(x);
            Render();
        }
    }

    /// <summary>
    /// Text that will not read against the artwork. One sentence names the
    /// fields and the fix; the ratios are there for whoever wants to take
    /// them back to the designer.
    /// </summary>
    private void ShowContrastWarnings()
    {
        var warnings = _art is null ? Array.Empty<ContrastWarning>() : _templates.Warnings(MacPictures.ToRgba(_art.Front));
        (_designWarnHead, _designWarnLines) = Screen.ContrastReport(warnings);
    }

    internal void StartAdjusting()
    {
        // not once a card is started; and Core keeps the map as it is now, for Discard to put back
        if (!_design.Begin()) return;
        _front = true;
        _tool = ToolKey.Design;
        _map.Hidden = false;
        _map.Refresh();
        Render();
        Window.MakeFirstResponder(_map);
    }

    // Leaving the editor by any route keeps the changes (DesignDesk.End).
    // Discard is right there for the other direction.
    private void StopAdjusting()
    {
        _map.Hidden = true;
        _design.End();
    }

    internal void KeepMap()
    {
        _map.Hidden = true;
        var said = _design.Keep();
        ShowContrastWarnings();                             // the boxes may have been moved somewhere lighter
        _designMsg = said;
        Render();
        Window.MakeFirstResponder(_btnTplMap);
    }

    internal void DiscardMap()
    {
        _map.Hidden = true;
        var said = _design.Discard();
        ShowContrastWarnings();
        _designMsg = said;
        Render();
        Window.MakeFirstResponder(_btnTplMap);
    }

    internal void ResetDesign()
    {
        if (!Confirm("Go back to the card the app was built with? The artwork and the map you set up here are removed from this computer.", "Go back")) return;
        _map.Hidden = true;
        // the map and the stored artwork are removed from this computer (DesignDesk.BackToBuiltIn)
        var said = _design.BackToBuiltIn();
        _art = _builtInArt;
        _frontFile = _backFile = null;
        ShowContrastWarnings();
        _designMsg = said;
        Render();
    }

    /* ---- moving the photo on the card itself ---------------------- */

    private readonly PhotoDragView _photoDrag = new() { TranslatesAutoresizingMaskIntoConstraints = false };
    private (double Dx, double Dy) _dragFrom;

    private bool CanDragPhoto => Screen.CanDragPhoto(_session, _front, EditingMap);

    private void WireCard()
    {
        _photoDrag.CanDrag = () => CanDragPhoto;
        _photoDrag.Began = () => _dragFrom = (_session.Adjust.Dx, _session.Adjust.Dy);
        _photoDrag.Dragged = DragPhotoTo;
        _photoDrag.Ended = Render;
        _photoDrag.Wheel = larger =>
        {
            var step = CardConfig.Photo.ZoomStep;
            _session.SetZoom(_session.Adjust.Zoom * (larger ? 1 + step : 1 - step));
            Render();
        };
    }

    /// <summary>The pointer is this many points from where the drag began.</summary>
    internal void DragPhotoTo(double dxPoints, double dyPoints)
    {
        var mmPerPoint = CardConfig.CardWidthMm / CardPointsWide;
        _session.SetAdjustOffset(_dragFrom.Dx + dxPoints * mmPerPoint, _dragFrom.Dy + dyPoints * mmPerPoint);
        DrawPreview();
    }

    /* ---- the pen tablet driver ------------------------------------ */

    private const string DriverOfferedKey = "driver-offered";
    private readonly NSButton _btnDriver = Ui.Button("Install the XP-Pen tablet driver", () => { });
    private readonly NSTextField _driverMsg = Ui.Note();
    private string _driverStatus = "";
    private bool _driverBusy;

    /// <summary>On its first start on a Mac with no tablet driver, the app offers to install XP-Pen's. Off in the window test.</summary>
    internal bool OfferDriverOnStart { get; set; } = true;

    /// <summary>Fetches the driver and opens Apple's installer. Replaced in the window test, which must not install anything.</summary>
    internal Func<Task<string>> InstallDriver { get; set; } = PenDriver.Install;

    // The Windows setup program offers the driver as it installs the app.
    // A Mac app is dragged into Applications with no setup program, so the
    // offer is made the first time the app starts instead, once.
    internal void OfferDriver()
    {
        if (!OfferDriverOnStart || PenDriver.Installed || _files.Get(DriverOfferedKey) is not null) return;
        _files.Set(DriverOfferedKey, "1");
        if (Confirm("Install the XP-Pen pen tablet driver? Signatures drawn with the pen are thicker where the pen presses harder, and that needs XP-Pen’s driver. "
            + "The app downloads it from XP-Pen and opens Apple’s installer, which asks for this Mac’s administrator password. You can also do this later from the signature step.", "Install the driver"))
        {
            _ = RunDriverInstall();
        }
    }

    internal async Task RunDriverInstall()
    {
        if (_driverBusy) return;
        _driverBusy = true;
        _driverStatus = "Downloading XP-Pen’s driver (about 58 MB)…";
        Render();
        try
        {
            var error = await InstallDriver();
            _driverStatus = error.Length > 0 ? error
                : "Apple’s installer is open. It asks for this Mac’s administrator password. Afterwards macOS asks you to allow the driver under Privacy & Security, in Input Monitoring and in Accessibility; then open this app again and sign.";
        }
        finally
        {
            _driverBusy = false;
            Render();
        }
    }

    private void RenderDriver(string tabletNote)
    {
        var installed = PenDriver.Installed;
        Ui.Show(_btnDriver, !installed);
        _btnDriver.Enabled = !_driverBusy;
        Ui.SetText(_driverMsg, _driverStatus);
        Ui.Show(_driverMsg, _driverStatus.Length > 0);
        Ui.SetText(_tabletNote, tabletNote);
    }

    /* ---- for the window test --------------------------------------- */

    internal void SetBatchFiles(string csv, string[] photos, string[] signatures)
    {
        _batchCsv = csv;
        _batchPhotos = photos;
        _batchSigs = signatures;
        Render();
    }

    internal NSPopUpButton BatchLayoutBox => _cmbBatchLayout;
    internal string BatchStatus => _batchStatus;
    internal IReadOnlyList<BatchRowResult> BatchResults => _batchResults;
    internal MapEditorView Map => _map;
    internal bool MapShown => EditingMap;
    internal string DesignMessage => _designMsg;
    internal string DesignNameLine => _designName.StringValue;
    internal TemplateStore Templates => _templates;
    internal NSButton DesignRailButton => _railDesign;
    internal NSButton DriverButton => _btnDriver;
    internal string DriverStatus => _driverStatus;
}
