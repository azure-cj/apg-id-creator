using System.IO.Compression;
using System.Text;
using System.Text.Json;
using IdMaker.Core;

namespace IdMaker.Tests;

public class NamingTests
{
    private static readonly JsonElement Web = Repo.WebReference;

    [Fact]
    public void Names_are_composed_as_the_web_app_composed_them()
    {
        Assert.Equal(Web.GetProperty("names")[0].GetString(), Naming.ComposeName(new NameParts("Aureo", "Christopher Joseph", "Villanueva")));
        Assert.Equal(Web.GetProperty("names")[1].GetString(), Naming.ComposeName(new NameParts("dela Cruz", "Juan", "", "Jr.")));
        Assert.Equal("", Naming.ComposeName(new NameParts()));
    }

    [Fact]
    public void Two_roles_merge_on_the_words_they_share()
    {
        var want = Web.GetProperty("titles").EnumerateArray().Select(t => t.GetString()).ToArray();
        Assert.Equal(want, new[]
        {
            Naming.ComposeTitle("IT Associate", "Admin Associate"),
            Naming.ComposeTitle("IT Associate", "Driver"),
            Naming.ComposeTitle("IT Associate", ""),
            Naming.ComposeTitle("Senior IT Associate", "Junior Admin Associate"),
            Naming.ComposeTitle("Driver", "driver"),
        });
    }

    [Fact]
    public void Lower_case_names_are_capitalised_and_anything_with_a_capital_is_left_alone()
    {
        foreach (var c in Web.GetProperty("caps").EnumerateArray())
        {
            var typed = c[0].GetString()!;
            Assert.Equal(c[1].GetString(), Naming.AutoCapitalise(typed));
            Assert.Equal(c[2].GetString(), Naming.AutoCapitalise(typed, suffix: true));
        }
    }

    [Fact]
    public void File_names_carry_the_number_and_a_plain_surname()
    {
        var want = Web.GetProperty("files").EnumerateArray().Select(t => t.GetString()).ToArray();
        Assert.Equal(want, new[]
        {
            Naming.PdfFileName(2026, 122, "Dela Cruz-Santos"),
            Naming.PdfFileNameFromWholeName(2026, 7, "Juan dela Cruz Jr."),
            Naming.PdfFileNameFromWholeName(2026, 7, "Christopher Joseph V. Aureo"),
            Naming.PdfFileNameFromWholeName(2026, 7, "Ana Peña-Ñandú"),
        });
    }

    [Fact]
    public void Employee_numbers_are_padded_and_a_missing_one_shows_blanks()
    {
        var want = Web.GetProperty("empLine").EnumerateArray().Select(t => t.GetString()).ToArray();
        Assert.Equal(want, new[] { Naming.EmployeeNoLine(2026, 122), Naming.EmployeeNoLine(2026, null), Naming.EmployeeId(2026, 7) });
    }
}

public class QrTests
{
    [Fact]
    public void Every_symbol_matches_the_library_the_web_app_used_module_for_module()
    {
        var cases = Repo.WebReference.GetProperty("qrs").EnumerateArray().ToList();
        Assert.Equal(5, cases.Count);
        foreach (var c in cases)
        {
            var qr = QrCode.Encode(c.GetProperty("s").GetString()!, c.GetProperty("ec").GetString()![0]);
            Assert.Equal(c.GetProperty("size").GetInt32(), qr.Size);
            Assert.Equal(c.GetProperty("rows").EnumerateArray().Select(r => r.GetString()).ToArray(), qr.ToRows());
        }
    }

    [Fact]
    public void The_card_s_own_code_is_the_one_printed_before()
    {
        var qr = QrCode.Encode(CardConfig.Qr.Content, CardConfig.Qr.ErrorCorrection);
        var want = Repo.WebReference.GetProperty("qr");
        Assert.Equal(want.GetProperty("rows").EnumerateArray().Select(r => r.GetString()).ToArray(), qr.ToRows());
    }

    [Fact]
    public void Text_too_long_for_any_symbol_is_refused()
    {
        Assert.Throws<ArgumentException>(() => QrCode.Encode(new string('x', 4000), 'H'));
    }
}

public class SheetTests
{
    [Fact]
    public void Every_paper_and_layout_plans_as_the_web_app_planned_it()
    {
        foreach (var p in Repo.WebReference.GetProperty("plans").EnumerateObject())
        {
            var parts = p.Name.Split('/');
            var plan = Sheets.Plan(parts[0], parts[1] == "pairs" ? SheetMode.Pairs : SheetMode.Duplex)!;
            var want = p.Value;
            Assert.Equal(want.GetProperty("cols").GetInt32(), plan.Cols);
            Assert.Equal(want.GetProperty("rows").GetInt32(), plan.Rows);
            Assert.Equal(want.GetProperty("landscape").GetBoolean(), plan.Landscape);
            Assert.Equal(want.GetProperty("turned").GetBoolean(), plan.Turned);
            Assert.Equal(want.GetProperty("left").GetDouble(), plan.Left, 6);
            Assert.Equal(want.GetProperty("top").GetDouble(), plan.Top, 6);
            Assert.Equal(want.GetProperty("pw").GetDouble(), plan.PageWidthMm, 6);
        }
    }

    [Fact]
    public void Backs_are_mirrored_by_column_for_a_long_edge_turn_and_by_row_for_a_short_edge_turn()
    {
        var plan = Sheets.Plan("A4", SheetMode.Duplex)!;         // 3 x 3
        var longEdge = Sheets.Pages(2, plan, DuplexFlip.LongEdge);
        Assert.Equal(2, longEdge.Count);
        Assert.False(longEdge[0].Backs);
        Assert.True(longEdge[1].Backs);
        // the first card's front is top-left, so its back is top-right
        Assert.Equal(plan.Left, longEdge[0].Slots[0].X, 6);
        Assert.Equal(plan.Left + 2 * (plan.CellW + CardConfig.SheetGapMm), longEdge[1].Slots[0].X, 6);
        Assert.Equal(longEdge[0].Slots[0].Y, longEdge[1].Slots[0].Y, 6);

        var shortEdge = Sheets.Pages(2, plan, DuplexFlip.ShortEdge);
        Assert.Equal(shortEdge[0].Slots[0].X, shortEdge[1].Slots[0].X, 6);
        Assert.Equal(plan.Top + 2 * (plan.CellH + CardConfig.SheetGapMm), shortEdge[1].Slots[0].Y, 6);
    }

    [Fact]
    public void Pairs_put_each_back_beside_its_front_and_spill_onto_more_sheets()
    {
        var plan = Sheets.Plan("A4", SheetMode.Pairs)!;          // 2 x 2 pairs
        var pages = Sheets.Pages(5, plan, DuplexFlip.LongEdge);
        Assert.Equal(2, pages.Count);
        Assert.Equal(8, pages[0].Slots.Count);
        Assert.Equal(2, pages[1].Slots.Count);
        var front = pages[0].Slots[0];
        var back = pages[0].Slots[1];
        Assert.Equal(front.Entry, back.Entry);
        Assert.True(back.Back);
        Assert.Equal(front.X + plan.CardW + CardConfig.SheetGapMm, back.X, 6);
        Assert.Equal(4, pages[1].Slots[0].Entry);
    }

    [Fact]
    public void Nothing_is_placed_off_the_page()
    {
        foreach (var paper in CardConfig.Papers)
        {
            foreach (var mode in new[] { SheetMode.Pairs, SheetMode.Duplex })
            {
                var plan = Sheets.Plan(paper.Key, mode)!;
                foreach (var page in Sheets.Pages(plan.PerSheet, plan, DuplexFlip.LongEdge))
                {
                    foreach (var s in page.Slots)
                    {
                        Assert.InRange(s.X, CardConfig.SheetMarginMm - 1e-9, plan.PageWidthMm - plan.CardW - CardConfig.SheetMarginMm + 1e-9);
                        Assert.InRange(s.Y, CardConfig.SheetMarginMm - 1e-9, plan.PageHeightMm - plan.CardH - CardConfig.SheetMarginMm + 1e-9);
                    }
                }
            }
        }
    }
}

public class TextFitTests
{
    private static readonly FakeMeasurer M = new();

    [Fact]
    public void Short_text_stays_on_one_line_at_the_largest_size()
    {
        var fit = TextFit.Fit(M, CardConfig.NameText, "Ana Li");
        Assert.True(fit.Fits);
        Assert.Equal(new[] { "Ana Li" }, fit.Lines);
        Assert.Equal(CardConfig.NameText.MaxSizePt, fit.SizePt);
    }

    [Fact]
    public void A_long_name_wraps_into_lines_of_even_width_rather_than_one_long_and_one_short()
    {
        var fit = TextFit.Fit(M, CardConfig.NameText, "Christopher Joseph V. Aureo");
        Assert.True(fit.Fits);
        Assert.True(fit.Lines.Count >= 2);
        Assert.Equal("Christopher Joseph V. Aureo", string.Join(' ', fit.Lines));
        var widths = fit.Lines.Select(l => l.Length).ToList();
        Assert.True(widths.Max() - widths.Min() <= 9, "the lines should be balanced: " + string.Join(" / ", fit.Lines));
        // every line fits the box at the chosen size
        var px = fit.SizePt * CardConfig.PtToMm * CardConfig.PrintPxPerMm;
        Assert.All(fit.Lines, l => Assert.True(M.Width(CardFont.Name, px, l) <= CardConfig.NameText.Box.W * CardConfig.PrintPxPerMm));
    }

    [Fact]
    public void Text_that_cannot_fit_is_drawn_at_the_smallest_size_and_reported()
    {
        var fit = TextFit.Fit(M, CardConfig.NameText, "Bartholomew Maximilian Q. Featherstonehaugh-Cholmondeley-Marjoribanks");
        Assert.False(fit.Fits);
        Assert.Equal(CardConfig.NameText.MinSizePt, fit.SizePt);
        Assert.NotEmpty(fit.Lines);
    }

    [Fact]
    public void Titles_are_upper_cased_and_spaces_collapse()
    {
        var fit = TextFit.Fit(M, CardConfig.TitleText, "  it   associate ");
        Assert.Equal(new[] { "IT ASSOCIATE" }, fit.Lines);
        Assert.Empty(TextFit.Fit(M, CardConfig.TitleText, "   ").Lines);
    }

    [Fact]
    public void A_block_is_anchored_the_way_its_box_says()
    {
        const double ppm = 10;
        var name = TextFit.Place(M, CardConfig.NameText, new FittedText(new[] { "A", "B" }, 6, true), ppm);
        var px = 6 * CardConfig.PtToMm * ppm;
        // bottom-aligned: the last line's descender sits on the box bottom
        Assert.Equal(CardConfig.NameText.Box.Bottom * ppm - 0.2 * px, name.Baselines[1], 6);
        Assert.Equal(name.Baselines[1] - px * CardConfig.NameText.LineHeight, name.Baselines[0], 6);
        Assert.Equal((CardConfig.NameText.Box.X + CardConfig.NameText.Box.W / 2) * ppm, name.AnchorX, 6);

        var title = TextFit.Place(M, CardConfig.TitleText, new FittedText(new[] { "A" }, 4, true), ppm);
        Assert.Equal(CardConfig.TitleText.Box.Y * ppm + 0.7 * (4 * CardConfig.PtToMm * ppm), title.Baselines[0], 6);

        var number = TextFit.Place(M, CardConfig.EmployeeNoText, new FittedText(new[] { "A" }, 4, true), ppm);
        Assert.Equal(CardConfig.EmployeeNoText.Box.Bottom * ppm, number.Baselines[0], 6);
        Assert.Equal(CardConfig.EmployeeNoText.Box.Right * ppm, number.AnchorX, 6);
    }
}

public class PdfTests
{
    private static byte[] Build(int pages, out byte[] pixels)
    {
        pixels = new byte[6 * 4 * 3];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i * 7);
        var px = pixels;
        var list = Enumerable.Range(0, pages).Select(_ => (Func<PdfPage>)(() => new PdfPage(6, 4, px, 54, 85.6))).ToList();
        using var ms = new MemoryStream();
        PdfWriter.Write(ms, list, "APG-2026-122 Peña", new DateTime(2026, 11, 17, 9, 30, 0));
        return ms.ToArray();
    }

    [Fact]
    public void The_cross_reference_table_points_at_every_object()
    {
        var pdf = Build(2, out _);
        var text = Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF-1.4\n", text);
        Assert.EndsWith("%%EOF\n", text);

        var startxref = int.Parse(text[(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0]);
        Assert.StartsWith("xref\n0 10\n", text[startxref..]);
        var entries = text[startxref..].Split('\n').Skip(2).Take(10).ToArray();
        Assert.Equal("0000000000 65535 f ", entries[0]);
        for (var n = 1; n < 10; n++)
        {
            var offset = int.Parse(entries[n][..10]);
            Assert.StartsWith($"{n} 0 obj\n", text[offset..]);
        }
        Assert.Contains("/Count 2", text);
        Assert.Contains("/Kids [4 0 R 7 0 R]", text);
        // 54 x 85.6 mm in points
        Assert.Contains("/MediaBox [0 0 153.0709 242.6457]", text);
    }

    [Fact]
    public void Each_page_image_inflates_back_to_the_pixels_it_was_given()
    {
        var pdf = Build(1, out var pixels);
        var text = Encoding.Latin1.GetString(pdf);
        var head = text.IndexOf("/Subtype /Image", StringComparison.Ordinal);
        var lengthAt = text.IndexOf("/Length ", head, StringComparison.Ordinal) + 8;
        var length = int.Parse(text[lengthAt..].Split(' ')[0]);
        var start = text.IndexOf("stream\n", head, StringComparison.Ordinal) + 7;
        using var z = new ZLibStream(new MemoryStream(pdf, start, length), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        z.CopyTo(raw);
        Assert.Equal(pixels, raw.ToArray());
        Assert.Equal("\nendstream\n", text.Substring(start + length, 11));
    }

    [Fact]
    public void A_title_with_accents_is_written_as_utf16()
    {
        var text = Encoding.Latin1.GetString(Build(1, out _));
        // "ñ" is U+00F1
        Assert.Contains("/Title <FEFF", text);
        Assert.Contains("00f1", text);
        Assert.Contains("/CreationDate (D:20261117093000)", text);
    }

    [Fact]
    public void Pixels_that_do_not_match_the_page_size_are_refused()
    {
        using var ms = new MemoryStream();
        Assert.Throws<ArgumentException>(() => PdfWriter.Write(ms, new Func<PdfPage>[] { () => new PdfPage(2, 2, new byte[5], 54, 85.6) }, "x", DateTime.Now));
    }
}

public class StoreTests
{
    [Fact]
    public void A_file_store_keeps_values_between_instances_and_forgets_removed_ones()
    {
        var dir = Path.Combine(Path.GetTempPath(), "idmaker-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            new FileStore(dir).Set("titles", "[\"Driver\"]");
            Assert.Equal("[\"Driver\"]", new FileStore(dir).Get("titles"));
            new FileStore(dir).Set("titles", null);
            Assert.Null(new FileStore(dir).Get("titles"));
            Assert.Throws<ArgumentException>(() => new FileStore(dir).Get("..\\elsewhere"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_value_that_no_longer_parses_reads_as_the_fallback()
    {
        var store = new MemoryStore();
        store.Set("register", "{not json");
        Assert.Empty(store.Read("register", new List<RegisterEntry>()));
    }

    [Fact]
    public void Typed_titles_are_remembered_newest_first_and_company_titles_are_not_duplicated()
    {
        var store = new MemoryStore();
        var titles = new TitleList(store);
        titles.Remember("Driver");
        titles.Remember("  Messenger ");
        titles.Remember("it associate");            // already a company title
        titles.Remember("driver");                  // moves to the front, once
        Assert.Equal(CardConfig.Titles.Concat(new[] { "driver", "Messenger" }), new TitleList(store).All());
        titles.Forget("MESSENGER");
        Assert.Equal(CardConfig.Titles.Concat(new[] { "driver" }), new TitleList(store).All());
    }
}

public class TemplateStoreTests
{
    [Fact]
    public void Looking_at_the_map_without_moving_anything_changes_nothing()
    {
        var t = new TemplateStore(new MemoryStore());
        Assert.Same(CardGeometry.BuiltIn, t.Geometry());
        t.BeginEditing();
        Assert.True(t.IsPristine);
        var g = t.Geometry();
        Assert.Equal(CardConfig.Photo.HeadTop, g.Photo.HeadTop, 9);
        Assert.Equal(CardConfig.Photo.HeadCenterX, g.Photo.HeadCenterX, 9);
        Assert.Equal(CardConfig.Photo.HeadWidth, g.Photo.HeadWidth, 9);
        Assert.Equal(CardConfig.Photo.ReachY, g.Photo.ReachY, 9);
        Assert.Equal(CardConfig.NameText.Box, g.Name.Box);
        Assert.Equal(CardConfig.PhotoArea.Length, g.PhotoClip.Count);
        for (var i = 0; i < g.PhotoClip.Count; i++)
        {
            Assert.Equal(CardConfig.PhotoArea[i].X, g.PhotoClip[i].X, 9);
            Assert.Equal(CardConfig.PhotoArea[i].Y, g.PhotoClip[i].Y, 9);
        }
    }

    [Fact]
    public void A_moved_photo_window_takes_the_head_with_it()
    {
        var t = new TemplateStore(new MemoryStore());
        var start = t.Box(BoxKey.Photo);
        t.SetBox(BoxKey.Photo, start with { X = start.X - 5, Y = start.Y + 3 });
        Assert.False(t.IsPristine);
        var g = t.Geometry();
        Assert.Equal(CardConfig.Photo.HeadCenterX - 5, g.Photo.HeadCenterX, 6);
        Assert.Equal(CardConfig.Photo.HeadTop + 3, g.Photo.HeadTop, 6);
        Assert.Equal(CardConfig.Photo.HeadWidth, g.Photo.HeadWidth, 6);
        Assert.Equal("the built-in design, with the map moved", t.DesignName);
        Assert.Equal(2, g.FrontClip.Count);             // the band stays with the built-in artwork
    }

    [Fact]
    public void A_box_cannot_be_dragged_off_the_card_or_shrunk_past_grabbing()
    {
        var t = new TemplateStore(new MemoryStore());
        t.SetBox(BoxKey.Name, new RectMm(-10, 200, 0.5, 500));
        var b = t.Box(BoxKey.Name);
        Assert.Equal(0, b.X);
        Assert.Equal(2, b.W);
        Assert.Equal(CardConfig.CardHeightMm, b.H);
        Assert.Equal(0, b.Y);
    }

    [Fact]
    public void A_design_survives_a_restart_and_a_damaged_one_falls_back_to_the_built_in()
    {
        var store = new MemoryStore();
        var t = new TemplateStore(store);
        t.SetBox(BoxKey.Qr, new RectMm(40, 2, 9, 9));
        t.Save();

        var again = new TemplateStore(store);
        Assert.True(again.Restore());
        Assert.Equal(new RectMm(40, 2, 9, 9), again.Box(BoxKey.Qr));
        Assert.Equal(40, again.Geometry().Qr.X);

        store.Set("template", "{\"boxes\":{},\"photoRect\":{\"x\":0,\"y\":0,\"w\":0,\"h\":0}}");
        var broken = new TemplateStore(store);
        Assert.False(broken.Restore());
        Assert.False(broken.IsCustom);
        Assert.Null(store.Get("template"));
    }

    [Fact]
    public void Cancel_puts_the_map_back_as_it_was_when_the_editor_opened()
    {
        var t = new TemplateStore(new MemoryStore());
        var before = t.Snapshot();                      // nothing overridden
        t.SetBox(BoxKey.Title, new RectMm(1, 1, 10, 3));
        t.RestoreSnapshot(before);
        Assert.False(t.IsCustom);

        t.SetBox(BoxKey.Title, new RectMm(1, 1, 10, 3));
        var moved = t.Snapshot();
        t.SetBox(BoxKey.Title, new RectMm(5, 5, 10, 3));
        t.RestoreSnapshot(moved);
        Assert.Equal(new RectMm(1, 1, 10, 3), t.Box(BoxKey.Title));
    }

    [Fact]
    public void Uploaded_artwork_must_be_card_shaped_and_keeps_the_map_where_it_was()
    {
        Assert.NotEqual("", TemplateStore.ArtworkShapeProblem("front", 1000, 1000));
        Assert.Equal("", TemplateStore.ArtworkShapeProblem("front", 638, 1011));
        Assert.Equal("", TemplateStore.ArtworkShapeProblem("back", 1276, 2022));

        var t = new TemplateStore(new MemoryStore());
        t.SetBox(BoxKey.Name, new RectMm(5, 5, 20, 5));
        t.AdoptArtwork("New card", (1276, 2022), (638, 1011));
        Assert.True(t.HasArtwork);
        Assert.Equal(new RectMm(5, 5, 20, 5), t.Box(BoxKey.Name));
        Assert.Contains("front 1276 × 2022", t.Note);
        Assert.Empty(t.Geometry().FrontClip);           // nothing is known about new artwork's shapes
        Assert.Equal(4, t.Geometry().PhotoClip.Count);  // so the photo window is a plain rectangle
    }

    [Fact]
    public void Text_over_light_artwork_is_called_out_and_text_over_dark_artwork_is_not()
    {
        var t = new TemplateStore(new MemoryStore());
        t.BeginEditing();
        var light = new RgbaImage(64, 101);
        Array.Fill(light.Data, (byte)245);
        var warnings = t.Warnings(light);
        Assert.Equal(new[] { BoxKey.Name, BoxKey.Title, BoxKey.EmployeeNo, BoxKey.Signature }, warnings.Select(w => w.Key));
        Assert.All(warnings, w => Assert.True(w.Ratio < 3));

        var dark = new RgbaImage(64, 101);
        for (var i = 3; i < dark.Data.Length; i += 4) dark.Data[i] = 255;
        Assert.Empty(t.Warnings(dark));
    }
}
