/* ------------------------------------------------------------------
   App: form state, validation, live preview, review dialog
   ------------------------------------------------------------------ */

const $ = (id) => document.getElementById(id);
const YEAR = new Date().getFullYear();
const NAME_RE = /^[\p{L}\p{M} .,'’-]+$/u;
const ROTATE_KEY = 'apg-id-rotate-back';
const FIELD_LABEL = { name: 'name', title: 'job title', seq: 'employee number', photo: 'photo', signature: 'signature' };
const FIELD_INPUT = { name: 'f-name', title: 'f-title', seq: 'f-seq', photo: 'f-photo', signature: 'f-signature' };

const state = {
  templates: null,
  loadError: '',
  side: 'front',
  rotateBack: readRotate(),
  name: '',
  title: '',
  seq: '',
  photo: null,          // result of processPhotoFile when check.ok
  photoError: '',
  signature: null,
  touched: { name: false, title: false, seq: false, photo: false, signature: false },
  example: false,
  checking: false,
};

function readRotate() { try { return localStorage.getItem(ROTATE_KEY) === '1'; } catch { return false; } }
function writeRotate(on) { try { localStorage.setItem(ROTATE_KEY, on ? '1' : '0'); } catch { /* private mode */ } }

/* ---- derived data ------------------------------------------------ */

function seqNumber() { return /^\d{1,4}$/.test(state.seq) && Number(state.seq) > 0 ? Number(state.seq) : null; }

function cardData() {
  return {
    name: state.name,
    title: state.title,
    year: YEAR,
    seq: seqNumber(),
    photo: state.photo ? { image: state.photo.image, placement: state.photo.check.placement } : null,
    signature: state.signature,
  };
}

function problems() {
  const data = cardData();
  const layout = state.templates ? layoutText(data) : null;
  const name = collapseSpaces(state.name), title = collapseSpaces(state.title);
  return {
    name: !name ? 'Enter the full name as it should appear on the card.'
        : !NAME_RE.test(name) ? 'Use letters, spaces, periods, commas, hyphens and apostrophes only.'
        : layout && !layout.name.fits ? 'Too long for the card, even at the smallest size. Try a middle initial or a shorter form.'
        : '',
    title: !title ? 'Enter the job title.'
         : layout && !layout.title.fits ? 'Too long for the card. Try a shorter form, such as “Sr. HR Business Partner”.'
         : '',
    seq: !state.seq ? 'Enter the employee number HR assigned, such as 124.'
       : seqNumber() === null ? 'Use 1 to 4 digits, such as 124.'
       : '',
    photo: state.photoError || (state.photo ? '' : 'Upload the new hire’s photo.'),
    signature: hasInk(state.signature) ? '' : 'Ask the new hire to sign in the box.',
  };
}

/* ---- rendering --------------------------------------------------- */

const previewCanvas = $('card-canvas');
const previewBox = $('card-box');

function drawPreview() {
  if (!state.templates) return;
  const cssW = previewBox.clientWidth;
  if (!cssW) return;
  const pxPerMm = (cssW * (window.devicePixelRatio || 1)) / CONFIG.card.widthMm;
  const ctx = previewCanvas.getContext('2d');
  if (state.side === 'front') drawFront(ctx, state.templates, pxPerMm, cardData());
  else drawBack(ctx, state.templates, pxPerMm, state.rotateBack);
  previewCanvas.setAttribute('aria-label', state.side === 'front'
    ? `Front of the ID card for ${state.name || 'a new hire'}` : 'Back of the ID card');
}

function setMsg(fieldId, msgId, error, help) {
  $(fieldId).classList.toggle('has-error', !!error);
  $(msgId).textContent = error || help;
  const input = $(fieldId).querySelector('input');
  if (input) input.setAttribute('aria-invalid', error ? 'true' : 'false');
}

function render() {
  const p = problems();
  const shown = (k) => (state.touched[k] ? p[k] : '');

  // side tabs + caption
  $('side-front').classList.toggle('on', state.side === 'front');
  $('side-back').classList.toggle('on', state.side === 'back');
  $('side-front').setAttribute('aria-selected', state.side === 'front');
  $('side-back').setAttribute('aria-selected', state.side === 'back');
  $('caption').textContent = state.side === 'back'
    ? 'The back is the same on every card.'
    : 'Updates as you type. The print file is drawn the same way at 300 DPI.';

  // template loading
  $('card-box').hidden = !state.templates;
  $('card-loading').hidden = !!state.templates;
  $('card-loading').textContent = state.loadError || 'Loading the card template…';

  // example tag
  $('btn-example').hidden = state.example;
  $('example-tag').hidden = !state.example;

  // text fields
  setMsg('field-name', 'f-name-msg', shown('name'), 'Exactly as it should appear; capitals are kept as typed.');
  setMsg('field-title', 'f-title-msg', shown('title'), 'Shown in capitals on the card.');
  setMsg('field-seq', 'f-seq-msg', shown('seq'),
    `The number HR assigned. The year (${YEAR}) comes from this device’s date. Check it carefully: nothing else catches a duplicate.`);
  $('seq-prefix').textContent = `${CONFIG.employeeNo.prefix} - ${YEAR} -`;

  // photo
  const photoErr = state.touched.photo || state.photoError ? state.photoError || shown('photo') : '';
  const ph = state.photo;
  const pm = photoErr ? { kind: 'error', text: photoErr }
    : ph?.check.warning ? { kind: 'warn', text: ph.check.warning }
    : ph ? { kind: 'ok', text: `${ph.background === 'green' ? 'Green background removed · ' : ph.background === 'blue' ? 'Blue background removed · ' : ''}Placed automatically` }
    : { kind: 'hint', text: 'A cut-out PNG, or a photo with a solid green background. Chest-up, facing forward.' };
  const photoMsg = $('photo-msg');
  photoMsg.className = `msg msg-${pm.kind}`;
  photoMsg.textContent = pm.text;
  if (pm.kind === 'error') photoMsg.setAttribute('role', 'alert'); else photoMsg.removeAttribute('role');
  $('photo-btn').textContent = state.checking ? 'Checking photo…' : ph ? 'Change photo' : 'Upload photo';
  $('photo-name').hidden = !ph;
  $('photo-name').textContent = ph ? ph.fileName : '';
  const thumb = $('photo-thumb');
  if (ph) {
    if (!thumb.querySelector('img') || thumb.querySelector('img').src !== ph.url) {
      thumb.innerHTML = '';
      const img = document.createElement('img'); img.src = ph.url; img.alt = ''; thumb.appendChild(img);
    }
  } else if (!thumb.querySelector('span')) {
    thumb.innerHTML = '<span>PNG</span>';
  }

  // signature
  $('sig').classList.toggle('sig-invalid', !!shown('signature'));
  $('sig-hint').hidden = hasInk(state.signature);
  $('btn-clear-sig').disabled = !hasInk(state.signature);
  $('sig-msg').hidden = !shown('signature');
  $('sig-msg').textContent = shown('signature');

  // number line + status
  $('card-line').textContent = employeeNoLine(YEAR, seqNumber());
  const missing = Object.keys(p).filter((k) => p[k]);
  const complete = !!state.templates && missing.length === 0;
  $('btn-review').disabled = !state.templates;
  $('status').textContent = complete
    ? 'Everything is filled in. Check the card, then download the print file.'
    : `Still needed: ${missing.map((k) => FIELD_LABEL[k]).join(', ')}.`;

  $('opt-rotate').checked = state.rotateBack;
  drawPreview();
}

/* ---- events ------------------------------------------------------ */

function touch(field) { state.touched[field] = true; }

$('f-name').addEventListener('input', (e) => { state.example = false; touch('name'); state.name = e.target.value; render(); });
$('f-title').addEventListener('input', (e) => { state.example = false; touch('title'); state.title = e.target.value; render(); });
$('f-seq').addEventListener('input', (e) => {
  state.example = false; touch('seq');
  state.seq = e.target.value.replace(/\D/g, '').slice(0, 4);
  e.target.value = state.seq;
  render();
});
for (const k of ['name', 'title', 'seq']) $(`f-${k}`).addEventListener('blur', () => { touch(k); render(); });

$('side-front').addEventListener('click', () => { state.side = 'front'; render(); });
$('side-back').addEventListener('click', () => { state.side = 'back'; render(); });
$('opt-rotate').addEventListener('change', (e) => { state.rotateBack = e.target.checked; writeRotate(state.rotateBack); render(); });

// photo upload (button + drag & drop)
async function acceptPhoto(file) {
  if (!file) return;
  state.checking = true; render();
  const result = await processPhotoFile(file);
  state.checking = false;
  state.example = false;
  touch('photo');
  if ('error' in result) { state.photo = null; state.photoError = result.error; }
  else if (result.check.ok) { state.photo = result; state.photoError = ''; }
  else { state.photo = null; state.photoError = result.check.problem ?? 'This photo can’t be used.'; }
  $('f-photo').value = '';
  state.side = 'front';
  render();
}
$('f-photo').addEventListener('change', (e) => acceptPhoto(e.target.files?.[0]));
const photoZone = $('photo');
photoZone.addEventListener('dragover', (e) => { e.preventDefault(); photoZone.classList.add('dragging'); });
photoZone.addEventListener('dragleave', () => photoZone.classList.remove('dragging'));
photoZone.addEventListener('drop', (e) => { e.preventDefault(); photoZone.classList.remove('dragging'); acceptPhoto(e.dataTransfer.files[0]); });

// photo prompt guide
$('photo-prompt').value = PHOTO_PROMPT;
$('btn-guide').addEventListener('click', () => {
  const open = $('guide-body').hidden;
  $('guide-body').hidden = !open;
  $('guide-teaser').hidden = open;
  $('guide-sign').textContent = open ? '−' : '+';
  $('btn-guide').setAttribute('aria-expanded', String(open));
});
$('btn-copy-prompt').addEventListener('click', async () => {
  try { await navigator.clipboard.writeText(PHOTO_PROMPT); $('copy-status').textContent = 'Copied.'; }
  catch { $('photo-prompt').focus(); $('photo-prompt').select(); $('copy-status').textContent = 'Selected. Press Ctrl+C or ⌘C to copy.'; }
});

// signature pad
const pad = createSignaturePad($('f-signature'), (sig) => {
  state.example = false; touch('signature'); state.signature = sig; state.side = 'front'; render();
});
$('btn-clear-sig').addEventListener('click', () => pad.clear());

// example
$('btn-example').addEventListener('click', async () => {
  const photo = ASSETS.example ? await processPhotoUrl(ASSETS.example).catch(() => null) : null;
  state.name = EXAMPLE.name; state.title = EXAMPLE.title; state.seq = EXAMPLE.seq;
  $('f-name').value = state.name; $('f-title').value = state.title; $('f-seq').value = state.seq;
  state.photo = photo?.check.ok ? photo : null; state.photoError = '';
  state.signature = EXAMPLE.signature;
  pad.setValue(EXAMPLE.signature);
  state.touched = { name: false, title: false, seq: false, photo: false, signature: false };
  state.example = true; state.side = 'front';
  render();
});

// review & generate
$('btn-review').addEventListener('click', () => {
  const p = problems();
  const missing = Object.keys(p).filter((k) => p[k]);
  if (missing.length) {
    for (const k of Object.keys(state.touched)) state.touched[k] = true;
    render();
    $(FIELD_INPUT[missing[0]])?.focus();
    return;
  }
  state.side = 'front';
  render();
  openReview();
});

/* ---- review dialog ----------------------------------------------- */

const review = { front: null, back: null, fileName: '', title: '', saving: false };

function openReview() {
  const data = cardData();
  review.front = renderSideCanvas(state.templates, 'front', data, state.rotateBack);
  review.back = renderSideCanvas(state.templates, 'back', data, state.rotateBack);
  review.fileName = pdfFileName(YEAR, data.seq, data.name);
  review.title = `${employeeId(YEAR, data.seq)} ${collapseSpaces(data.name)}`;

  $('review-title').textContent = 'Check the card before printing';
  $('review-sub').textContent = `${employeeId(YEAR, data.seq)} · ${collapseSpaces(data.name)}`;
  $('review-front').src = review.front.toDataURL('image/png');
  $('review-front').alt = `Front of the card for ${collapseSpaces(data.name)}`;
  $('review-back').src = review.back.toDataURL('image/png');
  $('review-back-cap').textContent = `Back${state.rotateBack ? ' (turned 180°)' : ''}`;
  $('review-file').textContent = `File: ${review.fileName} · 2 pages, ${CONFIG.card.widthMm} × ${CONFIG.card.heightMm} mm, ${CONFIG.card.printDpi} DPI`;
  $('review-error').hidden = true;
  $('review-stage').hidden = false;
  $('saved-stage').hidden = true;
  $('btn-download').disabled = false;
  $('btn-download').textContent = 'Download PDF';
  $('overlay').hidden = false;
  $('btn-download').focus();
}

function closeReview() { if (!review.saving) $('overlay').hidden = true; }

async function downloadPdf() {
  review.saving = true;
  $('btn-download').disabled = true; $('btn-download').textContent = 'Preparing PDF…'; $('btn-back-edit').disabled = true;
  $('review-error').hidden = true;
  try {
    const blob = await buildCardPdf(review.front, review.back, review.title);
    downloadBlob(review.fileName, blob);
    $('review-title').textContent = 'ID ready to print';
    $('saved-name').textContent = review.fileName;
    $('review-stage').hidden = true;
    $('saved-stage').hidden = false;
    $('btn-new-id').focus();
  } catch (err) {
    console.error(err);
    $('review-error').textContent = 'The file couldn’t be saved here. Try again, or open the app in a regular browser tab.';
    $('review-error').hidden = false;
  } finally {
    review.saving = false;
    $('btn-download').disabled = false; $('btn-download').textContent = 'Download PDF'; $('btn-back-edit').disabled = false;
  }
}

function newId() {
  state.name = ''; state.title = ''; state.seq = '';
  $('f-name').value = ''; $('f-title').value = ''; $('f-seq').value = '';
  state.photo = null; state.photoError = ''; state.signature = null;
  pad.setValue(null);
  state.touched = { name: false, title: false, seq: false, photo: false, signature: false };
  state.example = false; state.side = 'front';
  $('overlay').hidden = true;
  render();
  window.scrollTo({ top: 0, behavior: 'smooth' });
}

$('btn-download').addEventListener('click', downloadPdf);
$('btn-again').addEventListener('click', downloadPdf);
$('btn-back-edit').addEventListener('click', closeReview);
$('btn-new-id').addEventListener('click', newId);
$('overlay').addEventListener('click', (e) => { if (e.target === e.currentTarget) closeReview(); });
window.addEventListener('keydown', (e) => { if (e.key === 'Escape' && !$('overlay').hidden) closeReview(); });

/* ---- boot -------------------------------------------------------- */

new ResizeObserver(drawPreview).observe(previewBox);

Promise.all([loadTemplates(), loadCardFonts()])
  .then(([templates]) => { state.templates = templates; render(); })
  .catch((err) => { state.loadError = `${err.message} Reload the page to try again.`; render(); });

render();
