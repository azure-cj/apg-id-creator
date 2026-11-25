using IdMaker.Core;

namespace IdMaker.Tests;

public class PhotoTests
{
    private static readonly PhotoSpec Spec = CardConfig.Photo;

    [Fact]
    public void The_built_in_example_lands_where_the_web_app_put_it()
    {
        var web = Repo.WebReference;
        var result = PhotoPipeline.Analyse(Png.Read(Repo.Asset("example-cut-out.png")), "example-cut-out.png", Spec);

        Assert.Equal(PhotoBackground.Transparent, result.Background);
        Assert.True(result.Check.Ok, result.Check.Problem);
        Assert.Empty(result.Check.Warnings);

        // The head is measured on a copy shrunk by this app's own resampler,
        // not Chromium's, so the two agree to a fraction of a percent rather
        // than to the last digit. Half a millimetre on the card is the bar.
        var want = web.GetProperty("placement");
        var got = result.Check.Placement!;
        Assert.InRange(got.X, want.GetProperty("x").GetDouble() - 0.5, want.GetProperty("x").GetDouble() + 0.5);
        Assert.InRange(got.Y, want.GetProperty("y").GetDouble() - 0.5, want.GetProperty("y").GetDouble() + 0.5);
        Assert.InRange(got.W, want.GetProperty("w").GetDouble() - 0.5, want.GetProperty("w").GetDouble() + 0.5);
        Assert.InRange(got.H, want.GetProperty("h").GetDouble() - 0.8, want.GetProperty("h").GetDouble() + 0.8);
        Assert.Equal(0, got.ShortBy);

        var head = web.GetProperty("head");
        Assert.InRange(result.Check.Head!.Width, head.GetProperty("width").GetDouble() * 0.985, head.GetProperty("width").GetDouble() * 1.015);
        Assert.InRange(result.Check.Head.CenterX, head.GetProperty("centerX").GetDouble() - 4, head.GetProperty("centerX").GetDouble() + 4);
    }

    [Fact]
    public void The_head_is_scaled_to_the_width_the_card_asks_for()
    {
        var img = Make.Silhouette(600, 1000, headTop: 60, headW: 200, headH: 260, neckW: 90, neckH: 40, bodyW: 520);
        var check = PhotoPipeline.Analyse(img, "a.png", Spec).Check;
        Assert.True(check.Ok, check.Problem);
        var mmPerPx = check.Placement!.W / 600;
        Assert.InRange(check.Head!.Width * mmPerPx, Spec.HeadWidth * 0.97, Spec.HeadWidth * 1.03);
        Assert.InRange(check.Placement.Y + check.Head.Top * mmPerPx, Spec.HeadTop - 0.2, Spec.HeadTop + 0.2);
        Assert.InRange(check.Placement.X + check.Head.CenterX * mmPerPx, Spec.HeadCenterX - 0.3, Spec.HeadCenterX + 0.3);
    }

    [Fact]
    public void A_head_too_small_to_show_a_face_is_refused_and_a_merely_small_one_is_warned_about()
    {
        // two limits on the width of the head, in the photo's own pixels: under 40 the photo cannot be used,
        // under the card's 180 (PhotoSpec.MinHeadPx) it can, with a word about sharpness
        var tiny = PhotoPipeline.Analyse(Make.Silhouette(120, 200, headTop: 12, headW: 30, headH: 40, neckW: 14, neckH: 8, bodyW: 100), "tiny.png", Spec).Check;
        Assert.False(tiny.Ok);
        Assert.Equal("This photo is too small to show the face. Please use a larger photo.", tiny.Problem);

        var small = PhotoPipeline.Analyse(Make.Silhouette(300, 500, headTop: 30, headW: 100, headH: 130, neckW: 45, neckH: 20, bodyW: 260), "small.png", Spec).Check;
        Assert.True(small.Ok, small.Problem);
        Assert.InRange(small.Head!.Width, 40, Spec.MinHeadPx);
        Assert.Contains(small.Warnings, w => w.StartsWith("This is a small photo, so the face may print slightly soft.", StringComparison.Ordinal));

        var big = PhotoPipeline.Analyse(Make.Silhouette(600, 1000, headTop: 60, headW: 200, headH: 260, neckW: 90, neckH: 40, bodyW: 520), "big.png", Spec).Check;
        Assert.DoesNotContain(big.Warnings, w => w.Contains("small photo", StringComparison.Ordinal));
    }

    [Fact]
    public void A_tuft_of_hair_standing_up_does_not_end_the_head_early()
    {
        // A narrow spike above the hair, with a dip before the head proper:
        // read from the top down, the dip looked like a neck and the head
        // came out a quarter of its width.
        var plain = Make.Silhouette(600, 1000, 120, 200, 260, 90, 40, 520);
        var spiked = Make.Silhouette(600, 1000, 120, 200, 260, 90, 40, 520, img =>
        {
            for (var y = 60; y < 130; y++)
                for (var x = 290; x < 312; x++)
                {
                    var o = (y * 600 + x) * 4;
                    img.Data[o] = 60; img.Data[o + 1] = 50; img.Data[o + 2] = 40; img.Data[o + 3] = 255;
                }
        });
        var a = PhotoPipeline.Analyse(plain, "a.png", Spec).Check;
        var b = PhotoPipeline.Analyse(spiked, "b.png", Spec).Check;
        Assert.True(b.Ok, b.Problem);
        Assert.InRange(b.Head!.Width, a.Head!.Width * 0.95, a.Head.Width * 1.05);
    }

    [Fact]
    public void A_closely_cropped_photo_is_not_enlarged_without_limit()
    {
        // head and a sliver of shoulders: reaching the bottom of the window
        // would need the head far larger than the card asks for
        var img = Make.Silhouette(600, 520, 40, 260, 320, 110, 40, 560);
        var check = PhotoPipeline.Analyse(img, "close.png", Spec).Check;
        Assert.True(check.Ok, check.Problem);
        var headMm = check.Head!.Width * check.Placement!.W / 600;
        Assert.InRange(headMm, Spec.HeadWidth * 0.97, Spec.HeadWidth * Spec.MaxEnlarge * 1.03);
        Assert.True(check.Placement.ShortBy > 0, "a photo that stops short says by how much, so its edge can be faded");
        Assert.Contains(check.Warnings, w => w.Contains("stops short"));
    }

    [Fact]
    public void How_the_person_is_framed_is_a_warning_not_a_refusal()
    {
        // no shoulders at all: just a head, floating
        var headOnly = new RgbaImage(400, 500);
        for (var y = 80; y < 380; y++)
        {
            var t = (y - 80 + 0.5) / 300 * 2 - 1;
            var half = (int)(110 * Math.Sqrt(Math.Max(0, 1 - t * t)));
            for (var x = 200 - half; x <= 200 + half; x++)
            {
                var o = (y * 400 + x) * 4;
                headOnly.Data[o] = (byte)(100 + (x * 5 + y * 3) % 80); headOnly.Data[o + 1] = 90; headOnly.Data[o + 2] = 70; headOnly.Data[o + 3] = 255;
            }
        }
        var check = PhotoPipeline.Analyse(headOnly, "head.png", Spec).Check;
        Assert.True(check.Ok, check.Problem);
        Assert.Contains(check.Warnings, w => w.Contains("shoulders aren’t visible"));
        Assert.InRange(check.Head!.Width, 200, 230);

        // the head touching the top edge
        var cutOff = Make.Silhouette(600, 1000, 0, 200, 260, 90, 40, 520);
        var top = PhotoPipeline.Analyse(cutOff, "top.png", Spec).Check;
        Assert.True(top.Ok, top.Problem);
        Assert.Contains(top.Warnings, w => w.Contains("top of the head"));
    }

    [Fact]
    public void A_green_backdrop_is_keyed_out_and_the_person_kept()
    {
        var img = Make.Silhouette(600, 1000, 60, 200, 260, 90, 40, 520, backdrop: (0, 255, 0));
        var result = PhotoPipeline.Analyse(img, "green.png", Spec);
        Assert.Equal(PhotoBackground.Green, result.Background);
        Assert.True(result.Check.Ok, result.Check.Problem);
        Assert.Equal(0, result.Image.Data[3]);                                  // the corner is transparent now
        Assert.Equal(255, result.Image.Data[(500 * 600 + 300) * 4 + 3]);        // the chest is still there
    }

    [Fact]
    public void Clothing_the_colour_of_the_backdrop_is_caught()
    {
        // a green shirt: the body below the shoulders vanishes with the backdrop
        var img = Make.Silhouette(600, 1000, 60, 200, 260, 90, 40, 520, backdrop: (0, 255, 0), extra: i =>
        {
            for (var y = 440; y < 1000; y++)
                for (var x = 80; x < 520; x++)
                {
                    var o = (y * 600 + x) * 4;
                    i.Data[o] = 10; i.Data[o + 1] = 240; i.Data[o + 2] = 20; i.Data[o + 3] = 255;
                }
        });
        var check = PhotoPipeline.Analyse(img, "shirt.png", Spec).Check;
        Assert.False(check.Ok);
    }

    private sealed class FakeRemover : IBackgroundRemover
    {
        private readonly Func<RgbaImage, byte[]?> _matte;
        public int Calls { get; private set; }
        public FakeRemover(Func<RgbaImage, byte[]?> matte) { _matte = matte; }
        public byte[]? Matte(RgbaImage image) { Calls++; return _matte(image); }
    }

    [Fact]
    public void An_ordinary_photo_is_cut_out_on_this_computer_when_a_remover_is_there()
    {
        var person = Make.Silhouette(600, 1000, 60, 200, 260, 90, 40, 520);
        var photo = Make.Silhouette(600, 1000, 60, 200, 260, 90, 40, 520, backdrop: (180, 170, 160));
        var remover = new FakeRemover(_ =>
        {
            var m = new byte[600 * 1000];
            for (var i = 0; i < m.Length; i++) m[i] = person.Data[i * 4 + 3];
            return m;
        });

        var without = PhotoPipeline.Analyse(photo, "wall.jpg", Spec);
        Assert.False(without.Check.Ok);
        Assert.True(without.Check.FixIsPrompt);

        var with = PhotoPipeline.Analyse(photo, "wall.jpg", Spec, remover);
        Assert.Equal(1, remover.Calls);
        Assert.Equal(PhotoBackground.Removed, with.Background);
        Assert.True(with.Check.Ok, with.Check.Problem);
        Assert.Equal(0, with.Image.Data[3]);

        // a cut-out or a green screen never needs the remover
        PhotoPipeline.Analyse(person, "cut.png", Spec, remover);
        Assert.Equal(1, remover.Calls);
    }

    // A photo of the standard silhouette on `paint`'s background, and a remover that finds exactly the person.
    private static (RgbaImage Photo, FakeRemover Remover) PersonIn(Func<int, int, (byte R, byte G, byte B)> paint)
    {
        var person = Make.Silhouette(600, 1000, 60, 200, 260, 90, 40, 520);
        var photo = person.Clone();
        for (var y = 0; y < 1000; y++)
            for (var x = 0; x < 600; x++)
            {
                var o = (y * 600 + x) * 4;
                if (person.Data[o + 3] != 0) continue;
                var (r, g, b) = paint(x, y);
                photo.Data[o] = r; photo.Data[o + 1] = g; photo.Data[o + 2] = b; photo.Data[o + 3] = 255;
            }
        var remover = new FakeRemover(_ =>
        {
            var m = new byte[600 * 1000];
            for (var i = 0; i < m.Length; i++) m[i] = person.Data[i * 4 + 3];
            return m;
        });
        return (photo, remover);
    }

    [Fact]
    public void A_photo_taken_in_a_room_is_refused_and_points_to_the_fix()
    {
        // a wall, a door on the left, a window on the right, the floor along the bottom
        var (photo, remover) = PersonIn((x, y) =>
            y > 900 ? ((byte)124, (byte)106, (byte)88)
            : x < 110 && y > 150 ? ((byte)138, (byte)108, (byte)74)
            : x > 430 && x < 580 && y > 60 && y < 330 ? ((byte)159, (byte)192, (byte)223)
            : ((byte)208, (byte)196, (byte)178));
        var result = PhotoPipeline.Analyse(photo, "room.jpg", Spec, remover);
        Assert.False(result.Check.Ok);
        Assert.Equal(PhotoPipeline.MsgRoomBehind, result.Check.Problem);
        Assert.True(result.Check.FixIsPrompt);
    }

    [Theory]
    [InlineData(235, 233, 228)]     // off-white
    [InlineData(208, 196, 178)]     // beige
    [InlineData(150, 170, 190)]     // pale blue
    [InlineData(90, 90, 92)]        // dark grey
    public void A_plain_wall_shading_from_one_side_to_the_other_is_still_a_wall(int r, int g, int b)
    {
        // light falling across the wall: 40 levels darker at one side than the other
        var (photo, remover) = PersonIn((x, y) => ((byte)Math.Clamp(r - 40 * x / 600, 0, 255), (byte)Math.Clamp(g - 40 * x / 600, 0, 255), (byte)Math.Clamp(b - 30 * x / 600, 0, 255)));
        var result = PhotoPipeline.Analyse(photo, "wall.jpg", Spec, remover);
        Assert.True(result.Check.Ok, result.Check.Problem);
    }

    [Fact]
    public void A_wall_with_a_darker_panel_and_a_rail_is_still_a_wall()
    {
        // the office the background-remover tests use: two colours and a thin rail
        var (photo, remover) = PersonIn((x, y) =>
            Math.Abs(y - 500) < 5 ? ((byte)60, (byte)60, (byte)60)
            : x > 372 ? ((byte)(135 - 20 * x / 600), (byte)(143 - 10 * y / 1000), (byte)125)
            : ((byte)(205 - 20 * x / 600), (byte)(198 - 10 * y / 1000), (byte)185));
        var result = PhotoPipeline.Analyse(photo, "office.jpg", Spec, remover);
        Assert.True(result.Check.Ok, result.Check.Problem);
    }

    [Fact]
    public void A_remover_that_finds_nobody_says_so_plainly()
    {
        var photo = Make.Silhouette(600, 1000, 60, 200, 260, 90, 40, 520, backdrop: (180, 170, 160));
        var everything = new FakeRemover(i => Enumerable.Repeat((byte)255, i.Width * i.Height).ToArray());
        var check = PhotoPipeline.Analyse(photo, "wall.jpg", Spec, everything).Check;
        Assert.False(check.Ok);
        Assert.Contains("could not tell the person from the background", check.Problem);
    }

    [Fact]
    public void Files_that_are_not_pictures_the_app_can_open_are_turned_away_with_the_reason()
    {
        Assert.Contains("HEIC", PhotoPipeline.FileProblem("IMG_0012.HEIC", 1000));
        Assert.Contains("PNG, JPG or WebP", PhotoPipeline.FileProblem("notes.pdf", 1000));
        Assert.Contains("over 15 MB", PhotoPipeline.FileProblem("big.jpg", 16L * 1024 * 1024));
        Assert.Equal("", PhotoPipeline.FileProblem("Ana.JPEG", 2_000_000));
    }

    [Fact]
    public void Shrinking_keeps_a_transparent_pixel_s_colour_out_of_the_edge()
    {
        // left half opaque red, right half transparent green
        var img = new RgbaImage(4, 2);
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var o = (y * 4 + x) * 4;
                if (x < 2) { img.Data[o] = 255; img.Data[o + 3] = 255; } else { img.Data[o + 1] = 255; }
            }
        }
        var one = img.Shrink(1, 1);
        Assert.Equal(255, one.Data[0]);
        Assert.Equal(0, one.Data[1]);
        Assert.InRange(one.Data[3], 127, 128);
    }

    [Fact]
    public void A_manual_zoom_holds_the_face_still()
    {
        var p = new PhotoPlacement(15.8, 13.8, 37.9, 62.8, 0);
        var zoomed = p.Adjusted(new PhotoAdjust(0, 0, 1.2), Spec);
        // the point under the head anchor does not move
        Assert.Equal(Spec.HeadCenterX, zoomed.X + (Spec.HeadCenterX - p.X) * 1.2, 9);
        Assert.Equal(Spec.HeadTop, zoomed.Y + (Spec.HeadTop - p.Y) * 1.2, 9);
        Assert.Same(p, p.Adjusted(PhotoAdjust.None, Spec));
        Assert.Equal(p.X + 2, p.Adjusted(new PhotoAdjust(2, 0, 1), Spec).X, 9);
    }
}

public class SignatureTests
{
    private static DrawnSignature Line(params InkPoint[] pts) => new(new IReadOnlyList<InkPoint>[] { pts });

    [Fact]
    public void A_single_tap_is_a_dot_and_a_stroke_is_one_curve_per_gap()
    {
        var dot = SignatureInk.Segments(Line(new InkPoint(5, 5)), 1, 3);
        Assert.Single(dot);
        Assert.True(dot[0].Dot);

        var pts = Enumerable.Range(0, 10).Select(i => new InkPoint(i * 10, 20)).ToArray();
        var segs = SignatureInk.Segments(Line(pts), 1, 3);
        Assert.Equal(9, segs.Count);
        Assert.All(segs, s => Assert.InRange(s.Width, 1, 3));
        Assert.Equal(0, segs[0].X1, 6);
        Assert.Equal(90, segs[^1].X2, 6);
    }

    [Fact]
    public void A_harder_press_draws_a_thicker_line()
    {
        InkPoint[] Stroke(double pressure) => Enumerable.Range(0, 30).Select(i => new InkPoint(i * 6, 20, pressure)).ToArray();
        var light = SignatureInk.Segments(Line(Stroke(0.1)), 1, 3);
        var heavy = SignatureInk.Segments(Line(Stroke(0.95)), 1, 3);
        Assert.True(heavy[^1].Width > light[^1].Width + 0.8, $"heavy {heavy[^1].Width} against light {light[^1].Width}");
        Assert.InRange(light[^1].Width, 1, 3);
        Assert.InRange(heavy[^1].Width, 1, 3);
    }

    [Fact]
    public void Without_pressure_a_fast_stroke_is_thinner_than_a_slow_one()
    {
        var slowThenFast = Enumerable.Range(0, 20).Select(i => new InkPoint(i * 3, 0))
            .Concat(Enumerable.Range(1, 20).Select(i => new InkPoint(57 + i * 30, 0))).ToArray();
        var segs = SignatureInk.Segments(Line(slowThenFast), 1, 3);
        Assert.True(segs[10].Width > segs[^1].Width);
    }

    [Fact]
    public void Jitter_is_dropped_and_the_ends_of_a_stroke_stay_put()
    {
        var raw = new[] { new InkPoint(0, 0), new InkPoint(0.1, 0.1), new InkPoint(10, 4), new InkPoint(20, -4), new InkPoint(30, 0) };
        var smooth = SignatureInk.Smooth(raw);
        Assert.Equal(4, smooth.Count);
        Assert.Equal(new InkPoint(0, 0), smooth[0]);
        Assert.Equal(new InkPoint(30, 0), smooth[^1]);
        Assert.True(Math.Abs(smooth[1].Y) < 4);
    }

    [Fact]
    public void Ink_is_fitted_into_the_box_without_stretching_and_centred()
    {
        var spec = CardConfig.Signature;
        var fit = SignatureInk.FitInto(spec, 10, 20, 300, 40);          // much wider than the box is
        var pad = spec.StrokeMm / 2;
        Assert.Equal((spec.Box.W - 2 * pad) / 300, fit.Scale, 9);
        Assert.Equal(spec.Box.X + pad, fit.Left, 9);
        var inkH = 40 * fit.Scale;
        Assert.Equal(spec.Box.Y + pad + (spec.Box.H - 2 * pad - inkH) / 2, fit.Top, 9);
    }

    [Fact]
    public void The_example_signature_has_ink_and_an_empty_one_does_not()
    {
        Assert.True(Session.ExampleSignature().HasInk);
        Assert.False(new DrawnSignature(Array.Empty<IReadOnlyList<InkPoint>>()).HasInk);
        var (minX, minY, w, h) = SignatureInk.Bounds(Session.ExampleSignature());
        Assert.Equal(20, minX, 6);
        Assert.True(w > 300 && h > 50 && minY < 20);
    }

    private static RgbaImage Paper(int w, int h, byte level, Action<RgbaImage>? ink = null)
    {
        var img = new RgbaImage(w, h);
        for (var i = 0; i < w * h; i++) { img.Data[i * 4] = img.Data[i * 4 + 1] = img.Data[i * 4 + 2] = level; img.Data[i * 4 + 3] = 255; }
        ink?.Invoke(img);
        return img;
    }

    [Fact]
    public void A_scan_is_trimmed_to_its_ink()
    {
        var scan = Paper(400, 200, 235, img =>
        {
            for (var y = 80; y < 100; y++)
                for (var x = 100; x < 300; x++)
                {
                    var o = (y * 400 + x) * 4;
                    img.Data[o] = img.Data[o + 1] = img.Data[o + 2] = 25;
                }
        });
        var (sig, error) = SignatureInk.FromImage(scan, "sign.jpg");
        Assert.True(sig is not null, error);
        Assert.Equal(200, sig!.Width);
        Assert.Equal(20, sig.Height);
        Assert.Equal(255, sig.Alpha[10 * 200 + 100]);
        Assert.Equal("sign.jpg", sig.FileName);
    }

    [Fact]
    public void A_blank_page_and_a_dark_photo_are_both_refused_with_a_way_forward()
    {
        Assert.Contains("No signature found", SignatureInk.FromImage(Paper(300, 200, 240), "blank.png").Error);
        var dark = Paper(300, 200, 240, img =>
        {
            for (var i = 0; i < 300 * 140; i++) img.Data[i * 4] = img.Data[i * 4 + 1] = img.Data[i * 4 + 2] = 20;
        });
        Assert.Contains("too dark", SignatureInk.FromImage(dark, "dark.png").Error);
    }
}
