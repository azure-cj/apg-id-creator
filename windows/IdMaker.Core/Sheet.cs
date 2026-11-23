// Sheets of cards (imposition): how many fit on a sheet of paper, and where.

namespace IdMaker.Core;

/// <summary>
/// Pairs: front and back side by side in one cell, for cutting out and
/// sticking back to back. Duplex: one card per cell, all the fronts on one
/// page and all the backs on the next.
/// </summary>
public enum SheetMode { Pairs, Duplex }

/// <summary>How the printer turns the paper over for the second side.</summary>
public enum DuplexFlip { LongEdge, ShortEdge }

public sealed record SheetPlan(
    SheetMode Mode, bool Turned, bool Landscape, int Cols, int Rows,
    double PageWidthMm, double PageHeightMm, double CardW, double CardH, double CellW, double CellH,
    double Left, double Top)
{
    public int PerSheet => Cols * Rows;
}

/// <summary>One card side placed on a page. Entry indexes the list of cards being printed.</summary>
public readonly record struct SheetSlot(int Entry, bool Back, double X, double Y);

public sealed record SheetPage(IReadOnlyList<SheetSlot> Slots, bool Backs);

public static class Sheets
{
    /// <summary>
    /// The card is tried upright and turned on its side, and the paper is
    /// tried both ways round; whichever fits the most cards wins.
    /// </summary>
    public static SheetPlan? Plan(string? paperName, SheetMode mode)
    {
        var paper = CardConfig.PaperOrDefault(paperName);
        const double margin = CardConfig.SheetMarginMm, gap = CardConfig.SheetGapMm;
        var pair = mode == SheetMode.Pairs;

        SheetPlan? best = null;
        var bestCount = 0;
        var bestWaste = 0.0;
        foreach (var turned in new[] { false, true })               // the card on its side?
        {
            // one cell holds one card, or a front and a back next to each other
            var cardW = turned ? CardConfig.CardHeightMm : CardConfig.CardWidthMm;
            var cardH = turned ? CardConfig.CardWidthMm : CardConfig.CardHeightMm;
            var cellW = pair ? cardW * 2 + gap : cardW;
            var cellH = cardH;
            foreach (var landscape in new[] { false, true })        // the paper on its side?
            {
                var pw = landscape ? paper.HeightMm : paper.WidthMm;
                var ph = landscape ? paper.WidthMm : paper.HeightMm;
                var cols = (int)Math.Floor((pw - 2 * margin + gap) / (cellW + gap));
                var rows = (int)Math.Floor((ph - 2 * margin + gap) / (cellH + gap));
                if (cols < 1 || rows < 1) continue;
                var count = cols * rows;
                var waste = pw * ph - count * cellW * cellH;
                if (best is null || count > bestCount || (count == bestCount && waste < bestWaste))
                {
                    // centre the grid on the page
                    var gridW = cols * cellW + (cols - 1) * gap;
                    var gridH = rows * cellH + (rows - 1) * gap;
                    best = new SheetPlan(mode, turned, landscape, cols, rows, pw, ph,
                        cardW, cardH, cellW, cellH, (pw - gridW) / 2, (ph - gridH) / 2);
                    bestCount = count;
                    bestWaste = waste;
                }
            }
        }
        return best;
    }

    private static (double X, double Y) Cell(SheetPlan plan, int col, int row) =>
        (plan.Left + col * (plan.CellW + CardConfig.SheetGapMm), plan.Top + row * (plan.CellH + CardConfig.SheetGapMm));

    /// <summary>
    /// Lay `entries` cards out over as many pages as they need.
    ///
    /// In duplex mode each sheet is two pages, fronts then backs, and the
    /// backs are laid out so they end up behind the right fronts once the
    /// paper has been turned over. Turning a sheet about its long edge
    /// mirrors left-to-right, so the columns reverse; about the short edge
    /// it mirrors top-to-bottom, so the rows reverse instead. The fronts are
    /// never mirrored.
    /// </summary>
    public static IReadOnlyList<SheetPage> Pages(int entries, SheetPlan plan, DuplexFlip flip)
    {
        var pages = new List<SheetPage>();
        var per = plan.PerSheet;
        for (var start = 0; start < entries; start += per)
        {
            var n = Math.Min(per, entries - start);
            if (plan.Mode == SheetMode.Pairs)
            {
                var slots = new List<SheetSlot>(n * 2);
                var stride = plan.CardW + CardConfig.SheetGapMm;
                for (var i = 0; i < n; i++)
                {
                    var (x, y) = Cell(plan, i % plan.Cols, i / plan.Cols);
                    slots.Add(new SheetSlot(start + i, false, x, y));
                    slots.Add(new SheetSlot(start + i, true, x + stride, y));
                }
                pages.Add(new SheetPage(slots, false));
            }
            else
            {
                var fronts = new List<SheetSlot>(n);
                var backs = new List<SheetSlot>(n);
                for (var i = 0; i < n; i++)
                {
                    int col = i % plan.Cols, row = i / plan.Cols;
                    var (fx, fy) = Cell(plan, col, row);
                    fronts.Add(new SheetSlot(start + i, false, fx, fy));
                    var (bx, by) = flip == DuplexFlip.ShortEdge
                        ? Cell(plan, col, plan.Rows - 1 - row)
                        : Cell(plan, plan.Cols - 1 - col, row);
                    backs.Add(new SheetSlot(start + i, true, bx, by));
                }
                pages.Add(new SheetPage(fronts, false));
                pages.Add(new SheetPage(backs, true));
            }
        }
        return pages;
    }

    /// <summary>
    /// The eight crop-mark strokes around one card, as (x1, y1, x2, y2) in mm.
    /// </summary>
    public static IEnumerable<(double X1, double Y1, double X2, double Y2)> CropMarks(double x, double y, double w, double h)
    {
        const double o = CardConfig.CropMarkOffsetMm, len = CardConfig.CropMarkMm;
        foreach (var cx in new[] { x, x + w })
        {
            foreach (var cy in new[] { y, y + h })
            {
                var sx = cx == x ? -1 : 1;
                var sy = cy == y ? -1 : 1;
                yield return (cx + sx * o, cy, cx + sx * (o + len), cy);
                yield return (cx, cy + sy * o, cx, cy + sy * (o + len));
            }
        }
    }
}
