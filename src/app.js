/* ------------------------------------------------------------------
   App: form state, validation, live preview, review dialog
   ------------------------------------------------------------------ */

const $ = (id) => document.getElementById(id);
const THIS_YEAR = new Date().getFullYear();
const NAME_RE = /^[\p{L}\p{M} .,'’-]+$/u;
const SUFFIX_RE = /^[\p{L}\p{M}. \d]+$/u;
const NAME_PARTS = ['last', 'first', 'middle', 'suffix'];
const CUSTOM_TITLE = '__custom__';          // the "Other…" option's value
const TITLE_RE = /^[\p{L}\p{M}\d .,'’&()\/-]+$/u;
const ROTATE_KEY = 'apg-id-rotate-back';
const FLIP_KEY = 'apg-id-duplex-flip';
const DRAFT_KEY = 'apg-id-draft';
const FIELD_LABEL = {
  last: 'last name', first: 'first name', middle: 'middle name', suffix: 'suffix',
  nameFit: 'a shorter name', title: 'job title', title2: 'second role',
  titleFit: 'a shorter job title', seq: 'employee number',
  photo: 'photo', signature: 'signature',
};
const FIELD_INPUT = {
  last: 'f-last', first: 'f-first', middle: 'f-middle', suffix: 'f-suffix',
  nameFit: 'f-first', title: 'f-title-custom', title2: 'f-title2-custom', titleFit: 'f-title-custom',
  seq: 'f-seq', photo: 'f-photo', signature: 'f-signature',
};

// The wizard. `fields` are the keys of problems() that belong to each step,
// so a step is "done" exactly when none of its fields has a problem.
const STEPS = [
  { id: 'name',   label: 'Name',      fields: ['last', 'first', 'middle', 'suffix', 'nameFit'] },
  { id: 'title',  label: 'Job title', fields: ['title', 'title2', 'titleFit'] },
  { id: 'number', label: 'Number',    fields: ['seq'] },
  { id: 'photo',  label: 'Photo',     fields: ['photo'] },
  { id: 'sign',   label: 'Signature', fields: ['signature'] },
  { id: 'check',  label: 'Check',     fields: [] },
];
const LAST_STEP = STEPS.length - 1;

const state = {
  templates: null,
  loadError: '',
  side: 'front',
  rotateBack: readRotate(),
  flip: readFlip(),                     // 'long' | 'short': how the printer turns the paper
  year: THIS_YEAR,
  last: '',
  first: '',
  middle: '',
  suffix: '',
  role1: { pick: '', custom: '' },      // pick is a title, or CUSTOM_TITLE
  role2: { pick: '', custom: '' },
  showRole2: false,
  seq: '',
  photo: null,          // result of processPhotoFile when check.ok
  photoError: '',
  adjust: { dx: 0, dy: 0, zoom: 1 },   // manual nudge on top of the automatic placement
  guides: false,        // maintainer overlay (Ctrl+Shift+G or #guides)
  signature: null,
  touched: { last: false, first: false, middle: false, suffix: false, title: false, title2: false, seq: false, photo: false, signature: false },
  step: 0,
  // Cards always print front-and-back side by side, to be cut out and stuck
  // together. The batch panel still offers the other arrangements.
  print: { layout: 'duplex', paper: CONFIG.sheet.defaultPaper, copies: null, page: 0 },
  queue: [],            // cards already finished, waiting to print with this one
  example: false,
  checking: false,
  reissue: false,       // "Replacement card" ticked
  dupLocal: null,       // register entry for the current number on this computer
  dupShared: null,      // ... in the shared register (null, entry, or undefined = unreachable)
};

/* ---- draft: survive an accidental reload (text only, this tab only) ---- */
function saveDraft() {
  try {
    const d = {
      ...nameParts(), seq: state.seq, year: state.year,
      role1: state.role1, role2: state.role2, showRole2: state.showRole2,
    };
    if (!d.last && !d.first && !d.seq && !roleValue(d.role1)) sessionStorage.removeItem(DRAFT_KEY);
    else sessionStorage.setItem(DRAFT_KEY, JSON.stringify(d));
  } catch { /* storage blocked */ }
}
function clearDraft() { try { sessionStorage.removeItem(DRAFT_KEY); } catch { /* ignore */ } }
function restoreDraft() {
  try {
    const d = JSON.parse(sessionStorage.getItem(DRAFT_KEY) || 'null');
    if (!d || (!d.last && !d.first && !d.seq && !d.role1?.pick)) return false;
    for (const k of NAME_PARTS) { state[k] = d[k] || ''; $(`f-${k}`).value = state[k]; }
    state.seq = d.seq || '';
    state.year = Number(d.year) || THIS_YEAR;
    state.role1 = { pick: d.role1?.pick || '', custom: d.role1?.custom || '' };
    state.role2 = { pick: d.role2?.pick || '', custom: d.role2?.custom || '' };
    state.showRole2 = !!d.showRole2;
    $('f-title-custom').value = state.role1.custom;
    $('f-title2-custom').value = state.role2.custom;
    
    $('f-seq').value = state.seq;
    refreshDuplicate();
    return true;
  } catch { return false; }
}

function readRotate() { try { return localStorage.getItem(ROTATE_KEY) === '1'; } catch { return false; } }
function writeRotate(on) { try { localStorage.setItem(ROTATE_KEY, on ? '1' : '0'); } catch { /* private mode */ } }
function readFlip() { try { return localStorage.getItem(FLIP_KEY) === 'short' ? 'short' : 'long'; } catch { return 'long'; } }
function writeFlip(v) { try { localStorage.setItem(FLIP_KEY, v); } catch { /* private mode */ } }

/* ---- derived data ------------------------------------------------ */

function seqNumber() { return /^\d{1,4}$/.test(state.seq) && Number(state.seq) > 0 ? Number(state.seq) : null; }

const nameParts = () => ({ last: state.last, first: state.first, middle: state.middle, suffix: state.suffix });
const roleValue = (r) => (r.pick === CUSTOM_TITLE ? collapseSpaces(r.custom) : r.pick);
const sameRole = (a, b) => !!a && !!b && a.toLocaleLowerCase('en-US') === b.toLocaleLowerCase('en-US');
const role1 = () => roleValue(state.role1);
const role2 = () => (state.showRole2 ? roleValue(state.role2) : '');
const jobTitle = () => composeTitle(role1(), role2());
const printedName = () => composeName(nameParts());

function cardData() {
  return {
    name: printedName(),
    title: jobTitle(),
    year: state.year,
    seq: seqNumber(),
    photo: state.photo ? { image: state.photo.image, placement: adjustedPlacement(state.photo.check.placement, state.adjust) } : null,
    signature: state.signature,
  };
}

function problems() {
  const data = cardData();
  const layout = state.templates ? layoutText(data) : null;
  const title = jobTitle();
  const badChars = 'Use letters, spaces, periods, hyphens and apostrophes only.';
  const last = collapseSpaces(state.last), first = collapseSpaces(state.first);
  const middle = collapseSpaces(state.middle), suffix = collapseSpaces(state.suffix);
  return {
    last: !last ? 'Enter the last name.' : !NAME_RE.test(last) ? badChars : '',
    first: !first ? 'Enter the first name.' : !NAME_RE.test(first) ? badChars : '',
    middle: middle && !NAME_RE.test(middle) ? badChars : '',
    suffix: suffix && !SUFFIX_RE.test(suffix) ? 'Use something like Jr., Sr., II or III.' : '',
    nameFit: last && first && layout && !layout.name.fits
      ? 'This name is too long for the card, even at the smallest size. Try a shorter first name.' : '',
    title: !state.role1.pick ? 'Choose the job title.'
         : state.role1.pick === CUSTOM_TITLE && !collapseSpaces(state.role1.custom) ? 'Type the job title.'
         : state.role1.pick === CUSTOM_TITLE && !TITLE_RE.test(collapseSpaces(state.role1.custom)) ? 'Use letters, numbers, spaces and . , & ( ) / - only.'
         : '',
    title2: state.showRole2 && sameRole(roleValue(state.role2), roleValue(state.role1))
            ? 'This is the same as the job title above. Pick a different role, or remove it.'
          : state.showRole2 && state.role2.pick === CUSTOM_TITLE && !collapseSpaces(state.role2.custom) ? 'Type the second role, or remove it.'
          : state.showRole2 && state.role2.pick === CUSTOM_TITLE && !TITLE_RE.test(collapseSpaces(state.role2.custom)) ? 'Use letters, numbers, spaces and . , & ( ) / - only.'
          : state.showRole2 && !state.role2.pick ? 'Choose the second role, or remove it.'
          : '',
    titleFit: title && layout && !layout.title.fits
      ? 'This job title is too long for the card, even at the smallest size. Try a shorter wording.' : '',
    seq: !state.seq ? 'Enter the employee number HR assigned, such as 124.'
       : seqNumber() === null ? 'Use 1 to 4 digits, such as 124.'
       : duplicate() && !state.reissue ? duplicateMessage()
       : '',
    photo: state.photoError || (state.photo ? '' : 'Upload the new hire’s photo.'),
    signature: hasInk(state.signature) ? '' : 'Ask the new hire to sign in the box.',
  };
}

// The register entry that already holds this number: one waiting in this
// print job, or one already issued (shared wins over local).
function queuedDuplicate() {
  const seq = seqNumber();
  if (seq === null) return null;
  const id = employeeId(state.year, seq);
  const hit = state.queue.find((q) => q.id === id);
  return hit ? { ...hit.entry, queued: true } : null;
}
function duplicate() { return queuedDuplicate() || state.dupShared || state.dupLocal || null; }
function duplicateMessage() {
  const d = duplicate();
  if (d.queued) return `${d.id} is already waiting in this print, for ${d.name}. Use a different number.`;
  const where = d === state.dupShared ? 'in the shared register' : 'on this computer';
  return `${d.id} was already issued to ${d.name} on ${formatIssuedAt(d.issuedAt)} (${where}). Check the number with HR, or tick “Replacement card” below.`;
}

let dupToken = 0;
function refreshDuplicate() {
  state.dupLocal = Register.find(state.year, seqNumber());
  state.dupShared = null;
  if (!Register.shared || seqNumber() === null) return;
  const token = ++dupToken;
  Register.findShared(state.year, seqNumber()).then((hit) => {
    if (token !== dupToken) return;          // the number changed meanwhile
    state.dupShared = hit;
    render();
  });
}

/* ---- steps ------------------------------------------------------- */

const stepProblems = (i, p = problems()) => STEPS[i].fields.filter((f) => p[f]);
const stepDone = (i, p) => stepProblems(i, p).length === 0;

// A step can be opened once every step before it is filled in.
const stepReachable = (i, p) => STEPS.slice(0, i).every((_, j) => stepDone(j, p));

function goStep(i, { focus = true } = {}) {
  state.step = Math.max(0, Math.min(LAST_STEP, i));
  if (state.step === LAST_STEP) buildReview();
  render();
  if (state.step !== LAST_STEP) state.side = 'front';
  const section = $(`step-${STEPS[state.step].id}`);
  section.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  if (focus) {
    const first = section.querySelector('input:not([type=hidden]):not([hidden]), select, button:not([hidden])');
    if (first && state.step !== LAST_STEP) first.focus({ preventScroll: true });
  }
}

/** Move on, or show what this step is still missing. */
function nextStep() {
  const p = problems();
  const missing = stepProblems(state.step, p);
  if (missing.length) {
    for (const f of STEPS[state.step].fields) if (f in state.touched) state.touched[f] = true;
    state.touched.last = state.touched.last || STEPS[state.step].fields.includes('nameFit');
    render();
    const target = FIELD_INPUT[missing[0]];
    const el = target && $(target);
    if (el && !el.hidden) el.focus();
    return;
  }
  if (state.step < LAST_STEP) goStep(state.step + 1);
}

function renderStepBar(p) {
  const bar = $('steps');
  if (!bar.children.length) {
    STEPS.forEach((s, i) => {
      const li = document.createElement('li');
      const b = document.createElement('button');
      b.type = 'button';
      b.className = 'step-chip';
      b.dataset.step = String(i);
      const num = document.createElement('span');
      num.className = 'num';
      num.textContent = String(i + 1);
      b.append(num, document.createTextNode(s.label));
      b.addEventListener('click', () => {
        const probs = problems();
        if (i <= state.step || stepReachable(i, probs)) goStep(i);
        else {
          const firstBad = STEPS.findIndex((_, j) => !stepDone(j, probs));
          goStep(firstBad < 0 ? i : firstBad);
        }
      });
      li.append(b);
      bar.append(li);
    });
  }
  [...bar.querySelectorAll('.step-chip')].forEach((b, i) => {
    const done = stepDone(i, p) && i !== LAST_STEP;
    b.classList.toggle('is-current', i === state.step);
    b.classList.toggle('is-done', done && i !== state.step);
    b.disabled = i > state.step && !stepReachable(i, p);
    b.setAttribute('aria-current', i === state.step ? 'step' : 'false');
  });
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
  if (state.side === 'front') drawFront(ctx, state.templates, pxPerMm, cardData(), { showGuides: state.guides });
  else drawBack(ctx, state.templates, pxPerMm, state.rotateBack);
  previewCanvas.setAttribute('aria-label', state.side === 'front'
    ? `Front of the ID card for ${printedName() || 'a new hire'}` : 'Back of the ID card');
}

function setMsg(fieldId, msgId, error, help) {
  $(fieldId).classList.toggle('has-error', !!error);
  $(msgId).textContent = error || help;
  const input = $(fieldId).querySelector('input:not([type="radio"]):not([type="checkbox"])');
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
  // the stage shows the card while it is being made, the sheet at the end
  const onCheckStep = state.step === LAST_STEP;
  const duplex = state.print.layout === 'duplex';
  $('caption').textContent = onCheckStep
    ? (duplex
      ? 'These are the two pages that will print: the fronts, then the backs reversed to land behind them. Cut along the marks.'
      : 'This is the sheet that will print. Cut along the marks and stick each pair back to back.')
    : state.side === 'back'
      ? 'The back is the same on every card.'
      : 'Updates as you type. The print file is drawn the same way at 300 DPI.';
  document.querySelector('.segmented').hidden = onCheckStep;
  $('card-box').hidden = !state.templates || onCheckStep;
  $('card-loading').hidden = !!state.templates;
  if (!onCheckStep) $('sheet-box').hidden = true;      // the sheet belongs to the last step
  renderJob();
  $('card-loading').textContent = state.loadError || 'Loading the card template…';

  // example tag
  $('btn-example').hidden = state.example;
  $('example-tag').hidden = !state.example;

  // name fields
  for (const k of NAME_PARTS) setMsg(`field-${k}`, `f-${k}-msg`, shown(k), '');
  const preview = $('name-preview');
  const fitError = state.touched.last && state.touched.first ? p.nameFit : '';
  preview.classList.toggle('is-error', !!fitError);
  if (fitError) {
    preview.textContent = fitError;
    preview.setAttribute('role', 'alert');
  } else {
    preview.removeAttribute('role');
    const shownName = printedName();
    preview.innerHTML = '';
    preview.append('On the card: ');
    const strong = document.createElement('span');
    strong.className = 'as-printed';
    strong.textContent = shownName || '—';
    preview.append(strong);
  }

  // job title
  setMsg('field-title', 'f-title-msg', shown('title'), '');
  setMsg('field-title2', 'f-title2-msg', shown('title2'), '');
  syncChips($('title-chips'), state.role1.pick, null);
  $('f-title-custom').hidden = state.role1.pick !== CUSTOM_TITLE;
  $('field-title2').hidden = !state.showRole2;
  $('btn-role-add').hidden = state.showRole2;
  if (state.showRole2) {
    // whatever the main job title resolves to disappears from this row
    syncChips($('title2-chips'), state.role2.pick, roleValue(state.role1));
    $('f-title2-custom').hidden = state.role2.pick !== CUSTOM_TITLE;
  }
  const titlePreview = $('title-preview');
  const titleFit = state.touched.title ? p.titleFit : '';
  titlePreview.classList.toggle('is-error', !!titleFit);
  titlePreview.textContent = '';
  if (titleFit) {
    titlePreview.textContent = titleFit;
    titlePreview.setAttribute('role', 'alert');
  } else {
    titlePreview.removeAttribute('role');
    const shownTitle = CONFIG.text.title.uppercase ? jobTitle().toLocaleUpperCase('en-US') : jobTitle();
    titlePreview.append('On the card: ');
    const strong = document.createElement('span');
    strong.className = 'as-printed';
    strong.textContent = shownTitle || '—';
    titlePreview.append(strong);
  }
  const dup = duplicate();
  const seqHelp = Register.shared
    ? `The number HR assigned. Checked against this computer and the shared register.${state.dupShared === undefined ? ' (Shared register not reachable right now — only this computer was checked.)' : ''}`
    : 'The number HR assigned. Checked against the IDs issued on this computer only.';
  setMsg('field-seq', 'f-seq-msg', shown('seq'), dup && state.reissue
    ? `Replacement for ${dup.id} (${dup.name}, ${formatIssuedAt(dup.issuedAt)}).` : seqHelp);
  $('seq-prefix').textContent = `${CONFIG.employeeNo.prefix} -`;
  $('reissue-wrap').hidden = !dup;
  $('opt-reissue').checked = state.reissue;
  $('f-year').value = String(state.year);

  // photo
  const photoErr = state.touched.photo || state.photoError ? state.photoError || shown('photo') : '';
  const ph = state.photo;
  const pm = photoErr ? { kind: 'error', text: photoErr }
    : ph?.check.warnings?.length ? { kind: 'warn', text: ph.check.warnings.join(' ') }
    : ph ? { kind: 'ok', text: `${ph.background === 'green' ? 'Green background removed · ' : ph.background === 'blue' ? 'Blue background removed · ' : ''}Placed automatically` }
    : { kind: 'hint', text: 'A cut-out PNG, or a photo with a solid green background. Chest-up, facing forward.' };
  const photoMsg = $('photo-msg');
  photoMsg.className = `msg msg-${pm.kind}`;
  photoMsg.textContent = pm.text;
  if (pm.kind === 'error') photoMsg.setAttribute('role', 'alert'); else photoMsg.removeAttribute('role');
  $('photo-btn').textContent = state.checking ? 'Checking photo…' : ph ? 'Change photo' : 'Upload photo';
  $('photo-name').hidden = !ph;
  $('photo-name').textContent = ph ? ph.fileName : '';
  const adjusted = state.adjust.dx !== 0 || state.adjust.dy !== 0 || state.adjust.zoom !== 1;
  $('adjust').hidden = !ph;
  $('btn-adjust-reset').disabled = !adjusted;
  $('f-zoom').value = String(Math.round(state.adjust.zoom * 100));
  $('zoom-out').textContent = `${Math.round(state.adjust.zoom * 100)} %`;
  previewCanvas.classList.toggle('draggable', !!ph && state.side === 'front');
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
  const sigImage = state.signature?.kind === 'image';
  $('sig').classList.toggle('sig-invalid', !!shown('signature'));
  $('sig-hint').hidden = hasInk(state.signature);
  $('f-signature').hidden = sigImage;
  $('sig-image').hidden = !sigImage;
  if (sigImage && $('sig-image').dataset.for !== state.signature.fileName) {
    $('sig-image').src = state.signature.mask.toDataURL('image/png');
    $('sig-image').dataset.for = state.signature.fileName ?? 'scan';
  }
  $('sig-help').textContent = sigImage
    ? `Scanned: ${state.signature.fileName ?? 'signature'}. Clear to sign on screen instead.`
    : 'The new hire signs here; it appears in white on the card.';
  $('btn-clear-sig').disabled = !hasInk(state.signature);
  $('sig-msg').hidden = !shown('signature');
  $('sig-msg').textContent = shown('signature');

  // number line + status
  $('card-line').textContent = employeeNoLine(state.year, seqNumber());
  const missingHere = stepProblems(state.step, p);
  const missingAll = Object.keys(p).filter((k) => p[k]);
  $('status').textContent = !state.templates ? 'Loading the card template…'
    : state.step === LAST_STEP ? (missingAll.length ? `Still needed: ${missingAll.map((k) => FIELD_LABEL[k]).join(', ')}.` : '')
    : missingHere.length ? `Still needed on this step: ${missingHere.map((k) => FIELD_LABEL[k]).join(', ')}.`
    : `Step ${state.step + 1} of ${STEPS.length} · ready for the next step.`;

  $('opt-rotate').checked = state.rotateBack;
  $('opt-flip').value = state.flip;

  // one step at a time
  STEPS.forEach((s, i) => { $(`step-${s.id}`).hidden = i !== state.step; });
  renderStepBar(p);
  $('btn-back').hidden = state.step === 0;
  $('btn-next').hidden = state.step === LAST_STEP;
  $('btn-next').textContent = state.step === LAST_STEP - 1 ? 'Check the card' : 'Next';
  $('btn-next').disabled = !state.templates;

  renderRegister();
  drawPreview();
}

// Tick the chosen chip; `hideValue` removes one option from the row.
function syncChips(container, selected, hideValue) {
  for (const chip of container.querySelectorAll('.chip')) {
    const value = chip.dataset.value;
    const hidden = !!hideValue && value !== CUSTOM_TITLE && sameRole(value, hideValue);
    chip.hidden = hidden;
    const on = !hidden && value === selected;
    chip.classList.toggle('on', on);
    chip.querySelector('input').checked = on;
  }
}

function renderRegister() {
  const all = Register.all();
  $('register-count').textContent = all.length;
  $('btn-register-csv').disabled = !all.length;
  $('register-note').textContent = Register.shared
    ? 'Cards downloaded on this computer. The shared register holds every computer’s cards.'
    : all.length ? 'Cards downloaded on this computer. Other computers keep their own list — see register/README.md to share one.'
    : 'Nothing issued from this computer yet. Every downloaded card is listed here.';
  const list = $('register-list');
  list.innerHTML = '';
  for (const e of all.slice(0, 25)) {
    const li = document.createElement('li');
    const id = document.createElement('span'); id.className = 'id'; id.textContent = e.id + (e.replacement ? ' (replacement)' : '');
    const when = document.createElement('span'); when.className = 'when'; when.textContent = formatIssuedAt(e.issuedAt);
    const who = document.createElement('span'); who.className = 'who'; who.textContent = `${e.name} · ${e.title}`;
    li.append(id, when, who);
    list.appendChild(li);
  }
  if (all.length > 25) {
    const li = document.createElement('li'); li.className = 'msg'; li.textContent = `… and ${all.length - 25} more in the CSV.`; list.appendChild(li);
  }
}

/* ---- events ------------------------------------------------------ */

function touch(field) { state.touched[field] = true; }

for (const k of NAME_PARTS) {
  $(`f-${k}`).addEventListener('input', (e) => {
    state.example = false; touch(k); state[k] = e.target.value; saveDraft(); render();
  });
}
// Build both chip rows. Called once at boot, and again whenever the list
// of remembered titles changes — not on every keystroke, so focus and the
// keyboard's position inside the radio group survive.
function buildChips(container, groupName, onPick, options = null) {
  container.innerHTML = '';
  const add = (value, label, removable) => {
    const chip = document.createElement('label');
    chip.className = 'chip';
    chip.dataset.value = value;
    const radio = document.createElement('input');
    radio.type = 'radio'; radio.name = groupName; radio.value = value;
    radio.addEventListener('change', () => onPick(value));
    const text = document.createElement('span');
    text.textContent = label;
    chip.append(radio, text);
    if (removable) {
      const x = document.createElement('button');
      x.type = 'button'; x.className = 'chip-x'; x.textContent = '×';
      x.title = `Remove “${label}” from the list`;
      x.setAttribute('aria-label', `Remove ${label} from the list of job titles`);
      x.addEventListener('click', (e) => {
        e.preventDefault(); e.stopPropagation();
        Titles.forget(value);
        for (const r of [state.role1, state.role2]) if (r.pick === value) r.pick = '';
        refreshTitleOptions();
        saveDraft(); render();
      });
      chip.append(x);
    }
    container.append(chip);
  };
  if (options) { for (const o of options) add(o.value, o.label, false); return; }
  for (const t of Titles.all()) add(t, t, !Titles.isStandard(t));
  add(CUSTOM_TITLE, 'Other…', false);
}

function refreshTitleOptions() {
  buildChips($('title-chips'), 'role1', (value) => {
    state.example = false; touch('title');
    state.role1.pick = value;
    // the second role can never repeat the first
    if (state.showRole2 && sameRole(roleValue(state.role2), roleValue(state.role1))) state.role2.pick = '';
    saveDraft(); render();
    if (value === CUSTOM_TITLE) $('f-title-custom').focus();
  });
  buildChips($('title2-chips'), 'role2', (value) => {
    state.example = false; touch('title2');
    state.role2.pick = value;
    saveDraft(); render();
    if (value === CUSTOM_TITLE) $('f-title2-custom').focus();
  });
}
refreshTitleOptions();

$('f-title-custom').addEventListener('input', (e) => {
  state.example = false; touch('title'); state.role1.custom = e.target.value; saveDraft(); render();
});
$('f-title2-custom').addEventListener('input', (e) => {
  state.example = false; touch('title2'); state.role2.custom = e.target.value; saveDraft(); render();
});
$('btn-role-add').addEventListener('click', () => {
  state.showRole2 = true; state.example = false;
  saveDraft(); render();
  $('title2-chips').querySelector('.chip:not([hidden]) input')?.focus();
});
$('btn-role-remove').addEventListener('click', () => {
  state.showRole2 = false;
  state.role2 = { pick: '', custom: '' };
  state.touched.title2 = false;
  state.example = false;
  saveDraft(); render();
  $('btn-role-add').focus();
});
$('f-seq').addEventListener('input', (e) => {
  state.example = false; touch('seq');
  state.seq = e.target.value.replace(/\D/g, '').slice(0, 4);
  e.target.value = state.seq;
  state.reissue = false;
  refreshDuplicate();
  saveDraft();
  render();
});

// year selector: current-yearsBack … current+yearsAhead
for (let y = THIS_YEAR - CONFIG.employeeNo.yearsBack; y <= THIS_YEAR + CONFIG.employeeNo.yearsAhead; y++) {
  const o = document.createElement('option'); o.value = String(y); o.textContent = String(y); $('f-year').appendChild(o);
}
$('f-year').addEventListener('change', (e) => {
  state.year = Number(e.target.value); touch('seq'); state.reissue = false;
  refreshDuplicate(); saveDraft(); render();
});
$('opt-reissue').addEventListener('change', (e) => { state.reissue = e.target.checked; render(); });
$('btn-register-csv').addEventListener('click', () => {
  downloadBlob(`apg-id-register-${new Date().toISOString().slice(0, 10)}.csv`, new Blob([Register.csv()], { type: 'text/csv;charset=utf-8' }));
});
for (const k of [...NAME_PARTS, 'seq']) $(`f-${k}`).addEventListener('blur', () => { touch(k); render(); });
$('f-title-custom').addEventListener('blur', () => { touch('title'); render(); });
$('f-title2-custom').addEventListener('blur', () => { touch('title2'); render(); });

$('side-front').addEventListener('click', () => { state.side = 'front'; render(); });
$('side-back').addEventListener('click', () => { state.side = 'back'; render(); });
$('opt-rotate').addEventListener('change', (e) => { state.rotateBack = e.target.checked; writeRotate(state.rotateBack); render(); });
$('opt-flip').addEventListener('change', (e) => { state.flip = e.target.value === 'short' ? 'short' : 'long'; writeFlip(state.flip); renderPrint(); });

// photo upload (button + drag & drop)
async function acceptPhoto(file) {
  if (!file) return;
  state.checking = true; render();
  const result = await processPhotoFile(file);
  state.checking = false;
  state.example = false;
  touch('photo');
  state.adjust = { dx: 0, dy: 0, zoom: 1 };
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

// manual photo adjustment: buttons, slider, drag and wheel on the preview
const clampZoom = (z) => Math.min(CONFIG.photo.zoomMax, Math.max(CONFIG.photo.zoomMin, z));
function nudge(dxMm, dyMm) {
  if (!state.photo) return;
  state.adjust = { ...state.adjust, dx: +(state.adjust.dx + dxMm).toFixed(3), dy: +(state.adjust.dy + dyMm).toFixed(3) };
  state.side = 'front'; render();
}
function setZoom(z) {
  if (!state.photo) return;
  state.adjust = { ...state.adjust, zoom: +clampZoom(z).toFixed(3) };
  state.side = 'front'; render();
}
for (const b of document.querySelectorAll('.nudge')) {
  b.addEventListener('click', () => nudge(Number(b.dataset.dx) * CONFIG.photo.nudgeStepMm, Number(b.dataset.dy) * CONFIG.photo.nudgeStepMm));
}
$('f-zoom').addEventListener('input', (e) => setZoom(Number(e.target.value) / 100));
$('btn-adjust-reset').addEventListener('click', () => { state.adjust = { dx: 0, dy: 0, zoom: 1 }; render(); });

let drag = null;
previewCanvas.addEventListener('pointerdown', (e) => {
  if (!state.photo || state.side !== 'front' || (e.button !== undefined && e.button !== 0)) return;
  e.preventDefault();
  previewCanvas.setPointerCapture?.(e.pointerId);
  drag = { x: e.clientX, y: e.clientY, dx: state.adjust.dx, dy: state.adjust.dy };
  previewCanvas.classList.add('dragging');
});
previewCanvas.addEventListener('pointermove', (e) => {
  if (!drag) return;
  const mmPerPx = CONFIG.card.widthMm / previewBox.clientWidth;
  state.adjust = { ...state.adjust, dx: +(drag.dx + (e.clientX - drag.x) * mmPerPx).toFixed(3), dy: +(drag.dy + (e.clientY - drag.y) * mmPerPx).toFixed(3) };
  render();
});
const endDrag = () => { if (drag) { drag = null; previewCanvas.classList.remove('dragging'); render(); } };
previewCanvas.addEventListener('pointerup', endDrag);
previewCanvas.addEventListener('pointercancel', endDrag);
previewCanvas.addEventListener('wheel', (e) => {
  if (!state.photo || state.side !== 'front') return;
  e.preventDefault();
  setZoom(state.adjust.zoom * (e.deltaY < 0 ? 1 + CONFIG.photo.zoomStep : 1 - CONFIG.photo.zoomStep));
}, { passive: false });

// maintainer guides: outlines of the photo area (pink) and front outline (blue)
state.guides = location.hash === '#guides';
window.addEventListener('keydown', (e) => {
  if (e.ctrlKey && e.shiftKey && e.key.toLowerCase() === 'g') { e.preventDefault(); state.guides = !state.guides; render(); }
});

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
$('btn-clear-sig').addEventListener('click', () => {
  if (state.signature?.kind === 'image') { state.signature = null; touch('signature'); render(); }
  pad.clear();
});
$('f-sig-image').addEventListener('change', async (e) => {
  const file = e.target.files?.[0];
  e.target.value = '';
  if (!file) return;
  $('sig-help').textContent = 'Reading the signature…';
  const result = await signatureFromImage(file);
  state.example = false; touch('signature');
  if ('error' in result) {
    state.signature = null;
    render();
    $('sig-msg').hidden = false; $('sig-msg').textContent = result.error;
  } else {
    pad.setValue(null);
    state.signature = result;
    state.side = 'front';
    render();
  }
});

// example
$('btn-example').addEventListener('click', async () => {
  const photo = ASSETS.example ? await processPhotoUrl(ASSETS.example).catch(() => null) : null;
  for (const k of NAME_PARTS) { state[k] = EXAMPLE[k] ?? ''; $(`f-${k}`).value = state[k]; }
  state.role1 = { pick: EXAMPLE.title, custom: '' };
  state.role2 = { pick: '', custom: '' };
  state.showRole2 = false;
  state.seq = EXAMPLE.seq; state.year = THIS_YEAR;
  $('f-seq').value = state.seq;
  state.reissue = false; refreshDuplicate();
  state.photo = photo?.check.ok ? photo : null; state.photoError = '';
  state.adjust = { dx: 0, dy: 0, zoom: 1 };
  state.signature = EXAMPLE.signature;
  pad.setValue(EXAMPLE.signature);
  state.touched = { last: false, first: false, middle: false, suffix: false, title: false, title2: false, seq: false, photo: false, signature: false };
  state.example = true; state.side = 'front';
  goStep(LAST_STEP, { focus: false });        // the example is complete: show the finished card
});

// step navigation
$('btn-next').addEventListener('click', nextStep);
$('btn-back').addEventListener('click', () => goStep(state.step - 1));

// Enter in a text box moves on, rather than doing nothing
document.querySelector('.wizard').addEventListener('keydown', (e) => {
  if (e.key !== 'Enter') return;
  const t = e.target;
  if (t.tagName === 'BUTTON' || t.tagName === 'SELECT' || t.closest('.chip')) return;
  if (t.tagName === 'INPUT' && t.type === 'file') return;
  e.preventDefault();
  nextStep();
});

/* ---- review dialog ----------------------------------------------- */

const review = { front: null, back: null, data: null, fileName: '', title: '', entry: null, saving: false };

function buildReview() {
  if (!state.templates) return;
  const data = cardData();
  review.data = data;
  review.front = renderSideCanvas(state.templates, 'front', data, state.rotateBack);
  review.back = renderSideCanvas(state.templates, 'back', data, state.rotateBack);
  const jobSize = state.queue.length + 1;
  review.fileName = jobSize > 1 && state.print.layout !== 'single'
    ? `apg-ids-${jobSize}-${new Date().toISOString().slice(0, 10)}.pdf`
    : pdfFileName(state.year, data.seq, nameParts());
  review.title = jobSize > 1 && state.print.layout !== 'single'
    ? `Alpha Premier IDs — ${jobSize} cards`
    : `${employeeId(state.year, data.seq)} ${collapseSpaces(data.name)}`;
  const parts = nameParts();
  review.entry = {
    id: employeeId(state.year, data.seq), year: state.year, seq: data.seq,
    name: collapseSpaces(data.name),
    last: collapseSpaces(parts.last), first: collapseSpaces(parts.first),
    middle: collapseSpaces(parts.middle), suffix: collapseSpaces(parts.suffix),
    title: collapseSpaces(data.title),
    role1: role1(), role2: role2(),
    issuedAt: new Date().toISOString(), replacement: !!duplicate(),
  };
  renderPrint();

  $('step-check-h').textContent = 'Is everything correct?';
  $('review-sub').textContent = 'Check the details, then print. The sheet on the left is exactly what comes out.';

  // a plain summary, each line editable in one click
  const summary = $('summary');
  summary.innerHTML = '';
  const rows = [
    ['Name', collapseSpaces(data.name), 0],
    ['Job title', CONFIG.text.title.uppercase ? data.title.toLocaleUpperCase('en-US') : data.title, 1],
    ['Employee number', employeeId(state.year, data.seq), 2],
  ];
  for (const [label, value, step] of rows) {
    const row = document.createElement('div');
    const dt = document.createElement('dt'); dt.textContent = label;
    const dd = document.createElement('dd'); dd.textContent = value;
    const edit = document.createElement('button');
    edit.type = 'button'; edit.className = 'link'; edit.textContent = 'Edit';
    edit.setAttribute('aria-label', `Edit ${label.toLowerCase()}`);
    edit.addEventListener('click', () => goStep(step));
    row.append(dt, dd, edit);
    summary.append(row);
  }
  $('review-file').textContent = printFileLine();
  $('review-error').hidden = true;
  const photoWarnings = state.photo?.check?.warnings ?? [];
  $('review-warning').hidden = !photoWarnings.length;
  $('review-warning').textContent = photoWarnings.join(' ');

  $('review-stage').hidden = false;
  $('saved-stage').hidden = true;
  $('btn-download').disabled = false;
  $('btn-download').textContent = 'Download PDF';
}

async function downloadPdf() {
  review.saving = true;
  // buildPdf releases each page bitmap, so always hand it fresh canvases
  const data = review.data;
  review.front = renderSideCanvas(state.templates, 'front', data, state.rotateBack);
  review.back = renderSideCanvas(state.templates, 'back', data, state.rotateBack);
  $('btn-download').disabled = true; $('btn-download').textContent = 'Preparing PDF…'; $('btn-back').disabled = true;
  $('review-error').hidden = true;
  try {
    const plan = currentPlan();
    const cards = jobCards();
    const pages = plan
      ? printPages(printPxPerMm()).pages.map(withPageSize)      // renders one page at a time
      : [review.front, review.back];
    const blob = await buildPdf(pages, review.title);
    downloadBlob(review.fileName, blob);
    clearDraft();
    if (!state.example) {                      // the example is never a real card
      if (state.role1.pick === CUSTOM_TITLE) Titles.remember(role1());
      if (state.showRole2 && state.role2.pick === CUSTOM_TITLE) Titles.remember(role2());
      refreshTitleOptions();
      // every card in the print is issued, not just the one on screen
      const issued = (plan ? cards : [cards[cards.length - 1]]).filter(Boolean).map((c) => c.entry);
      for (const entry of issued) Register.record(entry);
      state.dupLocal = review.entry;
      (async () => { for (const entry of issued) await Register.recordShared(entry); })();
      state.queue = [];                        // the job has been printed
      renderJob();
    }
    $('step-check-h').textContent = cards.length > 1 ? `${cards.length} IDs ready to print` : 'ID ready to print';
    $('review-sub').textContent = '';
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
    $('btn-download').disabled = false; $('btn-download').textContent = 'Download PDF';
    $('btn-back').disabled = false;
  }
}

function clearCard({ keepQueue = false } = {}) {
  for (const k of NAME_PARTS) { state[k] = ''; $(`f-${k}`).value = ''; }
  state.role1 = { pick: '', custom: '' };
  state.role2 = { pick: '', custom: '' };
  state.showRole2 = false;
  $('f-title-custom').value = ''; $('f-title2-custom').value = '';
  state.seq = '';
  $('f-seq').value = '';
  state.photo = null; state.photoError = ''; state.signature = null;
  state.adjust = { dx: 0, dy: 0, zoom: 1 };
  state.year = THIS_YEAR; state.reissue = false; state.dupLocal = null; state.dupShared = null; dupToken++;
  pad.setValue(null);
  state.touched = { last: false, first: false, middle: false, suffix: false, title: false, title2: false, seq: false, photo: false, signature: false };
  state.example = false; state.side = 'front';
  if (!keepQueue) { state.queue = []; state.print.copies = null; }
  state.print.page = 0;
  clearDraft();
  $('restored-note').hidden = true;
  refreshDuplicate();
}

function newId() {
  clearCard();
  goStep(0);
  window.scrollTo({ top: 0, behavior: 'smooth' });
}

function canvasBlob(canvas) {
  return new Promise((resolve) => canvas.toBlob((b) => resolve(b), 'image/png'));
}
$('btn-png').addEventListener('click', async () => {
  const stem = review.fileName.replace(/\.pdf$/i, '');
  if (!review.front.width) {
    review.front = renderSideCanvas(state.templates, 'front', review.data, state.rotateBack);
    review.back = renderSideCanvas(state.templates, 'back', review.data, state.rotateBack);
  }
  downloadBlob(`${stem}-front.png`, await canvasBlob(review.front));
  setTimeout(async () => downloadBlob(`${stem}-back.png`, await canvasBlob(review.back)), 400);
});

$('btn-download').addEventListener('click', downloadPdf);
$('btn-again').addEventListener('click', downloadPdf);
$('btn-new-id').addEventListener('click', newId);

/* ---- print layout (step 6) --------------------------------------- */


/** Everyone in this print: the queued cards, then the one on screen. */
function jobCards() {
  const list = state.queue.map((q) => ({ ...q, current: false }));
  const p = problems();
  const ready = !!state.templates && Object.keys(p).every((k) => !p[k]);
  if (ready) {
    const data = cardData();
    list.push({
      id: employeeId(state.year, data.seq),
      label: collapseSpaces(data.name),
      title: data.title,
      data,
      entry: review.entry,
      current: true,
    });
  }
  return list;
}

/** Put the finished card in the queue and start a fresh one. */
function addAnother() {
  const data = cardData();
  state.queue.push({
    id: employeeId(state.year, data.seq),
    label: collapseSpaces(data.name),
    title: data.title,
    data,                                  // holds its own photo and signature
    entry: { ...review.entry },
  });
  clearCard({ keepQueue: true });
  goStep(0);
}

function removeFromQueue(i) {
  state.queue.splice(i, 1);
  refreshDuplicate();
  renderPrint();
  render();
}

function currentPlan() {
  return state.print.layout === 'single' ? null : planSheet(state.print.paper, state.print.layout);
}

/**
 * Copies of each card. One each by default: a sheet of nine identical IDs
 * is nine ID cards for one person, which is a security problem rather than
 * a convenience. Ask for more and you get more.
 */
function printCopies(plan, cards = jobCards().length) {
  if (!plan) return 1;
  const perSheet = plan.cols * plan.rows;
  const asked = state.print.copies;
  if (asked !== null) return Math.max(1, Math.min(perSheet * 20, asked));
  return 1;
}

function buildPrintControls() {
  const paper = $('f-paper');
  for (const [key, p] of Object.entries(CONFIG.sheet.papers)) {
    const o = document.createElement('option');
    o.value = key; o.textContent = p.label;
    paper.append(o);
  }
  paper.value = state.print.paper;
  paper.addEventListener('change', (e) => { state.print.paper = e.target.value; state.print.copies = null; renderPrint(); });
  $('f-copies').addEventListener('input', (e) => {
    const n = parseInt(e.target.value, 10);
    state.print.copies = Number.isFinite(n) ? n : null;
    renderPrint();
  });
}

function renderJob() {
  const cards = jobCards();
  const list = $('job-list');
  list.innerHTML = '';
  cards.forEach((c, i) => {
    const li = document.createElement('li');
    if (c.current) li.className = 'is-current';
    const num = document.createElement('span'); num.className = 'num'; num.textContent = `${i + 1}.`;
    const who = document.createElement('span'); who.className = 'who'; who.textContent = `${c.label} · ${c.id}`;
    li.append(num, who);
    if (c.current) {
      const tag = document.createElement('span'); tag.className = 'tag'; tag.textContent = 'on screen';
      li.append(tag);
    } else {
      const x = document.createElement('button');
      x.type = 'button'; x.className = 'chip-x'; x.textContent = '×';
      x.setAttribute('aria-label', `Take ${c.label} out of this print`);
      x.addEventListener('click', () => removeFromQueue(i));
      li.append(x);
    }
    list.append(li);
  });
  $('job-count').textContent = `${cards.length} card${cards.length === 1 ? '' : 's'}`;

  // the strip is for knowing what is already waiting; it has nothing to say
  // during the very first card
  const onCheck = state.step === LAST_STEP;
  $('queue-strip').hidden = !(state.queue.length || (onCheck && cards.length));
  $('job-note').textContent = state.queue.length
    ? (onCheck ? 'All of these print on one sheet, and are issued when you download.'
               : 'Waiting to print. Finish this card and they all go on the same sheet.')
    : 'Add another ID to put several people on the same sheet.';
}

let previewTimer = null;
const PREVIEW_DPI = 96;

function schedulePreview() {
  clearTimeout(previewTimer);
  previewTimer = setTimeout(drawPrintPreview, 120);
}

function printPages(pxPerMm) {
  const plan = currentPlan();
  if (!plan || !state.templates) return { plan: null, pages: [] };
  const cards = jobCards();
  const copies = printCopies(plan, cards.length);
  // a short-edge turn lands the back of the cut card upside down, so it is
  // drawn the other way up to cancel that out
  const backUp = plan.mode === 'duplex' && state.flip === 'short' ? !state.rotateBack : state.rotateBack;
  const entries = [];
  for (const c of cards) {
    for (let n = 0; n < copies; n++) {
      entries.push({
        get front() { return renderSideCanvas(state.templates, 'front', c.data, state.rotateBack, pxPerMm); },
        get back() { return renderSideCanvas(state.templates, 'back', c.data, backUp, pxPerMm); },
      });
    }
  }
  return { plan, pages: sheetPages(entries, plan, { pxPerMm, flip: state.flip }) };
}

function drawPrintPreview() {
  const fig = $('sheet-box');
  const plan = currentPlan();
  if (!(state.step === LAST_STEP && plan && state.templates)) { fig.hidden = true; return; }
  const { pages } = printPages(PREVIEW_DPI / 25.4);
  if (!pages.length) { fig.hidden = true; return; }
  state.print.page = Math.max(0, Math.min(pages.length - 1, state.print.page));
  const canvas = pages[state.print.page]();
  $('preview-img').src = canvas.toDataURL('image/png');
  canvas.width = canvas.height = 0;
  const side = plan.mode !== 'duplex' ? ''
    : (state.print.page % 2 === 0 ? 'Fronts — ' : 'Backs, reversed to match — ');
  $('preview-img').alt = `Preview of ${side ? side.replace(/\s*—\s*$/, '') + ', ' : ''}page ${state.print.page + 1} of the ${state.print.paper} sheet`;
  $('preview-caption').textContent = `${side}page ${state.print.page + 1} of ${pages.length} · ${state.print.paper}`;
  $('prev-page').hidden = pages.length < 2;
  $('next-page').hidden = pages.length < 2;
  $('prev-page').disabled = state.print.page === 0;
  $('next-page').disabled = state.print.page === pages.length - 1;
  fig.hidden = false;
}

function renderPrint() {
  renderJob();
  const plan = currentPlan();
  $('print-opts').hidden = !plan;

  if (!plan) {
    $('print-note').textContent = '';
  } else {
    const per = plan.cols * plan.rows;
    const cards = jobCards().length;
    const copies = printCopies(plan, cards);
    const total = cards * copies;
    $('f-paper').value = state.print.paper;
    $('f-copies').max = String(per * 20);
    if (document.activeElement !== $('f-copies')) $('f-copies').value = String(copies);
    $('field-copies').hidden = false;
    const sheets = Math.ceil(total / per);
    const pagesPerSheet = plan.mode === 'pairs' ? 1 : 2;
    const turned = plan.turned ? ', turned on their side' : '';
    const orient = plan.landscape ? 'landscape' : 'portrait';
    $('field-copies').querySelector('.field-label').textContent =
      jobCards().length > 1 ? 'Copies of each card' : 'Copies of this card';
    $('print-note').textContent =
      `${per} per ${state.print.paper} sheet (${plan.cols} × ${plan.rows}, ${orient}${turned}). `
      + `${total} card${total === 1 ? '' : 's'} → ${sheets} sheet${sheets === 1 ? '' : 's'}, `
      + `${sheets * pagesPerSheet} page${sheets * pagesPerSheet === 1 ? '' : 's'}. `
      + (plan.mode === 'duplex'
        ? `Print both sides at 100%, turning the paper on the ${state.flip === 'short' ? 'short' : 'long'} edge, then cut.`
        : 'Cut each pair out and stick them back to back.');
  }
  if (review.fileName) $('review-file').textContent = printFileLine();
  schedulePreview();
}

function printFileLine() {
  const plan = currentPlan();
  const C = CONFIG.card;
  if (!plan) return `File: ${review.fileName} · 2 pages, ${C.widthMm} × ${C.heightMm} mm, ${CONFIG.card.printDpi} DPI`;
  const cards = jobCards().length;
  const sheets = Math.ceil((cards * printCopies(plan, cards)) / (plan.cols * plan.rows));
  const pages = plan.mode === 'pairs' ? sheets : sheets * 2;
  return `File: ${review.fileName} · ${pages} page${pages === 1 ? '' : 's'} of ${state.print.paper}, cards ${C.widthMm} × ${C.heightMm} mm, ${CONFIG.card.printDpi} DPI`;
}

/* ---- shared register settings ------------------------------------ */

function setRegisterUrlMsg(text, kind = 'ok') {
  const el = $('register-url-msg');
  el.className = `msg msg-${kind}`;
  el.textContent = text;
}

function showRegisterUrl() {
  $('f-register-url').value = Register.url;
  $('btn-register-clear').disabled = !Register.url;
  if (!Register.url) setRegisterUrlMsg('Not set: duplicates are checked against this computer only.', 'ok');
}

$('btn-register-test').addEventListener('click', async () => {
  const url = $('f-register-url').value.trim();
  if (!url) { setRegisterUrlMsg('Paste the web-app address first, or leave it empty to use this computer only.', 'error'); return; }
  const wrong = Register.urlLooksWrong(url);
  if (wrong) { setRegisterUrlMsg(wrong, 'error'); return; }
  $('btn-register-test').disabled = true;
  setRegisterUrlMsg('Checking…', 'ok');
  const result = await Register.ping(url);
  $('btn-register-test').disabled = false;
  if (result.error) { setRegisterUrlMsg(result.error, 'error'); return; }
  Register.setUrl(url);
  setRegisterUrlMsg(`Connected. The shared register holds ${result.rows} card${result.rows === 1 ? '' : 's'}.`, 'ok');
  refreshDuplicate();
  render();
});

$('btn-register-clear').addEventListener('click', () => {
  Register.setUrl('');
  showRegisterUrl();
  setRegisterUrlMsg('Shared register switched off on this computer.', 'ok');
  refreshDuplicate();
  render();
});

/* ---- batch from CSV ---------------------------------------------- */

const batchFiles = { csv: null, photos: null, sigs: null };
const updateBatchButton = () => { $('btn-batch').disabled = !(batchFiles.csv && batchFiles.photos?.length && state.templates); };

$('f-csv').addEventListener('change', (e) => {
  batchFiles.csv = e.target.files?.[0] ?? null;
  $('csv-msg').textContent = batchFiles.csv ? batchFiles.csv.name : 'Columns: name, title, seq, and optionally year, photo, signature, replacement.';
  updateBatchButton();
});
$('f-photos').addEventListener('change', (e) => {
  batchFiles.photos = e.target.files;
  $('photos-msg').textContent = batchFiles.photos?.length ? `${batchFiles.photos.length} photo file(s) selected.` : 'Select every photo file. They are matched to the CSV by file name.';
  updateBatchButton();
});
$('f-sigs').addEventListener('change', (e) => {
  batchFiles.sigs = e.target.files;
  $('sigs-msg').textContent = batchFiles.sigs?.length ? `${batchFiles.sigs.length} signature file(s) selected.` : 'Photos or scans of signatures on white paper. Rows without one get a card with no signature.';
});
$('btn-csv-template').addEventListener('click', () => {
  downloadBlob('apg-id-batch-template.csv', new Blob([CSV_TEMPLATE], { type: 'text/csv;charset=utf-8' }));
});

function renderBatchReport(results) {
  const list = $('batch-report');
  list.innerHTML = '';
  for (const r of results) {
    const li = document.createElement('li');
    const head = document.createElement('strong');
    head.textContent = r.ok ? `${r.id} · ${r.label}` : `Line ${r.line} · ${r.label}`;
    const note = document.createElement('span');
    if (!r.ok) { note.className = 'row-bad'; note.textContent = r.problem; }
    else if (r.warnings?.length || r.noSignature) {
      note.className = 'row-warn';
      note.textContent = [r.noSignature ? 'No signature on this card.' : '', ...(r.warnings ?? [])].filter(Boolean).join(' ');
    } else { note.className = 'row-ok'; note.textContent = 'Ready.'; }
    li.append(head, note);
    list.appendChild(li);
  }
}

async function runBatchUi() {
  $('btn-batch').disabled = true;
  $('batch-report').innerHTML = '';
  $('batch-status').textContent = 'Reading the list…';
  try {
    const csvText = await batchFiles.csv.text();
    const out = await runBatch({
      csvText,
      photos: batchFiles.photos,
      signatures: batchFiles.sigs,
      templates: state.templates,
      rotateBack: state.rotateBack,
      layout: $('f-layout').value,
      paper: $('f-batch-paper').value,
      onProgress: (phase, i, n) => { $('batch-status').textContent = `Checking ${i} of ${n}…`; },
    });
    if (out.results) renderBatchReport(out.results);
    if (out.error) { $('batch-status').textContent = out.error; return; }

    const blob = await buildPdf(out.pages.map(withPageSize), out.title, {
      onProgress: (done, total) => { $('batch-status').textContent = `Building the PDF — page ${done} of ${total}…`; },
    });
    downloadBlob(out.fileName, blob);

    for (const r of out.good) Register.record(r.entry);
    if (Register.shared) (async () => { for (const r of out.good) await Register.recordShared(r.entry); })();
    refreshDuplicate();
    render();

    const skipped = out.results.length - out.good.length;
    $('batch-status').textContent = `${out.good.length} card${out.good.length === 1 ? '' : 's'} saved as ${out.fileName}`
      + (skipped ? ` · ${skipped} row${skipped === 1 ? '' : 's'} skipped, see below.` : '. Print at 100 %, both sides.');
  } catch (err) {
    console.error(err);
    $('batch-status').textContent = 'Something went wrong while building the batch. Try a smaller list, or check the CSV.';
  } finally {
    updateBatchButton();
  }
}
$('btn-add-another').addEventListener('click', addAnother);
$('prev-page').addEventListener('click', () => { state.print.page--; drawPrintPreview(); });
$('next-page').addEventListener('click', () => { state.print.page++; drawPrintPreview(); });

$('btn-batch').addEventListener('click', runBatchUi);

// paper list and the "N per sheet" hint for the batch panel
for (const [key, p] of Object.entries(CONFIG.sheet.papers)) {
  const o = document.createElement('option');
  o.value = key; o.textContent = p.label;
  $('f-batch-paper').append(o);
}
$('f-batch-paper').value = CONFIG.sheet.defaultPaper;
function renderBatchLayout() {
  const layout = $('f-layout').value;
  $('f-batch-paper').closest('.field').hidden = layout === 'cards';
  if (layout === 'cards') { $('layout-msg').textContent = 'Two pages per person, each the size of the card.'; return; }
  const plan = planSheet($('f-batch-paper').value, layout);
  $('layout-msg').textContent = `${plan.count} cards per sheet (${plan.cols} × ${plan.rows}, ${plan.landscape ? 'landscape' : 'portrait'}).`;
}
$('f-layout').addEventListener('change', renderBatchLayout);
$('f-batch-paper').addEventListener('change', renderBatchLayout);
renderBatchLayout();

/* ---- boot -------------------------------------------------------- */

new ResizeObserver(drawPreview).observe(previewBox);

$('app-version').textContent = `${APP_NAME} ${APP_VERSION}`;
$('brand-logo').src = ASSETS.logo;
showRegisterUrl();
buildPrintControls();
if (restoreDraft()) {
  $('restored-note').hidden = false;
  // carry on where the typing stopped
  const p = problems();
  const firstGap = STEPS.findIndex((_, i) => !stepDone(i, p));
  state.step = firstGap < 0 ? 0 : firstGap;
}

Promise.all([loadTemplates(), loadCardFonts()])
  .then(([templates]) => {
    state.templates = templates;
    updateBatchButton();
    if (state.step === LAST_STEP) buildReview();
    render();
  })
  .catch((err) => { state.loadError = `${err.message} Reload the page to try again.`; render(); });

render();
