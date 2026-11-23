// The jobs: the things the app does that take more than one step, and
// where the order of the steps is the rule.
//
// Each used to be written out in both windows. The one that matters most is
// SavePrint: an ID is issued only after its PDF is known to be saved, and
// that order is now one method with a test (JobTests), not the order of
// lines in two window files. The windows hand in what only they have (pages
// drawn by their renderer, a picture decoder) and show what comes back.
//
// None of these methods leaves the thread it was called on before it
// touches the form: an await here comes back to the window's own thread.

namespace IdMaker.Core;

/// <summary>What signing in came to: whether it worked, and what to tell the operator either way.</summary>
public sealed record SignInReport(bool Ok, string Message);

/// <summary>A print ready to be drawn and saved.</summary>
/// <param name="Issue">What saving this print issues. The window never needs to look inside.</param>
public sealed record PrintJob(
    SheetPlan Plan, IReadOnlyList<CardData> Entries, IReadOnlyList<SheetPage> Pages, bool BackUpsideDown, string Title, int Cards, PrintIssue Issue);

/// <summary>
/// The cards a print issues, fixed when the print is made. The form stays
/// live while the file is written (a queued card can be removed, a name
/// typed again), and what is on the paper is what was on the form when the
/// pages were laid out, so that is what the register is given.
/// </summary>
/// <param name="Example">The print is of the example card: nothing is issued.</param>
/// <param name="TypedTitles">Job titles typed under "Other…" on the card that was on screen, to be offered next time.</param>
public sealed record PrintIssue(bool Example, IReadOnlyList<JobCard> Cards, IReadOnlyList<string> TypedTitles);

/// <summary>What became of a print.</summary>
/// <param name="Error">Why the PDF could not be saved, or "". When it is not "", nothing was issued.</param>
/// <param name="Cards">How many cards were issued.</param>
/// <param name="Trouble">The shared register being told, still in flight: what went wrong there, or "".</param>
public sealed record PrintSaved(string Error, int Cards, Task<string> Trouble);

/// <summary>What became of a scanned signature.</summary>
/// <param name="Landed">False when the card it was for has been put aside meanwhile: nothing was changed.</param>
/// <param name="Error">Why the scan could not be used, or "".</param>
public sealed record ScanTaken(bool Landed, string Error);

/// <summary>Writing the print to disk.</summary>
public static class PrintFiles
{
    /// <summary>
    /// Write a PDF. It is written beside where it is going and moved over
    /// only when it is whole, so a failure never leaves half a file under
    /// the real name. The pages are asked for one at a time, off the
    /// caller's thread. Returns "" or what went wrong, in words for the operator.
    /// </summary>
    public static async Task<string> Write(string path, IReadOnlyList<Func<PdfPage>> pages, string title, DateTime created, Action<int, int>? progress = null)
    {
        var partial = path + ".part";
        var drawn = pages.Select(Drawn).ToList();
        try
        {
            await Task.Run(() =>
            {
                using (var stream = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    PdfWriter.Write(stream, drawn, title, created, progress);
                }
                File.Move(partial, path, overwrite: true);
            });
            return "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or OutOfMemoryException or PageNotDrawnException)
        {
            try { File.Delete(partial); }
            catch (Exception d) when (d is IOException or UnauthorizedAccessException) { /* nothing more to be done about a file that will not go */ }
            return e switch
            {
                OutOfMemoryException => "This print is too large for this computer’s memory. Print fewer cards at a time.",
                // not the folder's fault, so no advice about the folder
                PageNotDrawnException => $"A page of this print couldn’t be drawn {Words.Why(e)}. Nothing was saved. Try again; if it happens again, close the app and open it again.",
                _ => $"The file couldn’t be saved there {Words.Why(e)}. If it is open in another program, close it, or choose a different folder.",
            };
        }
    }

    /// <summary>
    /// A page the window could not draw (it is closing, a picture is gone).
    /// A window's page function says so with InvalidOperationException, which
    /// is also what a slip in the drawing code throws; only one that came out
    /// of a page function becomes this, and Write catches this and no other
    /// InvalidOperationException, so neither is taken for a file that is open
    /// in another program.
    /// </summary>
    private sealed class PageNotDrawnException(InvalidOperationException cause) : Exception(cause.Message, cause);

    private static Func<PdfPage> Drawn(Func<PdfPage> page) => () =>
    {
        try { return page(); }
        catch (InvalidOperationException e) { throw new PageNotDrawnException(e); }
    };

    /// <summary>Where the back goes when the front is saved as a picture: beside it, "-front" swapped for "-back".</summary>
    public static string BackPictureFor(string frontPath, string folderIfNone) =>
        Path.Combine(Path.GetDirectoryName(frontPath) ?? folderIfNone,
            Path.GetFileNameWithoutExtension(frontPath).Replace("-front", "", StringComparison.OrdinalIgnoreCase) + "-back.png");

    /// <summary>Pictures are a proof to look at, not a print: the card is issued when the PDF is saved.</summary>
    public static string PicturesSaved(string frontPath, string backPath) =>
        $"Saved {Path.GetFileName(frontPath)} and {Path.GetFileName(backPath)}. The card is not issued until you save the PDF.";

    public static string PicturesNotSaved(Exception e) => $"The pictures couldn’t be saved there {Words.Why(e)}. Choose a different folder.";
}

public sealed partial class Session
{
    /* ---- the shared register --------------------------------------
       After anything that changes what the register knows, the number on
       the form is checked again. That used to be the window's to remember.
       ---------------------------------------------------------------- */

    /// <summary>Is there a shared register at all in this build?</summary>
    public bool HasSharedRegister => _register.Shared;

    /// <summary>
    /// Start-up: read the shared register now rather than on the first
    /// keystroke, and send the cards saved while it was out of reach. True
    /// when one of them was refused (somebody else took its number
    /// meanwhile): that cannot wait to be found, so the window opens the list.
    /// </summary>
    public async Task<bool> WarmRegister()
    {
        if (await _register.LoadIndex()) await _register.SendWaiting();
        var refused = _register.Refused.Count > 0;
        RefreshDuplicate();
        return refused;
    }

    /// <summary>Sign this computer in, read the register, and send what was waiting for somebody to sign in.</summary>
    public async Task<SignInReport> SignIn(string email, string password)
    {
        var result = await _register.SignIn(email, password);
        if (!result.Ok) return new SignInReport(false, result.Error);
        var read = await _register.LoadIndex();
        var left = read ? await _register.SendWaiting() : _register.WaitingForShared;
        RefreshDuplicate();
        return new SignInReport(true,
            !read ? $"Signed in, but the register could not be read just now. {_register.LastProblem}"
            : left > 0 ? $"Signed in. {left} card{Words.S(left)} could not be written yet and will be tried again."
            : "Signed in. This computer now checks every number against the shared register.");
    }

    /// <summary>Forget this computer's sign-in. Returns what to tell the operator.</summary>
    public string SignOut()
    {
        _register.SignOut();
        RefreshDuplicate();
        return "Signed out. Numbers are checked against this computer only until somebody signs in.";
    }

    /// <summary>Ask the shared register whether it is answering, and send anything waiting if it is. Returns what to tell the operator.</summary>
    public async Task<string> CheckRegister()
    {
        var ping = await _register.Ping();
        if (ping.Ok) await _register.SendWaiting();
        RefreshDuplicate();
        return ping.Ok ? $"The shared register is answering: {ping.Rows} card{Words.S(ping.Rows)} in it." : ping.Error;
    }

    /// <summary>Take in a saved list of issued IDs. Returns what to tell the operator.</summary>
    public string ImportRegister(string csvText)
    {
        var added = _register.ImportCsv(csvText);
        RefreshDuplicate();
        return added > 0 ? $"Added {added} card{Words.S(added)} from that list." : "Nothing new in that list.";
    }

    /// <summary>The list of issued IDs on this computer, as a CSV file's text.</summary>
    public string RegisterCsv() => _register.ToCsv();

    /// <summary>The operator has dealt with the cards the shared register refused.</summary>
    public void ClearRefused() => _register.ClearRefused();

    /* ---- saving the print ------------------------------------------ */

    /// <summary>The print as it stands: what to draw. Null when there is nothing to print.</summary>
    public PrintJob? Print()
    {
        var issue = Issuing();
        if (PrintPages(issue.Cards) is not { } print || print.Entries.Count == 0) return null;
        return new PrintJob(print.Plan, print.Entries, print.Pages, BackUpsideDown(print.Plan), PrintFile().Title, issue.Cards.Count, issue);
    }

    /// <summary>
    /// Write the PDF, and only once it is safely on disk mark its cards as
    /// issued. A print that failed to save has issued nothing. The shared
    /// register is told after that, and the answer follows in Trouble.
    /// The cards issued are the job's, not the form's as it stands when the
    /// write ends: the register must say what the paper says.
    /// </summary>
    /// <param name="pages">The job's pages, drawn by the window's renderer when each is asked for.</param>
    public async Task<PrintSaved> SavePrint(string path, PrintJob job, IReadOnlyList<Func<PdfPage>> pages)
    {
        var error = await PrintFiles.Write(path, pages, job.Title, _now());
        if (error.Length > 0) return new PrintSaved(error, 0, Task.FromResult(""));
        // Only now: the file is whole and under its own name.
        var issued = MarkIssued(job.Issue);
        return new PrintSaved("", job.Cards, TellShared(issued));
    }

    private async Task<string> TellShared(IReadOnlyList<RegisterEntry> issued) => (await RecordShared(issued)).Message();

    /* ---- a batch ---------------------------------------------------- */

    /// <summary>
    /// Read the shared register before a batch, so duplicates across
    /// computers are caught. False when there is one and it could not be
    /// reached: the batch goes on, checked against this computer only.
    /// </summary>
    public async Task<bool> ReadSharedRegister() => !_register.Shared || await _register.LoadIndex();

    /// <summary>What a batch needs to check its rows: this form's register, design and clock, and the window's measurer and decoder.</summary>
    public Batch.Services BatchServices(ITextMeasurer measurer, Func<string, (RgbaImage? Image, string Error)> decode, IBackgroundRemover? remover) =>
        new(_register, measurer, Geometry, decode, remover, _now);

    /// <summary>
    /// The batch's PDF has been saved: the rows in it are now issued, here
    /// at once and then in the shared register. The task is the shared
    /// register being told: what went wrong there, or "".
    /// </summary>
    public Task<string> IssueBatch(IReadOnlyList<RegisterEntry> issued)
    {
        foreach (var entry in issued) { _register.Record(entry); _register.NoteShared(entry); }
        RefreshDuplicate();
        return TellSharedOfBatch(issued);
    }

    private async Task<string> TellSharedOfBatch(IReadOnlyList<RegisterEntry> issued)
    {
        var trouble = await TellShared(issued);
        // a refused row's number is somebody else's: the number on the form is checked against that
        if (trouble.Length > 0) RefreshDuplicate();
        return trouble;
    }

    /* ---- a photo, a scanned signature ------------------------------- */

    /// <summary>A photo is being looked at. The window shows that, and takes no second photo meanwhile.</summary>
    public bool CheckingPhoto { get; private set; }

    // Still needed: a file whose size cannot be read (gone, or not ours to read) is given none, so it
    // passes the size limit, and the decoder then says in words why it cannot be opened.
    private static long SizeOf(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>
    /// Open a picture file and put it through the photo pipeline. The
    /// picture is decoded and looked at off the caller's thread, and is
    /// never kept anywhere but on the card being made. False when nothing
    /// was changed: another photo is already being looked at, or the card
    /// this one was for was put aside while it was being looked at (it must
    /// not land on the blank card that replaced it).
    /// </summary>
    public async Task<bool> TakePhoto(string path, Func<string, (RgbaImage? Image, string Error)> decode, IBackgroundRemover? remover)
    {
        if (CheckingPhoto) return false;
        var name = Path.GetFileName(path);
        var problem = PhotoPipeline.FileProblem(name, SizeOf(path));
        if (problem.Length > 0) { PhotoFailed(problem); return true; }

        CheckingPhoto = true;
        Changed?.Invoke();
        var card = CardSerial;
        var spec = Geometry.Photo;
        (PhotoResult? Result, string Error) outcome;
        try
        {
            outcome = await Task.Run(() =>
            {
                var (image, error) = decode(path);
                return image is null ? ((PhotoResult?)null, error) : (PhotoPipeline.Analyse(image, name, spec, remover), "");
            });
        }
        finally { CheckingPhoto = false; }

        if (card != CardSerial) return false;
        if (outcome.Result is null) PhotoFailed(outcome.Error);
        else AcceptPhoto(outcome.Result);
        return true;
    }

    /// <summary>
    /// Read a scanned signature from a picture file. A scan that cannot be
    /// read leaves whatever signature was there, and Error says why.
    /// </summary>
    public async Task<ScanTaken> TakeSignatureScan(string path, Func<string, (RgbaImage? Image, string Error)> decode)
    {
        var name = Path.GetFileName(path);
        var problem = PhotoPipeline.FileProblem(name, SizeOf(path), "picture of the signature");
        (ScannedSignature? Signature, string Error) outcome = (null, problem);
        var card = CardSerial;
        if (problem.Length == 0)
        {
            outcome = await Task.Run(() =>
            {
                var (image, error) = decode(path);
                return image is null ? (null, error) : SignatureInk.FromImage(image, name);
            });
        }
        // as with a photo: the card it was for is gone
        if (card != CardSerial) return new ScanTaken(false, "");
        if (outcome.Signature is null) return new ScanTaken(true, outcome.Error);
        SetSignature(outcome.Signature);
        return new ScanTaken(true, "");
    }
}

/// <summary>
/// Changing the card design: moving the map's boxes, taking new artwork,
/// going back to the built-in card. The windows draw the boxes and load the
/// pictures; what is kept, what is thrown away, and that the form's own copy
/// of the map (Session.Geometry) follows every change, is decided here.
/// </summary>
public sealed class DesignDesk
{
    private readonly Session _session;
    private readonly TemplateStore _templates;
    private readonly string _folder;
    private (Dictionary<string, RectMm> Boxes, RectMm PhotoRect)? _before;

    /// <param name="folder">Where this computer keeps the artwork of a design put in by hand.</param>
    public DesignDesk(Session session, TemplateStore templates, string folder)
    {
        _session = session;
        _templates = templates;
        _folder = folder;
    }

    /// <summary>The file a design's own front or back artwork is kept in ("front" or "back").</summary>
    public string StoredArtwork(string side) => Path.Combine(_folder, TemplateStore.ArtworkFileName(side));

    /// <summary>The form checks names and places photos against the map as it now stands.</summary>
    public void Apply() => _session.Geometry = _templates.Geometry();

    /// <summary>
    /// Start moving the boxes. False when a card has been started: once a
    /// photo has been placed against one map, it is already wrong for another.
    /// </summary>
    public bool Begin()
    {
        if (!_session.DesignAllowed) return false;
        // what Discard puts back: the map as it was when the editor opened
        _before = _templates.Snapshot();
        return true;
    }

    /// <summary>
    /// Stop moving the boxes, by any route. The changes are kept: the
    /// preview has been showing the result the whole time, and throwing
    /// away work on a stray click is the worse of the two mistakes.
    /// </summary>
    public void End()
    {
        if (_templates.IsPristine) _templates.Clear();      // looked, moved nothing
        else _templates.Save();
        Apply();
    }

    /// <summary>Keep the map as it is now. Returns what to tell the operator.</summary>
    public string Keep()
    {
        End();
        return _templates.IsCustom ? "Saved on this computer." : "Nothing was moved.";
    }

    /// <summary>Put the map back as it was when the editor opened. Returns what to tell the operator.</summary>
    public string Discard()
    {
        _templates.RestoreSnapshot(_before);
        End();
        return "Left as it was.";
    }

    /// <summary>The boxes go back where the built-in design has them; the editor stays open.</summary>
    public void ResetBoxes()
    {
        _templates.ResetBoxes();
        Apply();
    }

    /// <summary>What a design is called when its artwork comes from this file.</summary>
    public static string NameFromFile(string frontPath) => Path.GetFileNameWithoutExtension(frontPath).Replace('-', ' ').Replace('_', ' ').Trim();

    /// <summary>
    /// New artwork is in place (the window has checked its shape with
    /// TemplateStore.ArtworkShapeProblem and saved both pictures to
    /// StoredArtwork). Returns what to tell the operator.
    /// </summary>
    public string Adopt(string frontPath, (int W, int H) front, (int W, int H) back)
    {
        _templates.AdoptArtwork(NameFromFile(frontPath), front, back);
        _templates.Save();
        Apply();
        return _templates.Note.Length > 0 ? _templates.Note : "In use. Check the map against the new artwork.";
    }

    public static string ArtworkNotUsable(Exception e) => $"That artwork couldn’t be used {Words.Why(e)}. Save it as PNG and try again.";

    /// <summary>
    /// Go back to the card the app was built with: the map and the stored
    /// artwork are removed from this computer. Returns what to tell the operator.
    /// </summary>
    public string BackToBuiltIn()
    {
        _templates.Clear();
        foreach (var side in new[] { "front", "back" })
        {
            try { File.Delete(StoredArtwork(side)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* an orphaned picture is harmless: nothing points at it any more */ }
        }
        Apply();
        return "Back to the built-in design.";
    }
}
