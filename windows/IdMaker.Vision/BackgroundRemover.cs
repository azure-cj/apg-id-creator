// Cuts the person out of an ordinary photograph, on this computer.
//
// The model is MODNet (see Models/README.md). It wants the picture scaled
// so its short side is 512 pixels, both sides a multiple of 32, colours
// mapped to -1..1; it answers with one number per pixel, 0 for background
// to 1 for person, at that size. That is scaled back up to the photo.

using IdMaker.Core;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace IdMaker.Vision;

public sealed class OnnxBackgroundRemover : IBackgroundRemover, IDisposable
{
    private const int ShortSide = 512;
    private const int MaxLongSide = 1024;       // a very tall or wide picture is not worth four times the work
    private const int Multiple = 32;

    private readonly InferenceSession _session;
    private readonly string _input;

    private OnnxBackgroundRemover(InferenceSession session)
    {
        _session = session;
        _input = session.InputMetadata.Keys.First();
    }

    // The model's folder and file name, beside the program on Windows and inside the bundle on the Mac.
    public const string ModelFolder = "Models";
    public const string ModelFile = "modnet.onnx";

    /// <summary>Where the model sits beside the program.</summary>
    public static string DefaultModelPath => Path.Combine(AppContext.BaseDirectory, ModelFolder, ModelFile);

    /// <summary>
    /// Load the model. Returns null, with the reason, when it is missing or
    /// will not load — the app then carries on without background removal
    /// rather than failing to start.
    /// </summary>
    public static OnnxBackgroundRemover? TryCreate(out string problem, string? modelPath = null)
    {
        var path = modelPath ?? DefaultModelPath;
        if (!File.Exists(path)) { problem = $"The background-removal model is missing ({path})."; return null; }
        try
        {
            using var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
            problem = "";
            return new OnnxBackgroundRemover(new InferenceSession(path, options));
        }
        catch (Exception e) when (e is OnnxRuntimeException or DllNotFoundException or BadImageFormatException or TypeInitializationException)
        {
            problem = "The background remover could not start on this computer: " + e.Message;
            return null;
        }
    }

    /// <summary>The size the model is run at for a picture of this size.</summary>
    public static (int W, int H) ModelSize(int width, int height)
    {
        var k = (double)ShortSide / Math.Min(width, height);
        if (Math.Max(width, height) * k > MaxLongSide) k = (double)MaxLongSide / Math.Max(width, height);
        static int Snap(double v) => Math.Max(Multiple, (int)Math.Round(v / Multiple) * Multiple);
        return (Snap(width * k), Snap(height * k));
    }

    public byte[]? Matte(RgbaImage image)
    {
        var (w, h) = ModelSize(image.Width, image.Height);
        var input = new DenseTensor<float>(new[] { 1, 3, h, w });
        FillInput(image, input, w, h);

        float[] matte;
        try
        {
            using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_input, input) });
            matte = results[0].AsEnumerable<float>().ToArray();
        }
        catch (OnnxRuntimeException) { return null; }
        if (matte.Length != w * h) return null;
        return Upscale(matte, w, h, image.Width, image.Height);
    }

    // The picture at the model's size, as planes of red, green and blue in -1..1.
    private static void FillInput(RgbaImage image, DenseTensor<float> input, int w, int h)
    {
        var span = input.Buffer.Span;
        var plane = w * h;
        if (w <= image.Width && h <= image.Height)
        {
            var small = image.Shrink(w, h);          // area average: no shimmer from skipped pixels
            for (var i = 0; i < plane; i++)
            {
                span[i] = small.Data[i * 4] / 127.5f - 1;
                span[plane + i] = small.Data[i * 4 + 1] / 127.5f - 1;
                span[2 * plane + i] = small.Data[i * 4 + 2] / 127.5f - 1;
            }
            return;
        }
        // a small picture: blend the four nearest pixels
        for (var y = 0; y < h; y++)
        {
            var fy = Math.Clamp((y + 0.5) * image.Height / h - 0.5, 0, image.Height - 1);
            var y0 = (int)fy;
            var y1 = Math.Min(image.Height - 1, y0 + 1);
            var ty = fy - y0;
            for (var x = 0; x < w; x++)
            {
                var fx = Math.Clamp((x + 0.5) * image.Width / w - 0.5, 0, image.Width - 1);
                var x0 = (int)fx;
                var x1 = Math.Min(image.Width - 1, x0 + 1);
                var tx = fx - x0;
                for (var c = 0; c < 3; c++)
                {
                    double a = image.Data[(y0 * image.Width + x0) * 4 + c], b = image.Data[(y0 * image.Width + x1) * 4 + c];
                    double d = image.Data[(y1 * image.Width + x0) * 4 + c], e = image.Data[(y1 * image.Width + x1) * 4 + c];
                    var v = (a * (1 - tx) + b * tx) * (1 - ty) + (d * (1 - tx) + e * tx) * ty;
                    span[c * plane + y * w + x] = (float)(v / 127.5 - 1);
                }
            }
        }
    }

    private static byte[] Upscale(float[] matte, int w, int h, int outW, int outH)
    {
        var result = new byte[outW * outH];
        for (var y = 0; y < outH; y++)
        {
            var fy = Math.Clamp((y + 0.5) * h / outH - 0.5, 0, h - 1);
            var y0 = (int)fy;
            var y1 = Math.Min(h - 1, y0 + 1);
            var ty = (float)(fy - y0);
            for (var x = 0; x < outW; x++)
            {
                var fx = Math.Clamp((x + 0.5) * w / outW - 0.5, 0, w - 1);
                var x0 = (int)fx;
                var x1 = Math.Min(w - 1, x0 + 1);
                var tx = (float)(fx - x0);
                var v = (matte[y0 * w + x0] * (1 - tx) + matte[y0 * w + x1] * tx) * (1 - ty)
                      + (matte[y1 * w + x0] * (1 - tx) + matte[y1 * w + x1] * tx) * ty;
                result[y * outW + x] = (byte)Math.Clamp((int)(v * 255 + 0.5f), 0, 255);
            }
        }
        return result;
    }

    public void Dispose() => _session.Dispose();
}
