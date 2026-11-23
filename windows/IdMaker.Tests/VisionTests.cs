using IdMaker.Core;
using IdMaker.Vision;

namespace IdMaker.Tests;

/// <summary>These run the real model on the repository's sample portrait.</summary>
public class BackgroundRemoverTests
{
    private static readonly Lazy<OnnxBackgroundRemover> Remover = new(() =>
        OnnxBackgroundRemover.TryCreate(out var problem) ?? throw new InvalidOperationException(problem));

    // The sample cut-out pasted onto a wall: a soft gradient with a darker panel and a rail, like an office.
    private static (RgbaImage Photo, RgbaImage Truth) PersonAgainstAWall()
    {
        var cut = Png.Read(Repo.Asset("example-cut-out.png"));
        var photo = new RgbaImage(cut.Width, cut.Height);
        for (var y = 0; y < cut.Height; y++)
        {
            for (var x = 0; x < cut.Width; x++)
            {
                var o = (y * cut.Width + x) * 4;
                double r = 205 - 20.0 * x / cut.Width, g = 198 - 10.0 * y / cut.Height, b = 185;
                if (x > cut.Width * 0.62) { r -= 70; g -= 55; b -= 60; }
                if (Math.Abs(y - cut.Height / 2) < 5) { r = 60; g = 60; b = 60; }
                var a = cut.Data[o + 3] / 255.0;
                photo.Data[o] = (byte)(cut.Data[o] * a + r * (1 - a));
                photo.Data[o + 1] = (byte)(cut.Data[o + 1] * a + g * (1 - a));
                photo.Data[o + 2] = (byte)(cut.Data[o + 2] * a + b * (1 - a));
                photo.Data[o + 3] = 255;
            }
        }
        return (photo, cut);
    }

    [Fact]
    public void The_person_is_found_in_an_ordinary_photo()
    {
        var (photo, truth) = PersonAgainstAWall();
        var matte = Remover.Value.Matte(photo);
        Assert.NotNull(matte);
        Assert.Equal(photo.Width * photo.Height, matte!.Length);

        long both = 0, either = 0;
        for (var i = 0; i < matte.Length; i++)
        {
            bool m = matte[i] > 127, t = truth.Data[i * 4 + 3] > 127;
            if (m && t) both++;
            if (m || t) either++;
        }
        var overlap = (double)both / either;
        Assert.True(overlap > 0.97, $"the cut-out overlaps the true outline by {overlap:P1}; 97 % is the bar");
    }

    [Fact]
    public void A_photo_with_its_background_goes_through_the_whole_pipeline_and_lands_like_the_cut_out()
    {
        var (photo, truth) = PersonAgainstAWall();
        var fromPhoto = PhotoPipeline.Analyse(photo, "wall.jpg", CardConfig.Photo, Remover.Value);
        var fromCutOut = PhotoPipeline.Analyse(truth, "cut.png", CardConfig.Photo);

        Assert.Equal(PhotoBackground.Removed, fromPhoto.Background);
        Assert.True(fromPhoto.Check.Ok, fromPhoto.Check.Problem);
        var a = fromPhoto.Check.Placement!;
        var b = fromCutOut.Check.Placement!;
        // within a millimetre and a half of where the hand-made cut-out is placed
        Assert.InRange(a.X, b.X - 1.5, b.X + 1.5);
        Assert.InRange(a.Y, b.Y - 1.5, b.Y + 1.5);
        Assert.InRange(a.W, b.W * 0.96, b.W * 1.04);
    }

    [Theory]
    [InlineData(720, 1195, 512, 864)]
    [InlineData(3000, 4000, 512, 672)]
    [InlineData(4000, 3000, 672, 512)]
    [InlineData(400, 4000, 96, 1024)]
    [InlineData(100, 100, 512, 512)]
    public void The_model_is_run_at_a_size_it_accepts(int w, int h, int wantW, int wantH)
    {
        Assert.Equal((wantW, wantH), OnnxBackgroundRemover.ModelSize(w, h));
    }

    [Fact]
    public void A_missing_model_is_reported_rather_than_thrown()
    {
        Assert.Null(OnnxBackgroundRemover.TryCreate(out var problem, Path.Combine(Path.GetTempPath(), "no-such-model.onnx")));
        Assert.Contains("missing", problem);
    }
}
