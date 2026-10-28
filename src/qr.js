/* ------------------------------------------------------------------
   QR code on the front (bottom-left white square)
   ------------------------------------------------------------------
   Uses vendor/qrcode-generator.js (Kazuhiko Arase, MIT). The module
   grid is computed once and cached; drawing snaps every module to the
   print pixel grid so the code stays crisp at 300 DPI.
   ------------------------------------------------------------------ */

let qrCache = null;

function qrModules() {
  const key = `${geo().qr.content}|${geo().qr.errorCorrection}`;
  if (!qrCache || qrCache.key !== key) {
    const qr = qrcode(0 /* auto version */, geo().qr.errorCorrection);
    qr.addData(geo().qr.content);
    qr.make();
    const n = qr.getModuleCount();
    const dark = new Array(n * n);
    for (let r = 0; r < n; r++) for (let c = 0; c < n; c++) dark[r * n + c] = qr.isDark(r, c);
    qrCache = { key, size: n, dark };
  }
  return qrCache;
}

function drawQr(ctx, pxPerMm) {
  const { size: n, dark } = qrModules();
  const printPpm = printPxPerMm();
  const snap = (mm) => Math.round(mm * printPpm) / printPpm;   // to whole print pixels
  const sq = geo().qr.square;
  const x0 = snap(sq.x), y0 = snap(sq.y), side = snap(sq.size);

  ctx.fillStyle = geo().qr.background;
  ctx.fillRect(x0 * pxPerMm, y0 * pxPerMm, side * pxPerMm, side * pxPerMm);

  // module size: whole print pixels, leaving a 2-module quiet zone each side
  const moduleMm = Math.max(1, Math.floor((side * printPpm) / (n + 4))) / printPpm;
  const codeMm = n * moduleMm;
  const left = snap(x0 + (side - codeMm) / 2);
  const top = snap(y0 + (side - codeMm) / 2);

  ctx.fillStyle = geo().qr.color;
  for (let r = 0; r < n; r++) {
    for (let c = 0; c < n; c++) {
      if (!dark[r * n + c]) continue;
      const px = (left + c * moduleMm) * pxPerMm, py = (top + r * moduleMm) * pxPerMm;
      ctx.fillRect(px, py, (left + (c + 1) * moduleMm) * pxPerMm - px, (top + (r + 1) * moduleMm) * pxPerMm - py);
    }
  }
}
