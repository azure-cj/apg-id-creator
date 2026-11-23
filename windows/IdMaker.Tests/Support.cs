// What the tests share: the repository's own files, a PNG reader (the core
// library deliberately has no image codec; the app uses Windows'), a text
// measurer made of arithmetic, and a stand-in for the Firebase project the
// shared register lives in. No test ever calls the real one.

using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using IdMaker.Core;

namespace IdMaker.Tests;

internal static class Repo
{
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "test", "golden-front.png"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("The repository root (the folder holding assets/) was not found above the test binaries.");
        }
    }

    public static string Asset(string name) => Path.Combine(Root, "assets", name);

    /// <summary>Values captured from the web app this one replaces (see Fixtures/README.md).</summary>
    public static JsonElement WebReference { get; } =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "web-reference.json"))).RootElement;
}

/// <summary>Reads 8-bit RGB and RGBA PNGs that are not interlaced, which is all the repository holds.</summary>
internal static class Png
{
    public static RgbaImage Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        int width = 0, height = 0, colorType = 0;
        using var idat = new MemoryStream();
        for (var p = 8; p < bytes.Length;)
        {
            var len = (bytes[p] << 24) | (bytes[p + 1] << 16) | (bytes[p + 2] << 8) | bytes[p + 3];
            var type = Encoding.ASCII.GetString(bytes, p + 4, 4);
            if (type == "IHDR")
            {
                width = (bytes[p + 8] << 24) | (bytes[p + 9] << 16) | (bytes[p + 10] << 8) | bytes[p + 11];
                height = (bytes[p + 12] << 24) | (bytes[p + 13] << 16) | (bytes[p + 14] << 8) | bytes[p + 15];
                colorType = bytes[p + 17];
                if (bytes[p + 16] != 8 || colorType is not (2 or 6) || bytes[p + 20] != 0) throw new NotSupportedException("Only 8-bit RGB/RGBA, non-interlaced PNGs.");
            }
            else if (type == "IDAT") idat.Write(bytes, p + 8, len);
            p += 12 + len;
        }
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        z.CopyTo(raw);
        var data = raw.ToArray();

        var bpp = colorType == 6 ? 4 : 3;
        var stride = width * bpp;
        var img = new RgbaImage(width, height);
        var prev = new byte[stride];
        var cur = new byte[stride];
        for (var y = 0; y < height; y++)
        {
            var o = y * (stride + 1);
            var filter = data[o];
            for (var x = 0; x < stride; x++)
            {
                int a = x >= bpp ? cur[x - bpp] : 0, b = prev[x], c = x >= bpp ? prev[x - bpp] : 0;
                int v = data[o + 1 + x];
                v += filter switch
                {
                    0 => 0, 1 => a, 2 => b, 3 => (a + b) / 2,
                    _ => Paeth(a, b, c),
                };
                cur[x] = (byte)v;
            }
            for (var x = 0; x < width; x++)
            {
                var t = (y * width + x) * 4;
                img.Data[t] = cur[x * bpp]; img.Data[t + 1] = cur[x * bpp + 1]; img.Data[t + 2] = cur[x * bpp + 2];
                img.Data[t + 3] = bpp == 4 ? cur[x * bpp + 3] : (byte)255;
            }
            (prev, cur) = (cur, prev);
        }
        return img;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}

/// <summary>Every character is 0.55 em wide, capitals are 0.7 em tall, descenders reach 0.2 em.</summary>
internal sealed class FakeMeasurer : ITextMeasurer
{
    public double CharEm { get; init; } = 0.55;
    public double Width(CardFont font, double px, string text) => text.Length * CharEm * px;
    public double CapHeight(CardFont font, double px) => 0.7 * px;
    public double Descent(CardFont font, double px, bool uppercase) => (uppercase ? 0.15 : 0.2) * px;
}

internal static class Make
{
    /// <summary>The pretend project every test talks to (FakeFirebase.Project).</summary>
    public static readonly RegisterProject Project = FakeFirebase.Project;

    /// <summary>A register with no shared one behind it.</summary>
    public static Register LocalRegister(IKeyValueStore? store = null) => new(store ?? new MemoryStore()) { Project = null };

    /// <summary>A register talking to the pretend project, not yet signed in.</summary>
    public static Register SharedRegister(FakeFirebase firebase, IKeyValueStore? store = null, ISecretKeeper? keeper = null, Func<DateTimeOffset>? clock = null) =>
        clock is null
            ? new(store ?? new MemoryStore(), firebase.Http, keeper) { Project = Project }
            : new(store ?? new MemoryStore(), firebase.Http, keeper) { Project = Project, Clock = clock };

    /// <summary>The same, signed in as HR.</summary>
    public static async Task<Register> SignedIn(FakeFirebase firebase, IKeyValueStore? store = null, ISecretKeeper? keeper = null, Func<DateTimeOffset>? clock = null)
    {
        var r = SharedRegister(firebase, store, keeper, clock);
        var result = await r.SignIn(FakeFirebase.Email, FakeFirebase.Password);
        Assert.True(result.Ok, result.Error);
        return r;
    }

    public static Session Session(Register? register = null, IKeyValueStore? store = null, bool measurer = true)
    {
        store ??= new MemoryStore();
        var s = new Session(register ?? LocalRegister(store), new TitleList(store), store, () => new DateTime(2026, 11, 17, 9, 30, 0));
        if (measurer) s.Measurer = new FakeMeasurer();
        return s;
    }

    /// <summary>A card with everything on it except a real photograph.</summary>
    public static void FillForm(Session s, string seq = "124")
    {
        s.SetNamePart(Field.Last, "Reyes");
        s.SetNamePart(Field.First, "Ana");
        s.PickKind(PersonKind.Employee);
        s.PickRole1("HR Associate");
        s.SetSeq(seq);
        s.AcceptPhoto(Photo());
        s.SetSignature(IdMaker.Core.Session.ExampleSignature());
    }

    public static PhotoResult Photo() => new(new RgbaImage(4, 4), "photo.png", PhotoBackground.Transparent,
        new PhotoCheck { Ok = true, Placement = new PhotoPlacement(10, 10, 30, 50, 0), Head = new HeadInfo(1, 2, 2, 3) });

    /// <summary>A person-shaped cut-out: an ellipse for the head, a neck, and a wide body to the bottom edge.</summary>
    public static RgbaImage Silhouette(int w, int h, int headTop, int headW, int headH, int neckW, int neckH, int bodyW,
        Action<RgbaImage>? extra = null, (byte R, byte G, byte B)? backdrop = null)
    {
        var img = new RgbaImage(w, h);
        if (backdrop is { } bg)
        {
            for (var i = 0; i < w * h; i++) { img.Data[i * 4] = bg.R; img.Data[i * 4 + 1] = bg.G; img.Data[i * 4 + 2] = bg.B; img.Data[i * 4 + 3] = 255; }
        }
        void Fill(int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            var o = (y * w + x) * 4;
            // a little texture, so the sharpness measure has something to read
            var v = (byte)(120 + ((x * 7 + y * 13) % 60));
            img.Data[o] = v; img.Data[o + 1] = (byte)(v - 30); img.Data[o + 2] = (byte)(v - 50); img.Data[o + 3] = 255;
        }
        var cx = w / 2;
        for (var y = 0; y < headH; y++)
        {
            var t = (y + 0.5) / headH * 2 - 1;
            var half = (int)(headW / 2.0 * Math.Sqrt(Math.Max(0, 1 - t * t)));
            for (var x = cx - half; x <= cx + half; x++) Fill(x, headTop + y);
        }
        for (var y = headTop + headH - 2; y < headTop + headH + neckH; y++)
            for (var x = cx - neckW / 2; x <= cx + neckW / 2; x++) Fill(x, y);
        for (var y = headTop + headH + neckH; y < h; y++)
        {
            // shoulders slope out to full width over a few rows
            var grow = Math.Min(1.0, (y - (headTop + headH + neckH) + 1) / (h * 0.06));
            var half = (int)(neckW / 2 + (bodyW / 2 - neckW / 2) * grow);
            for (var x = cx - half; x <= cx + half; x++) Fill(x, y);
        }
        extra?.Invoke(img);
        return img;
    }
}
