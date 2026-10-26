/* ------------------------------------------------------------------
   Text fitting
   ------------------------------------------------------------------
   Given a text box (CONFIG.text.*), find the largest point size that
   lets the text fit, wrapping onto up to maxLines balanced lines.
   The fit is always measured at PRINT resolution so the preview and
   the PDF break lines identically.
   ------------------------------------------------------------------ */

const SIZE_STEP_PT = 0.25;

function cssFont(spec, px) {
  const f = CONFIG.fonts[spec.font];
  return `${f.weight} ${px}px "${f.family}"`;
}

// Vertical metrics for one line at the current ctx.font.
function lineMetrics(ctx, spec, px) {
  return {
    capH: ctx.measureText('H').actualBoundingBoxAscent,
    desc: spec.uppercase
      ? ctx.measureText('Q,').actualBoundingBoxDescent
      : ctx.measureText('gjpqy').actualBoundingBoxDescent,
    lineH: px * spec.lineHeight,
  };
}

// Height of an n-line block: first cap height + (n-1) line advances + last descender.
const blockHeight = (m, n) => m.capH + (n - 1) * m.lineH + m.desc;

// Split `words` into exactly `n` lines so the widest line is as narrow as
// possible (keeps "Christopher Joseph / V. Aureo" rather than a long first
// line and a one-word second line). Returns {lines, width} or null.
function balancedLines(ctx, words, n) {
  if (words.length < n) return null;
  const widths = new Map();
  const widthOf = (s) => {
    let w = widths.get(s);
    if (w === undefined) { w = ctx.measureText(s).width; widths.set(s, w); }
    return w;
  };
  let best = null;
  const walk = (start, left, lines, widest) => {
    if (best && widest >= best.width) return;          // prune
    if (left === 1) {
      const line = words.slice(start).join(' ');
      const w = Math.max(widest, widthOf(line));
      if (!best || w < best.width) best = { lines: [...lines, line], width: w };
      return;
    }
    for (let end = start + 1; end <= words.length - (left - 1); end++) {
      const line = words.slice(start, end).join(' ');
      walk(end, left - 1, [...lines, line], Math.max(widest, widthOf(line)));
    }
  };
  walk(0, n, [], 0);
  return best;
}

/**
 * fitText(ctx, spec, text, pxPerMm) -> { lines, sizePt, fits }
 * ctx is any 2D context used only for measuring.
 */
function fitText(ctx, spec, text, pxPerMm) {
  let s = collapseSpaces(text);
  if (spec.uppercase) s = s.toLocaleUpperCase('en-US');
  if (!s) return { lines: [], sizePt: spec.maxSizePt, fits: true };

  const boxW = spec.box.w * pxPerMm;
  const boxH = spec.box.h * pxPerMm;
  const words = s.split(' ');

  for (let pt = spec.maxSizePt; pt >= spec.minSizePt - 1e-9; pt -= SIZE_STEP_PT) {
    const px = pt * PT_TO_MM * pxPerMm;
    ctx.font = cssFont(spec, px);
    const m = lineMetrics(ctx, spec, px);
    if (ctx.measureText(s).width <= boxW && blockHeight(m, 1) <= boxH) {
      return { lines: [s], sizePt: pt, fits: true };
    }
    for (let n = 2; n <= spec.maxLines && blockHeight(m, n) <= boxH; n++) {
      const b = balancedLines(ctx, words, n);
      if (b && b.width <= boxW) return { lines: b.lines, sizePt: pt, fits: true };
    }
  }

  // Nothing fits: draw at the minimum size anyway so the user sees the
  // problem on the card, and report fits:false so the form can complain.
  const px = spec.minSizePt * PT_TO_MM * pxPerMm;
  ctx.font = cssFont(spec, px);
  const m = lineMetrics(ctx, spec, px);
  let lines = [s];
  for (let n = spec.maxLines; n >= 2; n--) {
    const b = blockHeight(m, n) <= boxH ? balancedLines(ctx, words, n) : null;
    if (b) { lines = b.lines; break; }
  }
  return { lines, sizePt: spec.minSizePt, fits: false };
}

/** Draw a fitted block into its box on ctx at pxPerMm. */
function drawFittedText(ctx, spec, fit, pxPerMm) {
  if (!fit.lines.length) return;
  const px = fit.sizePt * PT_TO_MM * pxPerMm;
  ctx.font = cssFont(spec, px);
  const m = lineMetrics(ctx, spec, px);
  const n = fit.lines.length;
  const { x, y, w, h } = spec.box;

  let firstBaseline;
  if (spec.vAlign === 'top')         firstBaseline = y * pxPerMm + m.capH;
  else if (spec.vAlign === 'bottom') firstBaseline = (y + h) * pxPerMm - m.desc - (n - 1) * m.lineH;
  else /* baseline */                firstBaseline = (y + h) * pxPerMm - (n - 1) * m.lineH;

  const anchorX = spec.align === 'center' ? (x + w / 2) * pxPerMm
                : spec.align === 'right'  ? (x + w) * pxPerMm
                : x * pxPerMm;

  ctx.textAlign = spec.align;
  ctx.textBaseline = 'alphabetic';
  ctx.fillStyle = spec.color;
  fit.lines.forEach((line, i) => ctx.fillText(line, anchorX, firstBaseline + i * m.lineH));
}
