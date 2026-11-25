// Photo pipeline.
//
//   1. validate      PNG / JPG / WebP, under the size limit
//   2. decode        (the app does this) then shrink to at most 2400 px tall
//   3. classify      sample the border: transparent / green / blue /
//                    checkerboard / other
//   4. chroma key    green or blue -> alpha, with de-spill on the edges,
//                    then a half-pixel alpha erosion to trim the halo
//   5. find the head on a copy no taller than 800 px, from the alpha
//                    silhouette. Reject if the key ate clothing the same
//                    colour as the backdrop; warn if the face is blurred
//   6. place         scale so the head is HeadWidth mm wide, head top at
//                    HeadTop, centred at HeadCenterX
//
// Nothing here stores or sends the photograph anywhere.

using System.Text.RegularExpressions;

namespace IdMaker.Core;

/// <summary>Pixels as straight (not premultiplied) RGBA, row by row.</summary>
public sealed class RgbaImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Data { get; }

    public RgbaImage(int width, int height, byte[]? data = null)
    {
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width), "An image is at least one pixel each way.");
        Width = width;
        Height = height;
        Data = data ?? new byte[width * height * 4];
        if (Data.Length != width * height * 4) throw new ArgumentException("The pixel data does not match the size.", nameof(data));
    }

    public RgbaImage Clone() => new(Width, Height, (byte[])Data.Clone());

    /// <summary>
    /// A smaller copy, each output pixel the area average of the pixels it
    /// covers, weighted by alpha so a transparent pixel's colour cannot
    /// bleed into the edge of the person.
    /// </summary>
    public RgbaImage Shrink(int newWidth, int newHeight)
    {
        newWidth = Math.Max(1, newWidth);
        newHeight = Math.Max(1, newHeight);
        if (newWidth == Width && newHeight == Height) return this;

        // horizontal pass into premultiplied doubles, then vertical
        var tmp = new double[newWidth * Height * 4];
        var xw = Weights(Width, newWidth);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < newWidth; x++)
            {
                double r = 0, g = 0, b = 0, a = 0;
                var (start, weights) = xw[x];
                for (var k = 0; k < weights.Length; k++)
                {
                    var o = (y * Width + start + k) * 4;
                    var wa = weights[k] * Data[o + 3];
                    r += Data[o] * wa; g += Data[o + 1] * wa; b += Data[o + 2] * wa; a += wa;
                }
                var t = (y * newWidth + x) * 4;
                tmp[t] = r; tmp[t + 1] = g; tmp[t + 2] = b; tmp[t + 3] = a;
            }
        }

        var result = new RgbaImage(newWidth, newHeight);
        var yw = Weights(Height, newHeight);
        for (var y = 0; y < newHeight; y++)
        {
            var (start, weights) = yw[y];
            for (var x = 0; x < newWidth; x++)
            {
                double r = 0, g = 0, b = 0, a = 0;
                for (var k = 0; k < weights.Length; k++)
                {
                    var t = ((start + k) * newWidth + x) * 4;
                    r += tmp[t] * weights[k]; g += tmp[t + 1] * weights[k]; b += tmp[t + 2] * weights[k]; a += tmp[t + 3] * weights[k];
                }
                var o = (y * newWidth + x) * 4;
                if (a > 0)
                {
                    result.Data[o] = ToByte(r / a); result.Data[o + 1] = ToByte(g / a); result.Data[o + 2] = ToByte(b / a);
                }
                result.Data[o + 3] = ToByte(a);
            }
        }
        return result;
    }

    private static byte ToByte(double v) => (byte)Math.Clamp((int)Math.Floor(v + 0.5), 0, 255);

    // For each output pixel: the first input pixel it covers and how much of each (weights sum to 1).
    private static (int Start, double[] Weights)[] Weights(int from, int to)
    {
        var result = new (int, double[])[to];
        var scale = (double)from / to;
        for (var i = 0; i < to; i++)
        {
            double a = i * scale, b = (i + 1) * scale;
            var start = (int)Math.Floor(a);
            var end = Math.Min(from, (int)Math.Ceiling(b));
            var w = new double[Math.Max(1, end - start)];
            for (var k = 0; k < w.Length; k++)
            {
                var lo = Math.Max(a, start + k);
                var hi = Math.Min(b, start + k + 1);
                w[k] = Math.Max(0, hi - lo) / scale;
            }
            result[i] = (start, w);
        }
        return result;
    }
}

/// <summary>Removed: the photo had an ordinary background, and the app cut the person out of it.</summary>
public enum PhotoBackground { Transparent, Green, Blue, Checkerboard, Other, Removed }

/// <summary>
/// Finds the person in an ordinary photograph, on this computer. Returns one
/// value per pixel, 0 (background) to 255 (person), or null when it cannot run.
/// </summary>
public interface IBackgroundRemover
{
    byte[]? Matte(RgbaImage image);
}

/// <summary>The head, in pixels of the full-size picture.</summary>
public sealed record HeadInfo(double Top, double Width, double CenterX, double NeckY);

/// <summary>Where the picture goes on the card (mm), and how far short of the window's bottom it stops.</summary>
public sealed record PhotoPlacement(double X, double Y, double W, double H, double ShortBy)
{
    /// <summary>
    /// Apply the operator's manual adjustment. Zoom is anchored at the head
    /// position, so the face stays put while the photo grows or shrinks.
    /// </summary>
    public PhotoPlacement Adjusted(PhotoAdjust adjust, PhotoSpec spec)
    {
        if (adjust.IsNone) return this;
        double ax = spec.HeadCenterX, ay = spec.HeadTop;
        return this with
        {
            X = ax + (X - ax) * adjust.Zoom + adjust.Dx,
            Y = ay + (Y - ay) * adjust.Zoom + adjust.Dy,
            W = W * adjust.Zoom,
            H = H * adjust.Zoom,
        };
    }
}

/// <summary>The operator's manual nudge on top of the automatic placement: mm, and a zoom factor.</summary>
public readonly record struct PhotoAdjust(double Dx, double Dy, double Zoom)
{
    public static readonly PhotoAdjust None = new(0, 0, 1);
    public bool IsNone => Dx == 0 && Dy == 0 && Zoom == 1;
}

public sealed record PhotoCheck
{
    public bool Ok { get; init; }
    public string Problem { get; init; } = "";
    /// <summary>True when the AI prompt is the way out of this rejection.</summary>
    public bool FixIsPrompt { get; init; }
    public HeadInfo? Head { get; init; }
    public PhotoPlacement? Placement { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed record PhotoResult(RgbaImage Image, string FileName, PhotoBackground Background, PhotoCheck Check);

public static partial class PhotoPipeline
{
    public const string MsgHasBackground = "This photo still has its background, and the background remover is not available on this computer. Use a photo on a plain green background, or a cut-out PNG.";
    public const string MsgCheckerboard = "This photo has a grey checkerboard painted into it, not a real transparent background. The ID needs a plain green background instead.";
    public const string MsgRoomBehind = "There’s a room behind the person in this photo. Take it again against a plain wall, or use a cut-out PNG or a photo on a green background.";

    // What the remover cut away must be a wall, not a room. A wall is one colour,
    // or two (a darker panel, a shadow), shading gently; a room behind somebody
    // shows a door, a window, shelves: three or more colours, each a real share.
    // Colours closer than this (0–441 across the RGB cube) count as one, so a
    // wall's shading does not split it.
    private const double SameColour = 48;
    private const double ColourShare = 0.04;    // a colour this much of the background counts
    private const int MostWallColours = 2;
    private const int MatteBackground = 32;     // matte at or below this is background

    private const int AlphaSolid = 128;         // alpha at or above this counts as "person"
    // Clothing the same colour as the backdrop is keyed out with it. Two signs:
    // holes inside the body outline, or the body collapsing below the shoulders.
    private const double MaxHoleFraction = 0.06;
    private const double MinTorsoRatio = 0.6;   // width just below the shoulders, against the shoulders
    private const int MinRowsToJudgeBody = 8;
    // Sharpness: variance of the Laplacian over the face, on the analysis copy.
    private const double BlurVariance = 55;
    // Narrower than this and the face cannot be seen: the photo is refused. Not PhotoSpec.MinHeadPx
    // (180, the card's own), under which a photo is used with a warning that it may print soft.
    private const double UnusableHeadPx = 40;
    public const int AnalysisMaxHeight = 800;   // head detection works on a copy this tall
    public const int WorkingMaxHeight = 2400;   // the keyed image kept for the card

    [GeneratedRegex(@"\.(png|jpe?g|webp)$", RegexOptions.IgnoreCase)] private static partial Regex Accepted();
    [GeneratedRegex(@"\.(heic|heif)$", RegexOptions.IgnoreCase)] private static partial Regex Heic();

    private static int Round(double v) => (int)Math.Floor(v + 0.5);

    /// <summary>Why this file cannot be used as a picture, or "" when it can be tried.</summary>
    public static string FileProblem(string fileName, long sizeBytes, string what = "photo")
    {
        if (Heic().IsMatch(fileName))
            return "This is an iPhone HEIC photo, which this app can’t open. On the iPhone, share it as JPEG (Settings → Camera → Formats → Most Compatible), or open it in Windows Photos and save it as JPG, then use that.";
        if (!Accepted().IsMatch(fileName)) return $"Please choose a PNG, JPG or WebP {what}.";
        if (sizeBytes > CardConfig.Photo.MaxFileMb * 1024 * 1024) return $"This file is over {CardConfig.Photo.MaxFileMb:0} MB. Please choose a smaller {what}.";
        return "";
    }

    /* ---- 3. classify the background ------------------------------ */

    public static PhotoBackground ClassifyBackground(RgbaImage img)
    {
        int w = img.Width, h = img.Height;
        var d = img.Data;
        var idx = new List<int>();
        var step = Math.Max(1, (w + h) / 400);
        for (var x = 0; x < w; x += step) { idx.Add(x); idx.Add((h - 1) * w + x); idx.Add(Math.Min(h - 1, 1) * w + x); }   // top two rows + bottom
        for (var y = 0; y < h; y += step) { idx.Add(y * w); idx.Add(y * w + w - 1); }                                   // left + right columns

        int transparent = 0, green = 0, blue = 0, grey = 0;
        var greyLevels = new Dictionary<int, int>();
        foreach (var i in idx)
        {
            var o = i * 4;
            int r = d[o], g = d[o + 1], b = d[o + 2], a = d[o + 3];
            if (a < 20) { transparent++; continue; }
            if (g > 120 && g - Math.Max(r, b) > 60) green++;
            else if (b > 120 && b - Math.Max(r, g) > 60) blue++;
            if (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) < 16 && r > 170)
            {
                grey++;
                var level = Round(r / 12.0);
                greyLevels[level] = greyLevels.GetValueOrDefault(level) + 1;
            }
        }
        double n = idx.Count;
        if (transparent / n > 0.4) return PhotoBackground.Transparent;
        if (green / n > 0.5) return PhotoBackground.Green;
        if (blue / n > 0.5) return PhotoBackground.Blue;
        if (grey / n > 0.6)
        {
            // two distinct light greys along the border = a painted checkerboard
            var counts = greyLevels.Values.OrderByDescending(c => c).ToList();
            if (counts.Count >= 2 && (double)counts[1] / grey > 0.2) return PhotoBackground.Checkerboard;
        }
        return PhotoBackground.Other;
    }

    /* ---- 4. chroma key ------------------------------------------- */

    // In place. dominance = key channel minus the larger of the other two.
    //   <= 0      untouched
    //   0..30     colour only de-spilled (hair edges, reflections)
    //   30..110   alpha fades out linearly
    //   >= 110    fully transparent
    public static void ChromaKey(RgbaImage img, PhotoBackground key)
    {
        var d = img.Data;
        var green = key == PhotoBackground.Green;
        for (var i = 0; i < d.Length; i += 4)
        {
            int r = d[i], g = d[i + 1], b = d[i + 2];
            var dom = green ? g - Math.Max(r, b) : b - Math.Max(r, g);
            if (dom <= 0) continue;
            if (green) d[i + 1] = (byte)Math.Max(r, b); else d[i + 2] = (byte)Math.Max(r, g);     // de-spill
            if (dom <= 30) continue;
            var keep = dom >= 110 ? 0 : 1 - (dom - 30) / 80.0;
            d[i + 3] = (byte)Round(d[i + 3] * keep);
        }
    }

    // After keying, the outermost semi-transparent pixels can still carry a
    // faint tinted halo. Pull each edge pixel's alpha halfway towards the
    // minimum alpha in its 3x3 neighbourhood: a half-pixel erosion that
    // trims the fringe without eating into hair.
    public static void TrimEdgeHalo(RgbaImage img)
    {
        int w = img.Width, h = img.Height;
        var d = img.Data;
        var src = new byte[w * h];
        for (var i = 0; i < src.Length; i++) src[i] = d[i * 4 + 3];
        for (var y = 1; y < h - 1; y++)
        {
            for (var x = 1; x < w - 1; x++)
            {
                var a = src[y * w + x];
                if (a is 0 or 255) continue;
                var m = a;
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var n = src[(y + dy) * w + x + dx];
                        if (n < m) m = n;
                    }
                d[(y * w + x) * 4 + 3] = (byte)Round((a + m) / 2.0);
            }
        }
    }

    /* ---- 5. find the head, 6. place it ---------------------------- */

    // Variance of the Laplacian over a box — the standard cheap sharpness
    // measure. High on a crisp face, near zero on a blurred one.
    private static double SharpnessOf(RgbaImage img, double fx0, double fy0, double fx1, double fy1)
    {
        int w = img.Width, h = img.Height;
        var d = img.Data;
        int x0 = Math.Max(1, Round(fx0)), y0 = Math.Max(1, Round(fy0));
        int x1 = Math.Min(w - 2, Round(fx1)), y1 = Math.Min(h - 2, Round(fy1));
        if (x1 - x0 < 8 || y1 - y0 < 8) return double.PositiveInfinity;      // too small to judge
        double Lum(int i) => (d[i * 4] * 299 + d[i * 4 + 1] * 587 + d[i * 4 + 2] * 114) / 1000.0;
        double sum = 0, sumSq = 0;
        var n = 0;
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var i = y * w + x;
                if (d[i * 4 + 3] < AlphaSolid) continue;                     // only the person
                var l = 4 * Lum(i) - Lum(i - 1) - Lum(i + 1) - Lum(i - w) - Lum(i + w);
                sum += l; sumSq += l * l; n++;
            }
        }
        if (n < 64) return double.PositiveInfinity;
        var mean = sum / n;
        return sumSq / n - mean * mean;
    }

    /// <summary>
    /// Find the head on the analysis copy and work out where the full-size
    /// picture goes on the card. `keyed` says a backdrop colour was removed,
    /// which is when clothing can have gone with it.
    /// </summary>
    public static PhotoCheck FindHeadAndPlace(RgbaImage analysis, int fullWidth, bool keyed, PhotoSpec spec)
    {
        int w = analysis.Width, h = analysis.Height;
        var data = analysis.Data;
        var scale = (double)fullWidth / w;              // analysis px -> full-size px

        // must really be a cut-out: enough transparent pixels, and a mostly clear top row
        var transparent = 0;
        for (var i = 3; i < data.Length; i += 4) if (data[i] < 20) transparent++;
        var topClear = 0;
        for (var x = 0; x < w; x++) if (data[x * 4 + 3] < 20) topClear++;
        if ((double)transparent / (w * h) < 0.08 || (double)topClear / w < 0.5)
            return new PhotoCheck { Problem = MsgHasBackground, FixIsPrompt = true };

        // per-row silhouette: first and last solid column, and the count of solid pixels
        var first = new int[h];
        var last = new int[h];
        var count = new int[h];
        Array.Fill(first, -1);
        Array.Fill(last, -1);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (data[(y * w + x) * 4 + 3] >= AlphaSolid) { if (first[y] < 0) first[y] = x; last[y] = x; count[y]++; }
            }
        }
        var minCount = Math.Max(2, Round(w * 0.01));
        var top = 0;
        while (top < h && count[top] < minCount) top++;
        if (top >= h) return new PhotoCheck { Problem = "No person found in this photo. Please use the cut-out PNG." };

        double RowWidth(int y) => count[y] < minCount ? 0 : last[y] - first[y] + 1;

        /* ---- where the head ends and the body begins ----------------
           Walking down from the top and calling the first narrowing the
           end of the head does not work: a head is not reliably convex.
           One real photo had a tuft of hair standing up, and the dip
           between the tuft and the rest of the hair ended the head fifty
           pixels in. The head came out four times too small, so the card
           was scaled four times too large to make it 15 mm.

           Read it from the other end instead. The widest row in the
           picture is the shoulders or the chest; climb back up from there
           and the first place the silhouette stops narrowing is the neck.
           The head is then the widest row above the neck, whatever shape
           the hair is.
           ------------------------------------------------------------ */

        // smoothed, because single rows of hair, stray pixels and JPEG
        // fringes are not anatomy and a local minimum must not be made of them
        var smoothBy = Math.Max(1, Round(h * 0.015));
        var widths = new double[h];
        for (var y = 0; y < h; y++) widths[y] = RowWidth(y);
        var sums = new double[h + 1];
        for (var y = 0; y < h; y++) sums[y + 1] = sums[y] + widths[y];
        double Smooth(int y)
        {
            int a = Math.Max(0, y - smoothBy), b = Math.Min(h - 1, y + smoothBy);
            return (sums[b + 1] - sums[a]) / (b - a + 1);
        }

        double maxW = 0;
        var widestY = top;
        for (var y = top; y < h; y++) { var v = Smooth(y); if (v > maxW) { maxW = v; widestY = y; } }

        var neckY = -1;
        for (var y = widestY - 1; y > top; y--)
        {
            if (Smooth(y) < Smooth(y + 1)) continue;       // still narrowing on the way up
            neckY = y + 1;                                 // it stopped: that is the neck
            break;
        }
        if (neckY < 0) neckY = Round((top + widestY) / 2.0);

        double headW = 0;
        for (var y = top; y < neckY; y++) if (widths[y] > headW) headW = widths[y];

        var shoulderY = -1;
        for (var y = neckY; y < h; y++) if (Smooth(y) > headW * 1.25) { shoulderY = y; break; }

        // How a person is framed is the photographer's call, not a reason
        // to turn the photo away. With no shoulders to measure against, the
        // head is taken to be the widest part of what is there, the photo is
        // placed by that, and the operator is told to look at it.
        var framing = new List<string>();
        if (shoulderY < 0 || headW == 0)
        {
            headW = 0;
            for (var y = top; y < h; y++) if (widths[y] > headW) headW = widths[y];
            neckY = h - 1;
            shoulderY = -1;
            framing.Add("The shoulders aren’t visible in this photo, so it was placed by the head alone. Move or resize it on the card if it looks off.");
        }

        // head centre = mean centre of the wide rows between the top and the neck
        double sum = 0;
        var n = 0;
        for (var y = top; y <= neckY && y < h; y++) if (RowWidth(y) >= headW * 0.8) { sum += (first[y] + last[y]) / 2.0; n++; }
        var centerX = sum / Math.Max(1, n);

        var head = new HeadInfo(top * scale, headW * scale, centerX * scale, neckY * scale);
        if (head.Width < UnusableHeadPx) return new PhotoCheck { Head = head, Problem = "This photo is too small to show the face. Please use a larger photo." };
        if (top <= 1) framing.Add("The top of the head reaches the edge of the photo. Check on the card that nothing is cut off.");

        // Did the key eat the clothing? Holes inside the outline below the
        // neck, or a body that collapses below the shoulders, both mean yes.
        double span = 0, holes = 0;
        for (var y = neckY; y < h; y++)
        {
            var rw = RowWidth(y);
            if (rw == 0) continue;
            span += rw;
            holes += rw - count[y];
        }
        var holeFraction = span > 0 ? holes / span : 0;

        // the band directly under the shoulders: a torso has to be there
        double torsoRatio = 1;
        if (shoulderY >= 0)
        {
            var shoulderW = Math.Max(RowWidth(shoulderY), headW);
            var bandEnd = Math.Min(h - 1, shoulderY + (int)headW);
            var band = new List<double>();
            for (var y = shoulderY; y <= bandEnd; y++) band.Add(RowWidth(y));
            band.Sort();
            if (band.Count >= MinRowsToJudgeBody) torsoRatio = band[band.Count / 2] / shoulderW;
        }

        if (keyed && (holeFraction > MaxHoleFraction || torsoRatio < MinTorsoRatio))
        {
            return new PhotoCheck
            {
                Head = head, FixIsPrompt = true,
                Problem = "Clothing is too close to the background colour — redo the photo with a blue background or a different top.",
            };
        }

        var sharpness = SharpnessOf(analysis, centerX - headW / 2, top, centerX + headW / 2, neckY);

        var warnings = new List<string>(framing);
        if (head.Width < spec.MinHeadPx) warnings.Add("This is a small photo, so the face may print slightly soft. A larger photo prints sharper, but this one can be used.");
        if (sharpness < BlurVariance) warnings.Add("This photo looks blurred. Check the face closely before printing — a sharper photo is better.");
        for (var y = top; y <= neckY && y < h; y++)
        {
            if (count[y] >= minCount && (first[y] <= 0 || last[y] >= w - 1))
            {
                warnings.Add("The head touches the side of the photo. Check that nothing important is cut off.");
                break;
            }
        }

        // placement in mm: scale so the head is HeadWidth mm wide
        double fullW = fullWidth, fullH = h * scale;
        var mmPerPx = spec.HeadWidth / head.Width;
        var belowHeadTopPx = fullH - head.Top;
        double shortBy = 0;                       // mm of window the photo cannot fill
        if (spec.HeadTop + belowHeadTopPx * mmPerPx < spec.ReachY)
        {
            // The photo is cropped high: enlarging it makes the body reach
            // the bottom of the window instead of stopping in mid-air. That
            // is worth a little, but not any amount — the head being the
            // same size on every card is the whole point of measuring it.
            var needed = (spec.ReachY - spec.HeadTop) / belowHeadTopPx;
            var most = mmPerPx * spec.MaxEnlarge;
            if (needed > most)
            {
                warnings.Add("This photo is cropped too close to fill the card, so it stops short of the bottom. "
                    + "The face is the right size; a photo showing the chest fills the space properly.");
                mmPerPx = most;
                shortBy = spec.ReachY - (spec.HeadTop + belowHeadTopPx * mmPerPx);
            }
            else
            {
                if (needed / mmPerPx > 1.08)
                    warnings.Add("This photo is cropped close, so the head is shown slightly larger than usual. A photo that shows more of the chest gives a more consistent card.");
                mmPerPx = needed;
            }
        }

        return new PhotoCheck
        {
            Ok = true, Head = head,
            Placement = new PhotoPlacement(
                spec.HeadCenterX - head.CenterX * mmPerPx,
                spec.HeadTop - head.Top * mmPerPx,
                fullW * mmPerPx,
                fullH * mmPerPx,
                // a photo that stops inside the window is cut off square
                // against the card, which reads as a sticker coming unstuck.
                // Say by how much, and the card softens the edge instead.
                Math.Round(Math.Max(0, shortBy), 2)),
            Warnings = warnings,
        };
    }

    /// <summary>Steps 2 to 6 for a decoded picture. Never throws for a picture that merely cannot be used.</summary>
    public static PhotoResult Analyse(RgbaImage decoded, string fileName, PhotoSpec spec, IBackgroundRemover? remover = null)
    {
        var k = Math.Min(1.0, (double)WorkingMaxHeight / decoded.Height);
        var pixels = k < 1
            ? decoded.Shrink(Math.Max(1, Round(decoded.Width * k)), Math.Max(1, Round(decoded.Height * k)))
            : decoded.Clone();

        var background = ClassifyBackground(pixels);
        PhotoCheck check;
        var room = false;

        // An ordinary photo: cut the person out here rather than sending
        // anybody's face to an outside service to have it done.
        if (background is PhotoBackground.Other or PhotoBackground.Checkerboard && remover?.Matte(pixels) is { } matte
            && matte.Length == pixels.Width * pixels.Height)
        {
            // judged on the colours as taken, before the cut-out clears them
            room = WallColours(pixels, matte) > MostWallColours;
            for (var i = 0; i < matte.Length; i++) pixels.Data[i * 4 + 3] = (byte)(pixels.Data[i * 4 + 3] * matte[i] / 255);
            TrimEdgeHalo(pixels);
            background = PhotoBackground.Removed;
        }

        if (background == PhotoBackground.Checkerboard) check = new PhotoCheck { Problem = MsgCheckerboard, FixIsPrompt = true };
        else if (background == PhotoBackground.Other) check = new PhotoCheck { Problem = MsgHasBackground, FixIsPrompt = true };
        // the cut-out would work, but an ID photo is taken against a wall: the prompt (a green background) is one fix
        else if (room) check = new PhotoCheck { Problem = MsgRoomBehind, FixIsPrompt = true };
        else
        {
            var keyed = background is PhotoBackground.Green or PhotoBackground.Blue;
            if (keyed) { ChromaKey(pixels, background); TrimEdgeHalo(pixels); }
            var ak = Math.Min(1.0, (double)AnalysisMaxHeight / pixels.Height);
            var analysis = ak < 1
                ? pixels.Shrink(Math.Max(1, Round(pixels.Width * ak)), Math.Max(1, Round(pixels.Height * ak)))
                : pixels;
            check = FindHeadAndPlace(analysis, pixels.Width, keyed, spec);
            if (!check.Ok && background == PhotoBackground.Removed && check.Problem == MsgHasBackground)
            {
                check = check with
                {
                    Problem = "The app could not tell the person from the background in this photo. Try one taken against a plain wall, or a cut-out PNG.",
                };
            }
        }
        return new PhotoResult(pixels, fileName, background, check);
    }

    /// <summary>
    /// How many colours the background the remover found is made of, counting
    /// only those that cover a real share of it, and taking colours closer than
    /// SameColour as one. A wall comes to one or two; a room to three or more.
    /// A photo with too little background to judge counts as a wall.
    /// </summary>
    public static int WallColours(RgbaImage img, byte[] matte)
    {
        // bins of 16 levels a channel, filled from a grid of samples
        var bins = new Dictionary<int, int>();
        var step = Math.Max(1, (int)Math.Sqrt((double)img.Width * img.Height / 40000));
        var total = 0;
        for (var y = 0; y < img.Height; y += step)
            for (var x = 0; x < img.Width; x += step)
            {
                var i = y * img.Width + x;
                if (matte[i] > MatteBackground) continue;
                var o = i * 4;
                var key = (img.Data[o] >> 4) << 8 | (img.Data[o + 1] >> 4) << 4 | img.Data[o + 2] >> 4;
                bins[key] = bins.GetValueOrDefault(key) + 1;
                total++;
            }
        if (total < 500) return 1;

        // the biggest bins first; each joins the first colour it is close to, or starts one
        var colours = new List<(double R, double G, double B, int Count)>();
        foreach (var (key, count) in bins.OrderByDescending(kv => kv.Value))
        {
            double r = (key >> 8) * 16 + 8, g = (key >> 4 & 15) * 16 + 8, b = (key & 15) * 16 + 8;
            var at = colours.FindIndex(c => Math.Sqrt((c.R - r) * (c.R - r) + (c.G - g) * (c.G - g) + (c.B - b) * (c.B - b)) <= SameColour);
            if (at < 0) colours.Add((r, g, b, count));
            else colours[at] = colours[at] with { Count = colours[at].Count + count };
        }
        return colours.Count(c => (double)c.Count / total >= ColourShare);
    }
}
