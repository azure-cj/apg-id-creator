// Tests that need Windows: the real renderer against the card the web app
// printed, and the real window driven the way an operator drives it.

using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IdMaker.App;
using IdMaker.Core;
using IdMaker.Tests;

namespace IdMaker.App.Tests;

/// <summary>
/// One thread for everything WPF, as WPF requires: the application, its
/// resources and every window live on it, and each test hands it work.
/// </summary>
internal static class Ui
{
    private static readonly Dispatcher Dispatcher;

    static Ui()
    {
        var ready = new ManualResetEventSlim();
        Dispatcher? dispatcher = null;
        var thread = new Thread(() =>
        {
            var app = new global::IdMaker.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();                       // loads Theme.xaml, as starting the app does
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        }) { IsBackground = true, Name = "WPF test thread" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        Dispatcher = dispatcher!;
    }

    public static void Run(Action action) => Dispatcher.Invoke(action);
    public static T Run<T>(Func<T> func) => Dispatcher.Invoke(func);
    public static Task RunAsync(Func<Task> func) => Dispatcher.InvokeAsync(func).Task.Unwrap();

    /// <summary>Let queued layout, rendering and BeginInvoke work finish.</summary>
    public static void Settle() => Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}

internal static class Files
{
    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            // test/golden-front.png, not assets/: the program's own Assets folder sits beside these tests,
            // and on Windows "Assets" and "assets" are the same name
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "test", "golden-front.png"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("The repository root was not found above the test binaries.");
        }
    }

    public static string Beside(params string[] parts) => Path.Combine(new[] { AppContext.BaseDirectory }.Concat(parts).ToArray());

    public static JsonElement WebReference { get; } = JsonDocument.Parse(File.ReadAllText(Beside("Fixtures", "web-reference.json"))).RootElement;

    /// <summary>Where the pictures and numbers a person might want to look at afterwards are left.</summary>
    public static string ReportDirectory
    {
        get
        {
            var dir = Environment.GetEnvironmentVariable("IDMAKER_REPORT_DIR");
            if (string.IsNullOrWhiteSpace(dir)) dir = Path.Combine(Path.GetTempPath(), "idmaker-report");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string TempFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "idmaker-apptests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}

public class RenderTests
{
    private static (CardRenderer Renderer, CardArt Art) Load()
    {
        var renderer = new CardRenderer(WpfTextMeasurer.Load(Files.Beside("Fonts")));
        var art = new CardArt(ImageFiles.Load(Files.Beside("Assets", "front-template.png")), ImageFiles.Load(Files.Beside("Assets", "back.png")));
        return (renderer, art);
    }

    private static CardData ExampleCard()
    {
        var (image, error) = ImageFiles.Decode(Path.Combine(Files.RepoRoot, "assets", "example-cut-out.png"));
        Assert.True(image is not null, $"{error} ({ImageFiles.LastFault})");
        var photo = PhotoPipeline.Analyse(image!, "example-cut-out.png", CardConfig.Photo);
        Assert.True(photo.Check.Ok, photo.Check.Problem);
        return new CardData("Christopher Joseph V. Aureo", "IT Associate", 2026, 122, photo.Image, photo.Check.Placement, Session.ExampleSignature());
    }

    [Fact]
    public void The_card_fonts_measure_as_they_did_in_the_web_app()
    {
        Ui.Run(() =>
        {
            var m = WpfTextMeasurer.Load(Files.Beside("Fonts"));
            var report = new StringBuilder();
            var wrong = new List<string>();
            var worst = 0.0;
            foreach (var (font, key) in new[] { (CardFont.Name, "name"), (CardFont.Title, "title"), (CardFont.Number, "employeeNo") })
            {
                var web = Files.WebReference.GetProperty("metrics").GetProperty(key);
                void Height(string what, double want, double got)
                {
                    report.AppendLine($"{key,-10} {what,-22} web {want:0.000} here {got:0.000}");
                    if (want != got) wrong.Add($"{key} {what}: web {want:0.000}, here {got:0.000}");
                }
                Height("cap height", web.GetProperty("capH").GetDouble(), m.CapHeight(font, 100));
                Height("descent of Q,", web.GetProperty("descQ").GetDouble(), m.Descent(font, 100, uppercase: true));
                Height("descent of gjpqy", web.GetProperty("descg").GetDouble(), m.Descent(font, 100, uppercase: false));
                foreach (var w in web.GetProperty("widths").EnumerateObject())
                {
                    var got = m.Width(font, 100, w.Name);
                    var off = Math.Abs(got - w.Value.GetDouble());
                    worst = Math.Max(worst, off);
                    report.AppendLine($"{key,-10} \"{w.Name}\" web {w.Value.GetDouble():0.000} here {got:0.000}");
                    // within a twentieth of a pixel at 100 px: nothing on the card
                    if (off >= 0.05) wrong.Add($"{key} \"{w.Name}\": web {w.Value.GetDouble():0.000}, here {got:0.000}");
                }
            }
            File.WriteAllText(Path.Combine(Files.ReportDirectory, "font-metrics.txt"), $"worst width difference at 100 px: {worst:0.0000}\r\n{report}");
            Assert.True(wrong.Count == 0, string.Join("\n", wrong));
        });
    }

    [Fact]
    public void Names_and_titles_break_onto_the_same_lines_at_the_same_size()
    {
        Ui.Run(() =>
        {
            var m = WpfTextMeasurer.Load(Files.Beside("Fonts"));
            void Same(JsonElement want, FittedText got, string what)
            {
                Assert.Equal(want.GetProperty("lines").EnumerateArray().Select(l => l.GetString()).ToArray(), got.Lines.ToArray());
                Assert.True(want.GetProperty("sizePt").GetDouble() == got.SizePt, $"{what}: size {got.SizePt} pt, the web app chose {want.GetProperty("sizePt").GetDouble()}");
                Assert.Equal(want.GetProperty("fits").GetBoolean(), got.Fits);
            }

            var layout = Files.WebReference.GetProperty("layout");
            var here = TextFit.Layout(m, CardGeometry.BuiltIn, "Christopher Joseph V. Aureo", "IT Associate", 2026, 122);
            Same(layout.GetProperty("name"), here.Name, "name");
            Same(layout.GetProperty("title"), here.Title, "title");
            Same(layout.GetProperty("employeeNo"), here.EmployeeNo, "employee number");

            foreach (var fit in Files.WebReference.GetProperty("fits").EnumerateArray())
            {
                var spec = fit[0].GetString() == "name" ? CardConfig.NameText : CardConfig.TitleText;
                Same(fit[2], TextFit.Fit(m, spec, fit[1].GetString()), fit[1].GetString()!);
            }
        });
    }

    // How far two pictures of the same size are apart inside a box (mm):
    // the mean difference per channel, and the share of pixels that differ visibly.
    private static (double Mean, double Changed) Compare(byte[] a, byte[] b, int w, RectMm box, double ppm)
    {
        int x0 = Math.Max(0, (int)(box.X * ppm)), y0 = Math.Max(0, (int)(box.Y * ppm));
        int x1 = Math.Min(w, (int)Math.Ceiling(box.Right * ppm)), y1 = Math.Min(a.Length / 4 / w, (int)Math.Ceiling(box.Bottom * ppm));
        double sum = 0;
        long changed = 0, n = 0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var o = (y * w + x) * 4;
                var d = Math.Max(Math.Abs(a[o] - b[o]), Math.Max(Math.Abs(a[o + 1] - b[o + 1]), Math.Abs(a[o + 2] - b[o + 2])));
                sum += d;
                if (d > 48) changed++;
                n++;
            }
        }
        return n == 0 ? (0, 0) : (sum / n, (double)changed / n);
    }

    [Fact]
    public void The_example_card_is_the_card_the_web_app_printed()
    {
        Ui.Run(() =>
        {
            var (renderer, art) = Load();
            var ppm = CardConfig.PrintPxPerMm;
            var front = renderer.RenderFront(art, CardGeometry.BuiltIn, ExampleCard(), ppm);
            ImageFiles.SavePng(front, Path.Combine(Files.ReportDirectory, "front-native.png"));

            var golden = ImageFiles.Load(Path.Combine(Files.RepoRoot, "test", "golden-front.png"));
            Assert.Equal(golden.PixelWidth, front.PixelWidth);
            Assert.Equal(golden.PixelHeight, front.PixelHeight);
            var mine = CardRenderer.ToRgba(front).Data;
            var theirs = CardRenderer.ToRgba(golden).Data;
            var w = front.PixelWidth;

            // The two were drawn by different engines — Chromium's canvas
            // then, Windows now — so edges are not anti-aliased identically
            // and the photo is resampled by a different filter. What must
            // agree is where everything is and what it says. Each zone is
            // held to how far off it may be; a line of text one pixel out of
            // place, or a wrong font, blows straight through these.
            var zones = new (string Name, RectMm Box, double MaxMean, double MaxChanged)[]
            {
                ("whole card", new RectMm(0, 0, CardConfig.CardWidthMm, CardConfig.CardHeightMm), 4.0, 0.02),
                ("photo", new RectMm(17, 10, 36.5, 47), 6.0, 0.03),
                ("name", CardConfig.NameText.Box, 14.0, 0.08),
                ("job title", CardConfig.TitleText.Box, 14.0, 0.08),
                // The number is set in a light face four points high: its strokes are barely a pixel
                // wide, so how each engine smooths an edge is most of what there is to compare. Where
                // the ink sits is held to a pixel and a half by the check below instead.
                ("employee number", CardConfig.EmployeeNoText.Box, 14.0, 0.12),
                ("signature", CardConfig.Signature.Box, 14.0, 0.08),
                ("QR", new RectMm(CardConfig.Qr.X, CardConfig.Qr.Y, CardConfig.Qr.Size, CardConfig.Qr.Size), 1.0, 0.002),
            };
            var report = new StringBuilder();
            var failures = new List<string>();
            foreach (var z in zones)
            {
                var (mean, changed) = Compare(mine, theirs, w, z.Box, ppm);
                var line = $"{z.Name,-16} mean difference {mean,6:0.00} (limit {z.MaxMean:0.0})   pixels visibly different {changed,7:P2} (limit {z.MaxChanged:P1})";
                report.AppendLine(line);
                if (mean > z.MaxMean || changed > z.MaxChanged) failures.Add(line);
            }
            // Where the ink is. Each line of text is found in both pictures as
            // the pixels that differ from the bare artwork, and the box around
            // them must agree to a pixel and a half on every side: text one
            // pixel out of place fails here whatever the smoothing looks like.
            var bare = CardRenderer.ToRgba(renderer.RenderFront(art, CardGeometry.BuiltIn, null, ppm)).Data;
            (int L, int T, int R, int B) Ink(byte[] picture, RectMm box)
            {
                // a margin at the sides only: the name's box and the title's touch, so looking above or below one finds the other's ink
                int x0 = (int)((box.X - 1) * ppm), y0 = (int)Math.Ceiling(box.Y * ppm), x1 = (int)((box.Right + 1) * ppm), y1 = (int)(box.Bottom * ppm);
                int l = int.MaxValue, t = int.MaxValue, r = -1, b = -1;
                for (var y = y0; y < y1; y++)
                {
                    for (var x = x0; x < Math.Min(x1, w); x++)
                    {
                        var o = (y * w + x) * 4;
                        var d = Math.Max(Math.Abs(picture[o] - bare[o]), Math.Max(Math.Abs(picture[o + 1] - bare[o + 1]), Math.Abs(picture[o + 2] - bare[o + 2])));
                        if (d < 90) continue;
                        l = Math.Min(l, x); t = Math.Min(t, y); r = Math.Max(r, x); b = Math.Max(b, y);
                    }
                }
                return (l, t, r, b);
            }
            foreach (var (name, box) in new[] { ("name", CardConfig.NameText.Box), ("job title", CardConfig.TitleText.Box), ("employee number", CardConfig.EmployeeNoText.Box) })
            {
                var here = Ink(mine, box);
                var there = Ink(theirs, box);
                var line = $"{name,-16} ink box here {here}, web {there}";
                report.AppendLine(line);
                if (here.R < 0 || Math.Abs(here.L - there.L) > 1.5 || Math.Abs(here.T - there.T) > 1.5 || Math.Abs(here.R - there.R) > 1.5 || Math.Abs(here.B - there.B) > 1.5)
                    failures.Add(line);
            }
            File.WriteAllText(Path.Combine(Files.ReportDirectory, "front-against-web.txt"), report.ToString());

            // a picture of where the two differ, for a person to look at
            var diff = new byte[mine.Length];
            for (var i = 0; i < mine.Length; i += 4)
            {
                var d = Math.Max(Math.Abs(mine[i] - theirs[i]), Math.Max(Math.Abs(mine[i + 1] - theirs[i + 1]), Math.Abs(mine[i + 2] - theirs[i + 2])));
                var v = (byte)Math.Min(255, d * 4);
                diff[i] = v; diff[i + 1] = v; diff[i + 2] = v; diff[i + 3] = 255;
            }
            ImageFiles.SavePng(CardRenderer.ToBitmap(new RgbaImage(w, front.PixelHeight, diff)), Path.Combine(Files.ReportDirectory, "front-difference.png"));

            Assert.True(failures.Count == 0, "The native card differs from the web app's:\n" + string.Join("\n", failures) + "\n\nAll zones:\n" + report);
        });
    }

    [Fact]
    public void The_preview_is_the_print_drawn_smaller()
    {
        Ui.Run(() =>
        {
            var (renderer, art) = Load();
            var card = ExampleCard();
            var small = renderer.RenderFront(art, CardGeometry.BuiltIn, card, 6);
            Assert.Equal(324, small.PixelWidth);
            Assert.Equal(514, small.PixelHeight);
            var back = renderer.RenderBack(art, 6, rotate180: false);
            var turned = renderer.RenderBack(art, 6, rotate180: true);
            var a = CardRenderer.ToRgba(back).Data;
            var b = CardRenderer.ToRgba(turned).Data;
            // turned upside down, the first pixel is the last
            var last = a.Length - 4;
            Assert.InRange(b[0] - a[last], -12, 12);
            Assert.InRange(b[1] - a[last + 1], -12, 12);
        });
    }

    private static (byte R, byte G, byte B) PixelAt(System.Windows.Media.Imaging.BitmapSource bmp, double xMm, double yMm, double ppm)
    {
        var data = CardRenderer.ToRgba(bmp).Data;
        var o = ((int)(yMm * ppm) * bmp.PixelWidth + (int)(xMm * ppm)) * 4;
        return (data[o], data[o + 1], data[o + 2]);
    }

    [Fact]
    public void A_photo_that_stops_short_fades_out_instead_of_ending_in_a_hard_edge()
    {
        Ui.Run(() =>
        {
            var (renderer, art) = Load();
            const double ppm = 8;
            // a flat magenta "photo" that ends at 50 mm, well inside the window
            var flat = new RgbaImage(40, 40);
            for (var i = 0; i < flat.Data.Length; i += 4) { flat.Data[i] = 255; flat.Data[i + 1] = 0; flat.Data[i + 2] = 255; flat.Data[i + 3] = 255; }
            var placement = new PhotoPlacement(20, 20, 30, 30, ShortBy: 16);
            var card = new CardData("", "", 2026, null, flat, placement, null);
            var faded = renderer.RenderFront(art, CardGeometry.BuiltIn, card, ppm);
            var bare = renderer.RenderFront(art, CardGeometry.BuiltIn, null, ppm);

            // well above the fade the photo is solid
            var solid = PixelAt(faded, 35, 30, ppm);
            Assert.True(solid.R > 240 && solid.G < 20 && solid.B > 240, $"solid part is {solid}");
            // just above its bottom edge it has all but gone, and the artwork shows through
            var edge = PixelAt(faded, 35, 49.7, ppm);
            var under = PixelAt(bare, 35, 49.7, ppm);
            Assert.InRange(edge.G - under.G, -40, 40);
            Assert.True(Math.Abs(edge.R - under.R) < 60, $"edge {edge} against artwork {under}");
            // halfway down the fade it is a mix
            var mid = PixelAt(faded, 35, 46.5, ppm);
            var midUnder = PixelAt(bare, 35, 46.5, ppm);
            static int Apart((byte R, byte G, byte B) a, (byte R, byte G, byte B) b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
            Assert.True(Apart(mid, solid) > 25 && Apart(mid, midUnder) > 25, $"halfway down the fade is {mid}; solid photo {solid}, bare artwork {midUnder}");
        });
    }

    [Fact]
    public void A_scanned_signature_prints_in_white_inside_its_box_and_the_photo_stays_inside_its_window()
    {
        Ui.Run(() =>
        {
            var (renderer, art) = Load();
            const double ppm = 10;
            var alpha = Enumerable.Repeat((byte)255, 60 * 20).ToArray();
            var scan = new ScannedSignature(60, 20, alpha, "sign.png");
            // a photo far larger than the card: it must not spill over the artwork outside its window
            var flat = new RgbaImage(8, 8);
            for (var i = 0; i < flat.Data.Length; i += 4) { flat.Data[i] = 0; flat.Data[i + 1] = 255; flat.Data[i + 2] = 0; flat.Data[i + 3] = 255; }
            var card = new CardData("Ana Reyes", "HR Associate", 2026, 7, flat, new PhotoPlacement(-50, -50, 200, 200, 0), scan);
            var front = renderer.RenderFront(art, CardGeometry.BuiltIn, card, ppm);
            var bare = renderer.RenderFront(art, CardGeometry.BuiltIn, null, ppm);

            var box = CardConfig.Signature.Box;
            var ink = PixelAt(front, box.X + box.W / 2, box.Y + box.H / 2, ppm);
            Assert.True(ink.R > 240 && ink.G > 240 && ink.B > 240, $"signature ink is {ink}");

            // inside the photo window: the photo
            var inside = PixelAt(front, 36, 30, ppm);
            Assert.True(inside.G > 240 && inside.R < 20, $"inside the window: {inside}");
            // left of the window (the logo side) and in the band below: untouched artwork
            Assert.Equal(PixelAt(bare, 8, 30, ppm), PixelAt(front, 8, 30, ppm));
            Assert.Equal(PixelAt(bare, 45, 82, ppm), PixelAt(front, 45, 82, ppm));
            // the band over the shoulders is drawn back over the photo (two spots inside both the band and the photo window, clear of any text)
            Assert.Equal(PixelAt(bare, 20, 66.5, ppm), PixelAt(front, 20, 66.5, ppm));
            Assert.Equal(PixelAt(bare, 52.5, 58.2, ppm), PixelAt(front, 52.5, 58.2, ppm));
        });
    }

    [Fact]
    public void A_sheet_is_the_size_of_its_paper_and_the_pdf_has_a_page_per_side()
    {
        Ui.Run(() =>
        {
            var (renderer, art) = Load();
            var card = ExampleCard();
            var plan = Sheets.Plan("A4", SheetMode.Duplex)!;
            var entries = new[] { card, card };
            var pages = Sheets.Pages(entries.Length, plan, DuplexFlip.LongEdge);
            var pdfPages = renderer.SheetPdfPages(art, CardGeometry.BuiltIn, plan, entries, pages, backUpsideDown: false);
            Assert.Equal(2, pdfPages.Count);
            var first = pdfPages[0]();
            Assert.Equal(2480, first.Width);                 // 210 mm at 300 DPI
            Assert.Equal(3508, first.Height);
            Assert.Equal(210, first.WidthMm);
            // the top-left corner is paper, the middle of the first card is not
            Assert.Equal(255, first.Rgb[0]);
            var cx = (int)((plan.Left + 27) * CardConfig.PrintPxPerMm);
            var cy = (int)((plan.Top + 75) * CardConfig.PrintPxPerMm);
            Assert.True(first.Rgb[(cy * first.Width + cx) * 3] < 120, "the band at the bottom of the first card should be dark");

            using var ms = new MemoryStream();
            PdfWriter.Write(ms, renderer.CardPdfPages(art, CardGeometry.BuiltIn, new[] { card }, rotateBack: false), "one card", DateTime.Now);
            var text = Encoding.Latin1.GetString(ms.ToArray());
            Assert.Contains("/Count 2", text);
            Assert.Contains("/Width 638 /Height 1011", text);
        });
    }

    [Fact]
    public void A_phone_photo_stored_sideways_is_stood_upright()
    {
        // a JPEG 40 wide and 20 high, marked "rotate 90 degrees to view"
        var path = Path.Combine(Files.TempFolder(), "sideways.jpg");
        Ui.Run(() =>
        {
            var pixels = new byte[40 * 20 * 4];
            for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 200; pixels[i + 1] = 120; pixels[i + 2] = 40; pixels[i + 3] = 255; }
            var source = BitmapSource.Create(40, 20, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 160);
            var meta = new BitmapMetadata("jpg");
            meta.SetQuery("/app1/ifd/{ushort=274}", (ushort)6);
            var encoder = new JpegBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source, null, meta, null));
            using var stream = File.Create(path);
            encoder.Save(stream);
        });
        var (image, error) = ImageFiles.Decode(path);
        Assert.True(image is not null, error);
        Assert.Equal(20, image!.Width);
        Assert.Equal(40, image.Height);

        var (none, why) = ImageFiles.Decode(Path.Combine(Files.RepoRoot, "README.md"));
        Assert.Null(none);
        Assert.Contains("couldn’t be opened", why);
    }
}

/// <summary>The real window, driven the way an operator drives it.</summary>
public class WindowTests
{
    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static MainWindow Open(string folder)
    {
        var files = new FileStore(folder);
        // every movement ends at once, so no picture or check lands halfway through one
        Motion.Off = true;
        // no shared register: these tests must not write to the company's register
        var window = new MainWindow(files, new Register(files) { Project = null })
        {
            OpenSigningOnArrival = false,
            Confirm = (_, _) => true,
        };
        window.Show();
        return window;
    }

    /// <summary>The window on a computer whose shared register is the pretend project. Nothing here reaches a real one.</summary>
    private static MainWindow OpenShared(string folder, FakeFirebase firebase, ISecretKeeper? keeper = null)
    {
        var files = new FileStore(folder);
        Motion.Off = true;
        var window = new MainWindow(files, new Register(files, firebase.Http, keeper) { Project = FakeFirebase.Project })
        {
            OpenSigningOnArrival = false,
            Confirm = (_, _) => true,
        };
        window.Show();
        return window;
    }

    /// <summary>Wait for something the window does a moment later (a click's request coming back), without sleeping a fixed time.</summary>
    private static async Task Until(Func<bool> onWindow, string what)
    {
        for (var i = 0; i < 200; i++)
        {
            if (Ui.Run(onWindow)) return;
            await Task.Delay(25);
            Ui.Settle();
        }
        Assert.Fail("Waited five seconds for " + what + ".");
    }

    [Fact]
    public async Task Signing_in_under_issued_ids_connects_this_computer_to_the_shared_register()
    {
        var folder = Files.TempFolder();
        var year = DateTime.Now.Year;
        var firebase = new FakeFirebase();
        firebase.Put(new RegisterEntry { Id = Naming.EmployeeId(year, 124), Year = year, Seq = 124, Name = "Juan dela Cruz", Title = "Driver", IssuedAt = "2026-11-03T02:00:00.000Z" });
        var window = Ui.Run(() => OpenShared(folder, firebase, new FakeKeeper()));
        Ui.Settle();

        // nobody has signed in on this computer: the panel asks, and the number is checked here only
        Ui.Run(() =>
        {
            Click(window.RailRegister);
            Assert.Equal(Visibility.Visible, window.ToolRegister.Visibility);
            Assert.Equal(Visibility.Visible, window.SignInForm.Visibility);
            Assert.Equal(Visibility.Collapsed, window.SignedInActions.Visibility);
            Assert.Equal(Visibility.Collapsed, window.RefusedPanel.Visibility);
            Assert.Contains("Sign in with your Keystone account", window.RegisterAccount.Text);
            window.Session.SetSeq("124");
        });
        await Until(() => window.Session.DupShared == SharedAnswer.Unreachable, "the form to say the shared register was not checked");
        Assert.Empty(firebase.Requests);                              // nothing is sent as nobody

        // a wrong password says what to do, keeps nothing, and leaves the form there to try again
        Ui.Run(() =>
        {
            window.TxtSignInEmail.Text = FakeFirebase.Email;
            window.TxtSignInPassword.Password = "not the password";
            Click(window.BtnSignIn);
        });
        await Until(() => window.BtnSignIn.IsEnabled && window.RegisterUrlMsg.Text.Contains("not right"), "the wrong password to be refused");
        Ui.Run(() =>
        {
            Assert.Contains("email or password is not right", window.RegisterUrlMsg.Text);
            Assert.Equal("", window.TxtSignInPassword.Password);      // the password does not stay in the box
            Assert.Equal(Visibility.Visible, window.SignInForm.Visibility);
            Assert.False(File.Exists(Path.Combine(folder, "register-signin.json")));
        });

        // the right one signs in, and the number somebody else holds is now known here
        Ui.Run(() =>
        {
            window.TxtSignInPassword.Password = FakeFirebase.Password;
            Click(window.BtnSignIn);
        });
        await Until(() => window.SignedInActions.Visibility == Visibility.Visible, "the sign-in to be accepted");
        Ui.Run(() =>
        {
            Assert.Equal(Visibility.Collapsed, window.SignInForm.Visibility);
            Assert.Equal($"Signed in as {FakeFirebase.Email}.", window.RegisterAccount.Text);
            Assert.Contains("Signed in.", window.RegisterUrlMsg.Text);
            Assert.Equal("", window.TxtSignInPassword.Password);
            Assert.Equal(SharedAnswer.Taken, window.Session.DupShared);
            Assert.Contains("already issued to Juan dela Cruz", window.Session.Problems()[Field.Seq]);
            Shoot(window, "register-signed-in");
        });

        // what is kept is wrapped, and is not the password
        var kept = File.ReadAllText(Path.Combine(folder, "register-signin.json"));
        Assert.DoesNotContain(FakeFirebase.Password, kept);
        Assert.DoesNotContain("renewal-", kept);

        // asking the register how it is says how many cards it holds
        Ui.Run(() => Click(window.BtnRegisterTest));
        await Until(() => window.BtnRegisterTest.IsEnabled && window.RegisterUrlMsg.Text.Contains("answering"), "the register to answer");
        Ui.Run(() => Assert.Contains("1 card in it", window.RegisterUrlMsg.Text));

        // signing out forgets the sign-in and goes back to asking
        Ui.Run(() =>
        {
            Click(window.BtnSignOut);
            Assert.Equal(Visibility.Visible, window.SignInForm.Visibility);
            Assert.Equal(Visibility.Collapsed, window.SignedInActions.Visibility);
            Assert.False(File.Exists(Path.Combine(folder, "register-signin.json")));
            window.Close();
        });
    }

    [Fact]
    public async Task A_card_the_register_refused_is_put_in_front_of_the_operator_until_it_is_dealt_with()
    {
        var folder = Files.TempFolder();
        var year = DateTime.Now.Year;
        var number = Naming.EmployeeId(year, 200);
        var firebase = new FakeFirebase();
        var keeper = new FakeKeeper();

        // yesterday: this computer, signed in but offline, issued a card that is still waiting to be written…
        var before = new Register(new FileStore(folder), firebase.Http, keeper) { Project = FakeFirebase.Project };
        Assert.True((await before.SignIn(FakeFirebase.Email, FakeFirebase.Password)).Ok);
        firebase.Offline = true;
        var mine = new RegisterEntry { Id = number, Year = year, Seq = 200, Name = "Ana Reyes", Title = "HR Associate", IssuedAt = "2026-11-16T08:00:00.000Z" };
        Assert.Equal(SharedOutcome.Waiting, (await before.RecordShared(mine)).Outcome);
        // …and meanwhile another computer issued the same number
        firebase.Offline = false;
        firebase.Put(new RegisterEntry { Id = number, Year = year, Seq = 200, Name = "Juan dela Cruz", Title = "Driver", IssuedAt = "2026-11-16T08:30:00.000Z" });

        // today the app opens with the register in reach: the waiting card is sent, refused, and shown without being asked for
        var window = Ui.Run(() => OpenShared(folder, firebase, keeper));
        await Until(() => window.RefusedPanel.Visibility == Visibility.Visible, "the refused card to be shown");
        Ui.Run(() =>
        {
            Assert.Equal(Visibility.Visible, window.Drawer.Visibility);
            Assert.Equal(Visibility.Visible, window.ToolRegister.Visibility);
            Assert.Contains($"{number} belongs to Juan dela Cruz", window.RefusedNote.Text);
            Assert.Contains("The card made here for Ana Reyes is a duplicate", window.RefusedNote.Text);
            Assert.Equal(Visibility.Visible, window.SignedInActions.Visibility);
            Assert.Equal($"Signed in as {FakeFirebase.Email}.", window.RegisterAccount.Text);     // nothing left waiting
            Shoot(window, "register-refused");
        });
        Assert.Equal("Juan dela Cruz", firebase.Get(number)!.Name);   // the first card stands
        Assert.True(File.Exists(Path.Combine(folder, "register-refused.json")));
        Assert.False(File.Exists(Path.Combine(folder, "register-outbox.json")));

        // it stays until somebody says it has been dealt with
        Ui.Run(() =>
        {
            Click(window.BtnRefusedDone);
            Assert.Equal(Visibility.Collapsed, window.RefusedPanel.Visibility);
            window.Close();
        });
        Assert.False(File.Exists(Path.Combine(folder, "register-refused.json")));
    }

    [Fact]
    public async Task A_card_can_be_made_from_start_to_saved_pdf()
    {
        var folder = Files.TempFolder();
        var window = Ui.Run(() => Open(folder));
        Ui.Settle();
        var asked = 0;

        Ui.Run(() =>
        {
            Assert.NotNull(window.Art);
            Assert.Equal(Visibility.Visible, window.CardBox.Visibility);
            Assert.NotNull(window.CardImage.Source);

            // pressing Next on an empty form says what is missing, and stays put
            Click(window.BtnNext);
            Assert.Equal(0, window.Session.StepIndex);
            Assert.Equal(Visibility.Visible, window.Blocked.Visibility);
            Assert.Equal("Please complete the highlighted fields.", window.Blocked.Text);
            Assert.Equal("Required.", window.MsgLast.Text);

            window.TxtLast.Text = "reyes";
            window.TxtFirst.Text = "Ana";
            Shoot(window, "step-1-name");
            Click(window.BtnNext);
            Assert.Equal(1, window.Session.StepIndex);
            Assert.Equal(Visibility.Collapsed, window.Blocked.Visibility);

            // the step first asks who the ID is for, and does not move on without an answer
            Click(window.BtnNext);
            Assert.Equal(1, window.Session.StepIndex);
            Assert.Equal("Please complete the highlighted fields.", window.Blocked.Text);
            Assert.Equal("Required.", window.MsgKind.Text);
            Assert.Null(window.CmbKind.SelectedItem);
            window.CmbKind.SelectedIndex = 1;
            Assert.Equal(PersonKind.Intern, window.Session.Kind);
            window.CmbKind.SelectedIndex = 0;
            Assert.Equal(PersonKind.Employee, window.Session.Kind);

            // the job-title chips, then the number on the same page
            var chip = window.TitleChips.Children.OfType<RadioButton>().First(c => (string)c.Content == "HR Associate");
            chip.IsChecked = true;
            Click(chip);
            Assert.Equal("HR Associate", window.Session.JobTitle);
            Shoot(window, "step-2-job");
            // the job alone is not the whole step: the number is asked for beside it
            Click(window.BtnNext);
            Assert.Equal((int)Step.Title, window.Session.StepIndex);
            Assert.Equal("Required.", window.MsgSeq.Text);
            window.TxtSeq.Text = "12x4";
            Assert.Equal("124", window.Session.Seq);
            Shoot(window, "step-2-number");
            Click(window.BtnNext);
            Assert.Equal((int)Step.Photo, window.Session.StepIndex);
        });

        await Ui.RunAsync(() => window.AcceptPhotoFile(Path.Combine(Files.RepoRoot, "assets", "example-cut-out.png")));
        Ui.Run(() =>
        {
            Assert.True(window.Session.Photo is not null, window.Session.PhotoError);
            Assert.NotNull(window.PhotoThumb.Source);
            Assert.Equal(Visibility.Visible, window.AdjustBox.Visibility);
            Shoot(window, "step-3-photo");
            Click(window.BtnNext);
            Assert.Equal((int)Step.Sign, window.Session.StepIndex);

            // signing: the full-screen window is stood in for, and asked for exactly once
            window.AskForSignature = (_, _) => { asked++; return Session.ExampleSignature(); };
            Click(window.BtnSign);
            Assert.Equal(1, asked);
            Assert.True(window.Session.Signature is { HasInk: true });
            Assert.True(window.SigPreview.HasInk);
            Shoot(window, "step-4-signature");
            Click(window.BtnNext);
            Assert.Equal(Session.LastStep, window.Session.StepIndex);
            Assert.Equal("Reyes", window.Session.Last);               // capitalised on the way past
            Assert.Equal("Ana Reyes", window.SumName.Text);
            Assert.Equal("APG-" + DateTime.Now.Year + "-124", window.SumNumber.Text);
            Assert.Equal(Visibility.Visible, window.SheetImage.Visibility);
            // drawn the moment the step opens, at its final size: the stage is never empty, and nothing then
            // shrinks it (the page buttons' row is there from the start)
            Assert.NotNull(window.SheetImage.Source);
            Assert.NotEqual(Visibility.Collapsed, window.SheetNav.Visibility);
        });
        Ui.Settle();
        await Task.Delay(400);                                        // the sheet preview is drawn a moment after the step opens
        Ui.Settle();

        var pdf = Path.Combine(folder, "out", "card.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(pdf)!);
        Ui.Run(() => { Assert.NotNull(window.SheetImage.Source); Shoot(window, "step-5-check"); });
        await Ui.RunAsync(() => window.SavePrint(pdf));

        Assert.True(File.Exists(pdf));
        Assert.False(File.Exists(pdf + ".part"));
        var head = new byte[8];
        using (var f = File.OpenRead(pdf)) f.ReadExactly(head);
        Assert.Equal("%PDF-1.4", Encoding.ASCII.GetString(head));
        Assert.True(new FileInfo(pdf).Length > 50_000);

        Ui.Run(() =>
        {
            Assert.Equal(Visibility.Visible, window.SavedStage.Visibility);
            Assert.Equal("card.pdf", window.SavedName.Text);
            // the gold button at the right edge is now the next piece of work
            Assert.Equal(Visibility.Visible, window.BtnNewId.Visibility);
            Assert.Equal(Visibility.Collapsed, window.BtnDownload.Visibility);
            Shoot(window, "step-5-saved");
            var issued = new Register(new FileStore(folder)) { Project = null }.All();
            Assert.Single(issued);
            Assert.Equal("Ana Reyes", issued[0].Name);

            // starting again: the same number is now refused
            Click(window.BtnNewId);
            Assert.Equal(0, window.Session.StepIndex);
            Assert.Equal("", window.TxtLast.Text);
            window.Close();
        });
    }

    [Fact]
    public async Task A_print_that_cannot_be_saved_issues_nothing()
    {
        var folder = Files.TempFolder();
        var window = Ui.Run(() => Open(folder));
        Ui.Settle();
        Ui.Run(() =>
        {
            window.Session.LoadExample(null, 2026);                   // an example is never recorded…
            window.Session.NewId();
            window.TxtLast.Text = "Reyes";
            window.TxtFirst.Text = "Ana";
            window.Session.PickKind(PersonKind.Employee);
            window.Session.PickRole1("HR Associate");
            window.Session.SetSeq("7");
            window.Session.AcceptPhoto(new PhotoResult(new RgbaImage(8, 8), "p.png", PhotoBackground.Transparent,
                new PhotoCheck { Ok = true, Placement = new PhotoPlacement(20, 12, 30, 50, 0) }));
            window.Session.SetSignature(Session.ExampleSignature());
            window.Session.GoStep(Session.LastStep);
            window.Render();
        });
        // a folder that does not exist: the write fails
        var bad = Path.Combine(folder, "no-such-folder", "card.pdf");
        await Ui.RunAsync(() => window.SavePrint(bad));
        Ui.Run(() =>
        {
            Assert.Equal(Visibility.Visible, window.ReviewError.Visibility);
            Assert.Contains("couldn’t be saved", window.ReviewError.Text);
            Assert.Equal(Visibility.Collapsed, window.SavedStage.Visibility);
            Assert.Empty(new Register(new FileStore(folder)) { Project = null }.All());
            Assert.Single(window.Session.JobCards());                 // …and the card is still there to try again
            window.Close();
        });
    }

    [Fact]
    public void Every_tool_opens_and_the_map_can_be_moved_kept_and_reset()
    {
        var folder = Files.TempFolder();
        var window = Ui.Run(() => Open(folder));
        Ui.Settle();
        Ui.Run(() =>
        {
            foreach (var (rail, panel) in new (ToggleButton, FrameworkElement)[]
                     { (window.RailRegister, window.ToolRegister), (window.RailBatch, window.ToolBatch), (window.RailPrinter, window.ToolPrinter), (window.RailDesign, window.ToolDesign) })
            {
                Click(rail);
                Assert.Equal(Visibility.Visible, window.Drawer.Visibility);
                Assert.Equal(Visibility.Visible, panel.Visibility);
                Assert.True(rail.IsChecked);
            }

            // the map: open it, move the QR box, keep it
            Click(window.BtnTplMap);
            Assert.Equal(Visibility.Visible, window.MapOverlay.Visibility);
            Assert.False(window.FormPanel.IsEnabled);                 // the form stands down while boxes are dragged
            window.Templates.SetBox(BoxKey.Qr, new RectMm(40, 2, 9, 9));
            window.MapOverlay.Refresh();
            Click(window.BtnTplDone);
            Assert.Equal(Visibility.Collapsed, window.MapOverlay.Visibility);
            Assert.True(window.FormPanel.IsEnabled);
            Assert.Equal(40, window.Session.Geometry.Qr.X);
            Assert.True(File.Exists(Path.Combine(folder, "template.json")));

            // closing the drawer another way while editing must not strand the boxes on the card
            Click(window.BtnTplMap);
            Click(window.RailPrinter);
            Assert.Equal(Visibility.Collapsed, window.MapOverlay.Visibility);
            Assert.True(window.FormPanel.IsEnabled);

            // discarding puts back what was there when the editor opened
            Click(window.RailDesign);
            Click(window.BtnTplMap);
            window.Templates.SetBox(BoxKey.Qr, new RectMm(10, 10, 9, 9));
            Click(window.BtnTplCancel);
            Assert.Equal(40, window.Session.Geometry.Qr.X);

            Click(window.BtnTplReset);
            Assert.False(window.Templates.IsCustom);
            Assert.Equal(CardConfig.Qr.X, window.Session.Geometry.Qr.X);

            // once an ID is started the design can no longer be changed
            window.TxtLast.Text = "R";
            Assert.Equal(Visibility.Collapsed, window.RailDesign.Visibility);
            Assert.Equal(Visibility.Collapsed, window.Drawer.Visibility);
            window.Close();
        });
    }

    [Fact]
    public void A_tool_opens_over_the_card_and_turning_the_card_over_leaves_it_where_it_is()
    {
        var folder = Files.TempFolder();
        var window = Ui.Run(() => Open(folder));
        Ui.Settle();
        Ui.Run(() =>
        {
            Rect Place(FrameworkElement e) => new(e.TranslatePoint(new Point(0, 0), window), new Size(e.ActualWidth, e.ActualHeight));
            window.UpdateLayout();
            var card = Place(window.CardBox);
            var form = Place(window.FormPanel);

            // each tool opens over the dimmed card: neither the card nor the form moves or changes size
            foreach (var rail in new[] { window.RailRegister, window.RailBatch, window.RailPrinter, window.RailDesign })
            {
                Click(rail);
                window.UpdateLayout();
                Assert.Equal(Visibility.Visible, window.Drawer.Visibility);
                Assert.Equal(Visibility.Visible, window.Veil.Visibility);
                Assert.Equal(card, Place(window.CardBox));
                Assert.Equal(form, Place(window.FormPanel));
            }
            Shoot(window, "tool-over-card");

            // a click on the dimmed card puts the tool away
            window.Veil.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            Assert.Equal(Visibility.Collapsed, window.Drawer.Visibility);
            Assert.Equal(Visibility.Collapsed, window.Veil.Visibility);

            // turning the card over changes the side shown, not the card's size or place
            window.SideBack.IsChecked = true;
            Click(window.SideBack);
            window.UpdateLayout();
            Assert.Equal(card, Place(window.CardBox));
            Assert.Equal(Visibility.Collapsed, window.FocusLayer.Visibility);     // the field outline is for the front
            window.SideFront.IsChecked = true;
            Click(window.SideFront);
            window.UpdateLayout();
            Assert.Equal(card, Place(window.CardBox));
            Assert.Equal(Visibility.Visible, window.FocusLayer.Visibility);
            window.Close();
        });
    }

    [Fact]
    public void The_operator_can_keep_the_window_light_or_dark_whatever_windows_is_set_to()
    {
        var folder = Files.TempFolder();
        try
        {
            // the app the test host starts reads this computer's own settings, where a choice may already be kept: start from the default
            var window = Ui.Run(() => { ThemeManager.Choose(ThemeManager.MatchWindows); return Open(folder); });
            Ui.Settle();
            Ui.Run(() =>
            {
                Assert.True(window.AppearSystem.IsChecked);                 // Match Windows
                window.AppearDark.IsChecked = true;
                Click(window.AppearDark);
                Assert.Equal(AppTheme.Dark, ThemeManager.Current);
                Assert.Equal(ThemeManager.AlwaysDark, new FileStore(folder).Get(ThemeManager.ChoiceKey));
                Shoot(window, "appearance-dark");
                window.Close();
            });

            // opened again on this computer, it is still dark, whatever Windows says
            var again = Ui.Run(() => { ThemeManager.Apply(AppTheme.Light); return Open(folder); });
            Ui.Settle();
            Ui.Run(() =>
            {
                Assert.Equal(AppTheme.Dark, ThemeManager.Current);
                Assert.True(again.AppearDark.IsChecked);
                Assert.False(again.AppearLight.IsChecked);

                // back to following Windows: nothing is kept for it
                again.AppearSystem.IsChecked = true;
                Click(again.AppearSystem);
                Assert.Equal(ThemeManager.MatchWindows, ThemeManager.Choice);
                Assert.Equal(ThemeManager.SystemTheme(), ThemeManager.Current);
                Assert.Null(new FileStore(folder).Get(ThemeManager.ChoiceKey));
                again.Close();
            });
        }
        finally
        {
            Ui.Run(() => { ThemeManager.Choose(ThemeManager.MatchWindows); ThemeManager.Apply(AppTheme.Light); });   // the other tests expect the default
        }
    }

    private static Color PixelOf(BitmapSource picture, int x, int y)
    {
        var px = new byte[4];
        picture.CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
        return Color.FromRgb(px[2], px[1], px[0]);
    }

    /// <summary>The colour most of an element is painted, on a grid of samples.
    /// One fixed point can land on a heading; the background is what most of it shows.</summary>
    private static Color MostOf(BitmapSource picture, FrameworkElement element, Window window)
    {
        var counts = new Dictionary<Color, int>();
        for (var gx = 1; gx < 10; gx++)
            for (var gy = 1; gy < 10; gy++)
            {
                var at = element.TranslatePoint(new Point(element.ActualWidth * gx / 10, element.ActualHeight * gy / 10), window);
                // a panel that scrolls reaches past the window: count only what is on screen
                if (at.X < 0 || at.Y < 0 || at.X >= picture.PixelWidth || at.Y >= picture.PixelHeight) continue;
                // SAFETY: the point was just checked to lie inside the picture
                var c = PixelOf(picture, (int)at.X, (int)at.Y);
                counts[c] = counts.GetValueOrDefault(c) + 1;
            }
        Assert.True(counts.Values.Sum() >= 20, $"only {counts.Values.Sum()} of 81 sample points were on screen");
        return counts.OrderByDescending(kv => kv.Value).First().Key;
    }

    /// <summary>Keeps a picture of the window as it stands, for a person to look at in the report.
    /// The only photograph in these is the repository's own example cut-out.</summary>
    private static void Shoot(Window window, string name)
    {
        window.UpdateLayout();
        ImageFiles.SavePng(Snapshot(window), Path.Combine(Files.ReportDirectory, name + ".png"));
    }

    private static BitmapSource Snapshot(Window window)
    {
        var shot = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        shot.Render(window);
        return shot;
    }

    [Fact]
    public void The_window_follows_light_and_dark_mode_without_reopening()
    {
        var folder = Files.TempFolder();
        try
        {
            // whatever this machine is set to, reading it must not fail
            Ui.Run(() => Assert.True(Enum.IsDefined(ThemeManager.SystemTheme())));

            var window = Ui.Run(() => { ThemeManager.Apply(AppTheme.Dark); return Open(folder); });
            Ui.Settle();
            Ui.Run(() =>
            {
                Assert.Equal(AppTheme.Dark, ThemeManager.Current);
                var darkSurface = ((SolidColorBrush)window.FormPanel.Background).Color;
                Assert.Equal(Color.FromRgb(0x15, 0x16, 0x13), darkSurface);
                Assert.Equal(Color.FromRgb(0xF6, 0xF2, 0xE9), ((SolidColorBrush)window.Foreground).Color);
                // a border set from code, by name, follows too (gold-soft, the quiet outline)
                Assert.Equal(Color.FromRgb(0x8D, 0x75, 0x3E), ((SolidColorBrush)window.TxtLast.BorderBrush).Color);

                Shoot(window, "window-dark-form");
                // open the printer setting, which shows a drop-down list and a tick box drawn by the theme
                Click(window.RailPrinter);
                window.UpdateLayout();
            });
            Ui.Settle();
            Ui.Run(() =>
            {
                var shot = Snapshot(window);
                ImageFiles.SavePng(shot, Path.Combine(Files.ReportDirectory, "window-dark.png"));
                // the panel shows its surface and the fields on it; either way, the dark palette's own
                var darkGrounds = new[] { Color.FromRgb(0x09, 0x0A, 0x09), Color.FromRgb(0x15, 0x16, 0x13) };
                Assert.Contains(MostOf(shot, window.Drawer, window), darkGrounds);
                // the tool opens over the card, on a veil, and the form stays where it is at any width
                Assert.Equal(Visibility.Visible, window.Veil.Visibility);
                Assert.Equal(Visibility.Visible, window.FormPanel.Visibility);
                var list = window.CmbFlip.TranslatePoint(new Point(window.CmbFlip.ActualWidth - 50, window.CmbFlip.ActualHeight / 2), window);
                var listPixel = PixelOf(shot, (int)list.X, (int)list.Y);
                Assert.True(listPixel.R < 60 && listPixel.G < 60, $"the drop-down list should be dark in dark mode, but is {listPixel}");

                // Windows switches back to light: the same window, still open, follows
                ThemeManager.Apply(AppTheme.Light);
            });
            Ui.Settle();
            Ui.Run(() =>
            {
                Assert.Equal(Colors.White, ((SolidColorBrush)window.FormPanel.Background).Color);
                Assert.Equal(Color.FromRgb(0x8D, 0x75, 0x3E), ((SolidColorBrush)window.TxtLast.BorderBrush).Color);
                var shot = Snapshot(window);
                ImageFiles.SavePng(shot, Path.Combine(Files.ReportDirectory, "window-light.png"));
                var lightGrounds = new[] { Color.FromRgb(0xF5, 0xF5, 0xF2), Colors.White };
                Assert.Contains(MostOf(shot, window.Drawer, window), lightGrounds);
                // closing the tool brings the form back
                Click(window.DrawerClose);
                window.UpdateLayout();
                Assert.Equal(Visibility.Visible, window.FormPanel.Visibility);
                Assert.Contains(MostOf(Snapshot(window), window.FormPanel, window), lightGrounds);
                window.Close();
            });
        }
        finally
        {
            Ui.Run(() => ThemeManager.Apply(AppTheme.Light));      // the other tests expect the default
        }
    }

    [Fact]
    public void The_signing_window_opens_fills_the_screen_and_closes_without_a_tablet()
    {
        Ui.Run(() =>
        {
            var sign = new SignWindow("Ana", null);
            sign.Show();
            Assert.Equal(WindowState.Maximized, sign.WindowState);
            Assert.Equal(WindowStyle.None, sign.WindowStyle);
            sign.Close();
            Assert.Null(sign.Result);
            // one sentence either way, never an empty line
            Assert.NotEqual("", SignWindow.TabletStatus(null, sawPressure: false));
            Assert.Contains("Press harder", SignWindow.TabletStatus(null, sawPressure: true));
        });
    }

    [Fact]
    public void Signing_holds_the_pointer_on_the_pad_until_a_key_lets_it_go()
    {
        Ui.Run(() =>
        {
            var sign = new SignWindow("Ana", null);
            sign.Show();
            sign.Activate();
            sign.UpdateLayout();
            Assert.False(sign.Holding);

            // the pen comes down on the pad: the pointer is held there and the status says how to finish
            Stroke(sign.Pad);
            Assert.True(sign.Holding);
            Assert.True(sign.Pad.HasInk);
            // and the keys still have somewhere to go: with nothing focused, Windows hands a key
            // to no one, and Backspace and Esc did nothing (keys below go where Windows sends them)
            Assert.Same(sign, Keyboard.FocusedElement);

            // Backspace starts again, still held
            Press(sign, Key.Back);
            Assert.False(sign.Pad.HasInk);
            Assert.True(sign.Holding);

            // Esc lets the pointer go and stays on this screen
            Press(sign, Key.Escape);
            Assert.False(sign.Holding);
            Assert.True(sign.IsVisible);
            Assert.Null(sign.Result);

            // let go, Backspace still clears, as the key cap on Clear says
            Stroke(sign.Pad);
            Press(sign, Key.Escape);
            Assert.False(sign.Holding);
            Assert.True(sign.Pad.HasInk);
            Press(sign, Key.Back);
            Assert.False(sign.Pad.HasInk);
            Assert.True(sign.IsVisible);

            // Enter, once signed, uses the signature
            Stroke(sign.Pad);
            Press(sign, Key.Enter);
            Assert.False(sign.IsVisible);
            Assert.NotNull(sign.Result);
        });

        static void Stroke(SignaturePad pad)
        {
            var at = Environment.TickCount;
            pad.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, at, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
            pad.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, at + 1, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
        }

        // a key goes to whatever has the keyboard focus, as Windows sends it; nothing focused, no key
        static void Press(Window window, Key key)
        {
            var to = Keyboard.FocusedElement as UIElement;
            Assert.True(to is not null, $"{key} was pressed with nothing focused: Windows would hand it to no one");
            to.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        }
    }
}
