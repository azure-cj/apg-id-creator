/* ------------------------------------------------------------------
   Print file: a two-page PDF, one card side per page, page size =
   card size, each page a single full-bleed 300 DPI image.
   ------------------------------------------------------------------
   Written by hand (no library): PDF 1.4, images stored losslessly as
   /FlateDecode RGB using the browser's CompressionStream. If that API
   is missing (very old browsers) the pages fall back to high-quality
   JPEG (/DCTDecode).
   ------------------------------------------------------------------ */

const MM_TO_PT = 72 / 25.4;

async function zlibDeflate(bytes) {
  if (typeof CompressionStream === 'undefined') return null;
  const cs = new CompressionStream('deflate');            // zlib-wrapped, what /FlateDecode expects
  const writer = cs.writable.getWriter();
  writer.write(bytes);
  writer.close();
  return new Uint8Array(await new Response(cs.readable).arrayBuffer());
}

function base64ToBytes(b64) {
  const bin = atob(b64);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}

// canvas -> { filter, bytes, width, height }
async function encodeCanvas(canvas) {
  const { width, height } = canvas;
  const rgba = canvas.getContext('2d').getImageData(0, 0, width, height).data;
  const rgb = new Uint8Array(width * height * 3);
  for (let i = 0, j = 0; i < rgba.length; i += 4, j += 3) { rgb[j] = rgba[i]; rgb[j + 1] = rgba[i + 1]; rgb[j + 2] = rgba[i + 2]; }
  const flate = await zlibDeflate(rgb);
  if (flate) return { filter: '/FlateDecode', bytes: flate, width, height };
  const jpeg = canvas.toDataURL('image/jpeg', 0.95).split(',')[1];
  return { filter: '/DCTDecode', bytes: base64ToBytes(jpeg), width, height };
}

// Text string as UTF-16BE hex so names with ñ, é, etc. survive.
function pdfText(s) {
  let hex = 'FEFF';
  for (const ch of s) {
    const cp = ch.codePointAt(0);
    const units = cp > 0xffff ? [0xd800 + ((cp - 0x10000) >> 10), 0xdc00 + ((cp - 0x10000) & 0x3ff)] : [cp];
    for (const u of units) hex += u.toString(16).padStart(4, '0');
  }
  return `<${hex}>`;
}

function pdfDate(d = new Date()) {
  const p = (n) => String(n).padStart(2, '0');
  return `D:${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}${p(d.getHours())}${p(d.getMinutes())}${p(d.getSeconds())}`;
}

/**
 * buildCardPdf(frontCanvas, backCanvas, title) -> Blob
 */
async function buildCardPdf(frontCanvas, backCanvas, title) {
  const enc = new TextEncoder();
  const W = (CONFIG.card.widthMm * MM_TO_PT).toFixed(4);
  const H = (CONFIG.card.heightMm * MM_TO_PT).toFixed(4);
  const images = [await encodeCanvas(frontCanvas), await encodeCanvas(backCanvas)];

  const parts = [];
  let length = 0;
  const offsets = [];
  const push = (chunk) => {
    const bytes = typeof chunk === 'string' ? enc.encode(chunk) : chunk;
    parts.push(bytes);
    length += bytes.length;
  };
  const object = (num, body, stream) => {
    offsets[num] = length;
    push(`${num} 0 obj\n${body}\n`);
    if (stream) { push('stream\n'); push(stream); push('\nendstream\n'); }
    push('endobj\n');
  };

  // object numbers
  //   1 catalog, 2 pages, 3 info
  //   per page i (0,1): 4+3i page, 5+3i image, 6+3i content
  push('%PDF-1.4\n%âãÏÓ\n');
  object(1, '<< /Type /Catalog /Pages 2 0 R >>');
  object(2, `<< /Type /Pages /Kids [4 0 R 7 0 R] /Count 2 >>`);
  object(3, `<< /Title ${pdfText(title)} /Creator (Alpha Premier ID Maker) /Producer (Alpha Premier ID Maker) /CreationDate (${pdfDate()}) >>`);

  images.forEach((im, i) => {
    const page = 4 + 3 * i, image = 5 + 3 * i, content = 6 + 3 * i;
    object(page, `<< /Type /Page /Parent 2 0 R /MediaBox [0 0 ${W} ${H}] /Resources << /XObject << /Im1 ${image} 0 R >> >> /Contents ${content} 0 R >>`);
    object(image, `<< /Type /XObject /Subtype /Image /Width ${im.width} /Height ${im.height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter ${im.filter} /Length ${im.bytes.length} >>`, im.bytes);
    const draw = enc.encode(`q ${W} 0 0 ${H} 0 0 cm /Im1 Do Q`);
    object(content, `<< /Length ${draw.length} >>`, draw);
  });

  const count = 10;
  const xref = length;
  let table = `xref\n0 ${count}\n0000000000 65535 f \n`;
  for (let n = 1; n < count; n++) table += `${String(offsets[n]).padStart(10, '0')} 00000 n \n`;
  push(table);
  push(`trailer\n<< /Size ${count} /Root 1 0 R /Info 3 0 R >>\nstartxref\n${xref}\n%%EOF\n`);

  return new Blob(parts, { type: 'application/pdf' });
}

/* ---- file naming ------------------------------------------------- */

const NAME_SUFFIX = /^(jr|sr|ii|iii|iv|v)\.?$/i;

// "Christopher Joseph V. Aureo" -> "Aureo"; "Juan dela Cruz Jr." -> "Cruz"
function lastNameForFile(fullName) {
  const parts = collapseSpaces(fullName).split(' ').filter(Boolean);
  while (parts.length > 1 && NAME_SUFFIX.test(parts[parts.length - 1])) parts.pop();
  const last = parts[parts.length - 1] ?? '';
  return (last.split('-').pop() ?? last).normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[^A-Za-z]/g, '');
}

// "APG-2026-122_Aureo.pdf"
function pdfFileName(year, seq, fullName) {
  const last = lastNameForFile(fullName);
  return `${employeeId(year, seq)}${last ? `_${last}` : ''}.pdf`;
}

function downloadBlob(fileName, blob) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url; a.download = fileName;
  document.body.appendChild(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 2000);
}
