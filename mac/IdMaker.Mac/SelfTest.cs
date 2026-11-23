using System.Text.Json;
using IdMaker.Core;

namespace IdMaker.Mac;

/// <summary>The checks that need a real Mac: run with `--selftest`.</summary>
internal static class SelfTest
{
    public static int Run(string? repo, bool needsRegister = false)
    {
        var failed = 0;
        void Check(string what, bool holds)
        {
            Console.WriteLine($"{(holds ? "ok  " : "FAIL")} {what}");
            if (!holds) failed++;
        }

        Check($"shares the Windows app's rules (version {CardConfig.AppVersion})", CardConfig.AppVersion.Length > 0);
        // A release must know which project the register is; which one is not printed.
        if (needsRegister) Check("this build was given the register's project", CardConfig.Register is not null);
        else Console.WriteLine(CardConfig.Register is null ? "      (built without the register's project: this copy keeps its own list)" : "      (built with the register's project)");

        MacKeeper.Forget();
        var keeper = new MacKeeper();
        var wrapped = keeper.Protect("a sign-in token");
        Check("the Keychain keeper wraps a sign-in", wrapped is not null && !wrapped.Contains("a sign-in token"));
        Check("and opens it again", wrapped is not null && keeper.Unprotect(wrapped) == "a sign-in token");
        Check("a damaged one does not open", wrapped is not null && keeper.Unprotect(string.Concat(wrapped.AsSpan(0, wrapped.Length - 4), "AAA=")) is null);
        Check("nor does something that was never wrapped", keeper.Unprotect("not wrapped") is null);
        MacKeeper.Forget();
        Check("with the key gone it no longer opens", wrapped is not null && keeper.Unprotect(wrapped) is null);

        if (repo is null)
        {
            Check("the card checks were given the repository (--repo <path>)", false);
        }
        else
        {
            var problems = FontsAgainstTheWebApp(repo, out var worst);
            foreach (var problem in problems) Check(problem, false);
            Check($"the card fonts measure as they did in the web app (worst width difference at 100 px: {worst:0.0000})", worst < 0.05);
            foreach (var (line, holds) in CardAgainstTheWebApp(repo)) Check(line, holds);
        }

        Console.WriteLine(failed == 0 ? "selftest passed" : $"selftest FAILED: {failed}");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>
    /// The same check the Windows app's RenderTests make: cap height and
    /// descents equal to the web app's, every measured width within a
    /// twentieth of a pixel at 100 px.
    /// </summary>
    private static List<string> FontsAgainstTheWebApp(string repo, out double worst)
    {
        var wrong = new List<string>();
        worst = 0;
        var measurer = MacTextMeasurer.Load(Path.Combine(repo, "windows", "IdMaker.App", "Fonts"));
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(repo, "windows", "IdMaker.Tests", "Fixtures", "web-reference.json")));
        foreach (var (font, key) in new[] { (CardFont.Name, "name"), (CardFont.Title, "title"), (CardFont.Number, "employeeNo") })
        {
            var web = reference.RootElement.GetProperty("metrics").GetProperty(key);
            void Height(string what, double want, double got)
            {
                if (want != got) wrong.Add($"{key} {what}: web {want:0.000}, here {got:0.000}");
            }
            Height("cap height", web.GetProperty("capH").GetDouble(), measurer.CapHeight(font, 100));
            Height("descent of Q,", web.GetProperty("descQ").GetDouble(), measurer.Descent(font, 100, uppercase: true));
            Height("descent of gjpqy", web.GetProperty("descg").GetDouble(), measurer.Descent(font, 100, uppercase: false));
            foreach (var width in web.GetProperty("widths").EnumerateObject())
            {
                var got = measurer.Width(font, 100, width.Name);
                var off = Math.Abs(got - width.Value.GetDouble());
                worst = Math.Max(worst, off);
                if (off >= 0.05) wrong.Add($"{key} \"{width.Name}\": web {width.Value.GetDouble():0.000}, here {got:0.000}");
            }
        }
        return wrong;
    }

    // How far two pictures of the same size are apart inside a box (mm): the
    // mean difference per channel, and the share of pixels that differ visibly.
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

    /// <summary>
    /// The example card drawn here against the card the web app printed
    /// (`test/golden-front.png`): the same zones and the same limits the
    /// Windows app's RenderTests hold theirs to, and each line of text's ink
    /// box within a pixel and a half. The drawing is saved beside the report
    /// for a person to look at.
    /// </summary>
    private static List<(string Line, bool Holds)> CardAgainstTheWebApp(string repo)
    {
        var lines = new List<(string, bool)>();
        var renderer = new MacCardRenderer(MacTextMeasurer.Load(Path.Combine(repo, "windows", "IdMaker.App", "Fonts")));
        var frontArt = MacPictures.Load(Path.Combine(repo, "assets", CardConfig.FrontArtFile));
        var backArt = MacPictures.Load(Path.Combine(repo, "assets", CardConfig.BackArtFile));
        var (cutOut, error) = MacPictures.Decode(Path.Combine(repo, "assets", "example-cut-out.png"));
        var goldenFile = MacPictures.Load(Path.Combine(repo, "test", "golden-front.png"));
        if (frontArt is null || backArt is null || cutOut is null || goldenFile is null)
        {
            lines.Add(($"the artwork, the example photo and the reference card open ({error})", false));
            return lines;
        }
        var art = new CardArt(frontArt, backArt);
        var photo = PhotoPipeline.Analyse(cutOut, "example-cut-out.png", CardConfig.Photo);
        lines.Add(($"the example photo passes its checks {photo.Check.Problem}", photo.Check.Ok));
        var card = new CardData("Christopher Joseph V. Aureo", "IT Associate", 2026, 122, photo.Image, photo.Check.Placement, Session.ExampleSignature());

        var ppm = CardConfig.PrintPxPerMm;
        var front = renderer.RenderFront(art, CardGeometry.BuiltIn, card, ppm);
        var golden = MacPictures.ToRgba(goldenFile);
        var report = Environment.GetEnvironmentVariable("IDMAKER_REPORT_DIR");
        if (!string.IsNullOrEmpty(report))
        {
            Directory.CreateDirectory(report);
            MacPictures.SavePng(front, Path.Combine(report, "front-mac.png"));
        }
        lines.Add(($"the card is {golden.Width} x {golden.Height} pixels, as the web app's is (here {front.Width} x {front.Height})", front.Width == golden.Width && front.Height == golden.Height));
        if (front.Width != golden.Width || front.Height != golden.Height) return lines;

        var w = front.Width;
        var zones = new (string Name, RectMm Box, double MaxMean, double MaxChanged)[]
        {
            ("whole card", new RectMm(0, 0, CardConfig.CardWidthMm, CardConfig.CardHeightMm), 4.0, 0.02),
            ("photo", new RectMm(17, 10, 36.5, 47), 6.0, 0.03),
            ("name", CardConfig.NameText.Box, 14.0, 0.08),
            ("job title", CardConfig.TitleText.Box, 14.0, 0.08),
            ("employee number", CardConfig.EmployeeNoText.Box, 14.0, 0.12),
            ("signature", CardConfig.Signature.Box, 14.0, 0.08),
            ("QR", new RectMm(CardConfig.Qr.X, CardConfig.Qr.Y, CardConfig.Qr.Size, CardConfig.Qr.Size), 1.0, 0.002),
        };
        foreach (var zone in zones)
        {
            var (mean, changed) = Compare(front.Data, golden.Data, w, zone.Box, ppm);
            lines.Add(($"{zone.Name}: mean difference {mean:0.00} (limit {zone.MaxMean:0.0}), pixels visibly different {changed:P2} (limit {zone.MaxChanged:P1})",
                mean <= zone.MaxMean && changed <= zone.MaxChanged));
        }

        // Where the ink is: the pixels that differ from the bare artwork.
        var bare = renderer.RenderFront(art, CardGeometry.BuiltIn, null, ppm).Data;
        (int L, int T, int R, int B) Ink(byte[] picture, RectMm box)
        {
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
            var here = Ink(front.Data, box);
            var there = Ink(golden.Data, box);
            var close = here.R >= 0 && Math.Abs(here.L - there.L) <= 1.5 && Math.Abs(here.T - there.T) <= 1.5 && Math.Abs(here.R - there.R) <= 1.5 && Math.Abs(here.B - there.B) <= 1.5;
            lines.Add(($"{name}: ink box here {here}, web {there}", close));
        }

        // The preview is the print drawn smaller, and the back turns.
        var small = renderer.RenderFront(art, CardGeometry.BuiltIn, card, 6);
        lines.Add(($"the preview at 6 px/mm is 324 x 514 (here {small.Width} x {small.Height})", small.Width == 324 && small.Height == 514));
        var back = renderer.RenderBack(art, 6, rotate180: false).Data;
        var turned = renderer.RenderBack(art, 6, rotate180: true).Data;
        var last = back.Length - 4;
        var same = Math.Abs(back[0] - turned[last]) <= 2 && Math.Abs(back[1] - turned[last + 1]) <= 2 && Math.Abs(back[2] - turned[last + 2]) <= 2;
        lines.Add(("turned upside down, the back's first pixel is its last", same));
        return lines;
    }
}
