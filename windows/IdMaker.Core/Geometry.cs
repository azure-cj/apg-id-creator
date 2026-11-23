// The card design in use.
//
// Two things can be changed without rebuilding the app:
//
//   the artwork   two pictures the same shape as a card, front and back
//   the map       where the photo, name, job title, number, signature and
//                 QR sit on it
//
// The map is not a file. It is the boxes drawn over the preview, and it is
// always there to look at — on the built-in card as much as on uploaded
// artwork. The built-in design is never overwritten.

using System.Text.Json;

namespace IdMaker.Core;

public enum BoxKey { Photo, Name, Title, EmployeeNo, Signature, Qr }

/// <summary>Everything the renderer needs to know about where things go.</summary>
public sealed record CardGeometry(
    TextSpec Name, TextSpec Title, TextSpec EmployeeNo,
    PhotoSpec Photo, QrSpec Qr, SignatureSpec Signature,
    // the outline the photo is cut to
    IReadOnlyList<PointMm> PhotoClip,
    // artwork drawn back over the photo (the band and the gold line); empty when there is none
    IReadOnlyList<IReadOnlyList<PointMm>> FrontClip)
{
    public static readonly CardGeometry BuiltIn = new(
        CardConfig.NameText, CardConfig.TitleText, CardConfig.EmployeeNoText,
        CardConfig.Photo, CardConfig.Qr, CardConfig.Signature,
        CardConfig.PhotoArea, CardConfig.FrontOutline);
}

/// <summary>A moved map and, optionally, replaced artwork: what is kept between runs.</summary>
public sealed record CustomTemplate
{
    public string Name { get; init; } = "Built-in design";
    /// <summary>True when the artwork itself was replaced; the pictures are kept beside this record.</summary>
    public bool Artwork { get; init; }
    public Dictionary<string, RectMm> Boxes { get; init; } = new();
    public RectMm PhotoRect { get; init; }
}

public sealed record ContrastWarning(BoxKey Key, string Label, string Ink, double Ratio, double Need);

public sealed class TemplateStore
{
    private const string Key = "template";
    public const int ArtworkWidth = 638;              // a design is stored at the printing size:
    public const int ArtworkHeight = 1011;            // 54 x 85.6 mm at 300 DPI
    private const double ShapeSlack = 0.02;           // how far off the card's proportions artwork may be
    private const double MinBoxMm = 2;                // a box smaller than this cannot be grabbed

    /// <summary>The file a replaced design's picture is kept in, in the app's data folder. The side is "front" or "back".</summary>
    public static string ArtworkFileName(string side) => $"template-{side}.png";

    private readonly IKeyValueStore _store;

    public TemplateStore(IKeyValueStore store) { _store = store; }

    /// <summary>null while nothing differs from the card the app was built with.</summary>
    public CustomTemplate? Custom { get; private set; }

    /// <summary>Anything the operator should know about the last change.</summary>
    public string Note { get; private set; } = "";

    public bool IsCustom => Custom is not null;
    public bool HasArtwork => Custom?.Artwork == true;

    public string DesignName =>
        Custom is null ? "the built-in design"
        : Custom.Artwork ? Custom.Name
        : "the built-in design, with the map moved";

    private static RectMm Bounds(IReadOnlyList<PointMm> poly)
    {
        double x = poly.Min(p => p.X), y = poly.Min(p => p.Y);
        return new RectMm(x, y, poly.Max(p => p.X) - x, poly.Max(p => p.Y) - y);
    }

    private static readonly RectMm PhotoBase = Bounds(CardConfig.PhotoArea);

    private static PointMm[] RectPolygon(RectMm r) =>
        new PointMm[] { new(r.X, r.Y), new(r.Right, r.Y), new(r.Right, r.Bottom), new(r.X, r.Bottom) };

    /// <summary>
    /// Move and scale a polygon so its bounding box becomes `to`. This is
    /// what lets the built-in card's photo window stay the shape it is —
    /// stepped, to clear the gold line — while being dragged as a simple box.
    /// </summary>
    private static PointMm[] FitPolygon(IReadOnlyList<PointMm> poly, RectMm from, RectMm to)
    {
        double kx = to.W / from.W, ky = to.H / from.H;
        return poly.Select(p => new PointMm(to.X + (p.X - from.X) * kx, to.Y + (p.Y - from.Y) * ky)).ToArray();
    }

    /* ---- the map ------------------------------------------------- */

    private static string K(BoxKey key) => key.ToString();

    /// <summary>Where the built-in design puts everything: the starting point.</summary>
    public static (Dictionary<string, RectMm> Boxes, RectMm PhotoRect) Defaults() => (new Dictionary<string, RectMm>
    {
        [K(BoxKey.Name)] = CardConfig.NameText.Box,
        [K(BoxKey.Title)] = CardConfig.TitleText.Box,
        [K(BoxKey.EmployeeNo)] = CardConfig.EmployeeNoText.Box,
        [K(BoxKey.Signature)] = CardConfig.Signature.Box,
        [K(BoxKey.Qr)] = new RectMm(CardConfig.Qr.X, CardConfig.Qr.Y, CardConfig.Qr.Size, CardConfig.Qr.Size),
    }, PhotoBase);

    public RectMm Box(BoxKey key)
    {
        if (Custom is null)
        {
            var d = Defaults();
            return key == BoxKey.Photo ? d.PhotoRect : d.Boxes[K(key)];
        }
        return key == BoxKey.Photo ? Custom.PhotoRect : Custom.Boxes[K(key)];
    }

    /// <summary>
    /// The photo window's real outline — stepped on the built-in card, a
    /// plain rectangle on uploaded artwork. The editor draws its handle in
    /// this shape, so what is dragged looks like what is cut.
    /// </summary>
    public IReadOnlyList<PointMm> PhotoOutline()
    {
        var rect = Box(BoxKey.Photo);
        return HasArtwork ? RectPolygon(rect) : FitPolygon(CardConfig.PhotoArea, PhotoBase, rect);
    }

    /// <summary>
    /// The geometry the card is drawn with. With nothing changed this is the
    /// built-in geometry itself, so the built-in card renders exactly as it
    /// did before any of this existed.
    /// </summary>
    public CardGeometry Geometry()
    {
        if (Custom is null) return CardGeometry.BuiltIn;
        var c = Custom;
        var p = c.PhotoRect;
        var b = CardConfig.Photo;
        // Where the head sits inside the photo window, as fractions of it,
        // taken from the built-in design: a window dragged somewhere else
        // frames the face the same way this one does.
        var photo = b with
        {
            HeadTop = p.Y + (b.HeadTop - PhotoBase.Y) / PhotoBase.H * p.H,
            HeadCenterX = p.X + (b.HeadCenterX - PhotoBase.X) / PhotoBase.W * p.W,
            HeadWidth = b.HeadWidth / PhotoBase.W * p.W,
            ReachY = p.Y + (b.ReachY - PhotoBase.Y) / PhotoBase.H * p.H,
        };
        var qr = c.Boxes[K(BoxKey.Qr)];
        return new CardGeometry(
            CardConfig.NameText with { Box = c.Boxes[K(BoxKey.Name)] },
            CardConfig.TitleText with { Box = c.Boxes[K(BoxKey.Title)] },
            CardConfig.EmployeeNoText with { Box = c.Boxes[K(BoxKey.EmployeeNo)] },
            photo,
            CardConfig.Qr with { X = qr.X, Y = qr.Y, Size = Math.Min(qr.W, qr.H) },
            CardConfig.Signature with { Box = c.Boxes[K(BoxKey.Signature)] },
            PhotoOutline(),
            // uploaded artwork: nothing is known about its shapes, so the
            // photo is a plain window sitting on top of it
            c.Artwork ? Array.Empty<IReadOnlyList<PointMm>>() : CardConfig.FrontOutline);
    }

    /// <summary>
    /// Nothing has actually been changed: the built-in artwork with the
    /// built-in map. Opening the map to look at it should leave the card
    /// exactly as the app shipped it.
    /// </summary>
    public bool IsPristine
    {
        get
        {
            if (Custom is null || Custom.Artwork) return false;
            var d = Defaults();
            return Custom.PhotoRect == d.PhotoRect && d.Boxes.All(kv => Custom.Boxes.TryGetValue(kv.Key, out var b) && b == kv.Value);
        }
    }

    /// <summary>Start moving the map about on whatever artwork is in use.</summary>
    public void BeginEditing()
    {
        if (Custom is not null) return;
        var d = Defaults();
        Custom = new CustomTemplate { Boxes = d.Boxes, PhotoRect = d.PhotoRect };
    }

    private static double R3(double v) => Math.Round(v, 3, MidpointRounding.AwayFromZero);

    /// <summary>Move or resize one box, kept on the card and big enough to grab.</summary>
    public void SetBox(BoxKey key, RectMm box)
    {
        BeginEditing();
        const double cardW = CardConfig.CardWidthMm, cardH = CardConfig.CardHeightMm;
        var w = Math.Clamp(box.W, MinBoxMm, cardW);
        var h = Math.Clamp(box.H, MinBoxMm, cardH);
        var fixedBox = new RectMm(R3(Math.Clamp(box.X, 0, cardW - w)), R3(Math.Clamp(box.Y, 0, cardH - h)), R3(w), R3(h));
        if (key == BoxKey.Photo) Custom = Custom! with { PhotoRect = fixedBox };
        else Custom = Custom! with { Boxes = new Dictionary<string, RectMm>(Custom.Boxes) { [K(key)] = fixedBox } };
    }

    /// <summary>Back to where the built-in design puts the boxes.</summary>
    public void ResetBoxes()
    {
        if (Custom is null) return;
        var d = Defaults();
        Custom = Custom with { Boxes = d.Boxes, PhotoRect = d.PhotoRect };
    }

    /// <summary>The map as it stands, for Cancel to put back. null when nothing was overridden.</summary>
    public (Dictionary<string, RectMm> Boxes, RectMm PhotoRect)? Snapshot() =>
        Custom is null ? null : (new Dictionary<string, RectMm>(Custom.Boxes), Custom.PhotoRect);

    public void RestoreSnapshot((Dictionary<string, RectMm> Boxes, RectMm PhotoRect)? snapshot)
    {
        if (snapshot is null) { if (!HasArtwork) Clear(); else ResetBoxes(); return; }
        BeginEditing();
        Custom = Custom! with { Boxes = new Dictionary<string, RectMm>(snapshot.Value.Boxes), PhotoRect = snapshot.Value.PhotoRect };
    }

    /* ---- new artwork --------------------------------------------- */

    /// <summary>Why a picture of this size cannot be a card side, or "" when it can.</summary>
    public static string ArtworkShapeProblem(string side, int w, int h)
    {
        const double want = CardConfig.CardWidthMm / CardConfig.CardHeightMm;
        if (h <= 0 || Math.Abs((double)w / h - want) > ShapeSlack)
        {
            return $"The {side} is {w} × {h} pixels, which is not the shape of an ID card. "
                 + $"Export it at {ArtworkWidth} × {ArtworkHeight} pixels (or anything with the same proportions).";
        }
        return "";
    }

    /// <summary>Take new artwork, keeping wherever the map has been dragged to.</summary>
    public void AdoptArtwork(string name, (int W, int H) front, (int W, int H) back)
    {
        var resized = new List<string>();
        if (front != (ArtworkWidth, ArtworkHeight)) resized.Add($"front {front.W} × {front.H}");
        if (back != (ArtworkWidth, ArtworkHeight)) resized.Add($"back {back.W} × {back.H}");
        var d = Defaults();
        Custom = new CustomTemplate
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Uploaded design" : name,
            Artwork = true,
            Boxes = Custom?.Boxes ?? d.Boxes,
            PhotoRect = Custom?.PhotoRect ?? d.PhotoRect,
        };
        Note = resized.Count > 0 ? $"Resized to {ArtworkWidth} × {ArtworkHeight} ({string.Join(", ", resized)})." : "";
    }

    /* ---- is this design actually printable? ----------------------- */

    private static double Luminance(double r, double g, double b)
    {
        static double F(double v) { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        return 0.2126 * F(r) + 0.7152 * F(g) + 0.0722 * F(b);
    }

    private static (int R, int G, int B) Hex(string s) =>
        (Convert.ToInt32(s.Substring(1, 2), 16), Convert.ToInt32(s.Substring(3, 2), 16), Convert.ToInt32(s.Substring(5, 2), 16));

    /// <summary>
    /// The mistake a designer can make that nothing else would catch:
    /// putting a text box over light artwork. The name prints in pale gold
    /// and the rest in white, so on a light background the card comes out
    /// looking blank in exactly the places that carry the person's identity.
    /// Averages the artwork under each box and compares it with the ink.
    /// </summary>
    public IReadOnlyList<ContrastWarning> Warnings(RgbaImage front, double minRatio = 3)
    {
        var result = new List<ContrastWarning>();
        if (Custom is null) return result;
        var inks = new (BoxKey Key, string Label, string Ink)[]
        {
            (BoxKey.Name, "name", CardConfig.NameText.Color),
            (BoxKey.Title, "job title", CardConfig.TitleText.Color),
            (BoxKey.EmployeeNo, "employee number", CardConfig.EmployeeNoText.Color),
            (BoxKey.Signature, "signature", CardConfig.Signature.Color),
        };
        foreach (var (key, label, ink) in inks)
        {
            var box = Box(key);
            var x0 = Math.Clamp((int)Math.Round(box.X / CardConfig.CardWidthMm * front.Width), 0, front.Width - 1);
            var y0 = Math.Clamp((int)Math.Round(box.Y / CardConfig.CardHeightMm * front.Height), 0, front.Height - 1);
            var x1 = Math.Min(front.Width, x0 + Math.Max(1, (int)Math.Round(box.W / CardConfig.CardWidthMm * front.Width)));
            var y1 = Math.Min(front.Height, y0 + Math.Max(1, (int)Math.Round(box.H / CardConfig.CardHeightMm * front.Height)));
            double r = 0, g = 0, b = 0;
            long n = 0;
            for (var y = y0; y < y1; y++)
            {
                for (var x = x0; x < x1; x++)
                {
                    var o = (y * front.Width + x) * 4;
                    r += front.Data[o]; g += front.Data[o + 1]; b += front.Data[o + 2];
                    n++;
                }
            }
            if (n == 0) continue;
            var back = Luminance(r / n, g / n, b / n);
            var (ir, ig, ib) = Hex(ink);
            var fore = Luminance(ir, ig, ib);
            var ratio = (Math.Max(fore, back) + 0.05) / (Math.Min(fore, back) + 0.05);
            if (ratio < minRatio) result.Add(new ContrastWarning(key, label, ink, Math.Round(ratio, 1), minRatio));
        }
        return result;
    }

    /* ---- keeping it ---------------------------------------------- */

    public void Save() => _store.Set(Key, Custom is null ? null : JsonSerializer.Serialize(Custom, StoreJson.Options));

    /// <summary>Bring back a design kept earlier. False (and the built-in design) when there is none, or it no longer reads.</summary>
    public bool Restore()
    {
        var saved = _store.Read<CustomTemplate?>(Key, null);
        var d = Defaults();
        if (saved is null || saved.PhotoRect.W <= 0 || saved.PhotoRect.H <= 0 || !d.Boxes.Keys.All(saved.Boxes.ContainsKey))
        {
            if (_store.Get(Key) is not null) _store.Set(Key, null);
            Custom = null;
            return false;
        }
        Custom = saved;
        return true;
    }

    /// <summary>Back to the card the app was built with, artwork and map.</summary>
    public void Clear()
    {
        Custom = null;
        Note = "";
        _store.Set(Key, null);
    }
}
