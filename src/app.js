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
const LAYOUT_KEY = 'apg-id-print-layout';
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
// `land` is the control to put the cursor on when the step opens. Taking
// the first focusable thing in the section instead put the cursor on the
// year dropdown when the step is about the number, and on the signature
// pad's Clear button — so arriving and typing went into the wrong field,
// and arriving and pressing space wiped the signature. null means the
// step has nothing to type into.
const STEPS = [
  { id: 'name',   label: 'Name',      fields: ['last', 'first', 'middle', 'suffix', 'nameFit'], land: 'f-last' },
  { id: 'title',  label: 'Job title', fields: ['title', 'title2', 'titleFit'] },
  { id: 'number', label: 'Number',    fields: ['seq'], land: 'f-seq' },
  { id: 'photo',  label: 'Photo',     fields: ['photo'] },
  { id: 'sign',   label: 'Signature', fields: ['signature'], land: null },
  { id: 'check',  label: 'Check',     fields: [] },
];
const LAST_STEP = STEPS.length - 1;
const SIGN_STEP = STEPS.findIndex((s) => s.id === 'sign');

// Signing mode: the signing board over the whole window, until Enter
// (keep it) or Esc (cancel) — see "signing mode" below.
const signing = { on: false };

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
  photoFix: '',          // 'prompt' when the AI prompt is the way out of this rejection
  adjust: { dx: 0, dy: 0, zoom: 1 },   // manual nudge on top of the automatic placement
  guides: false,        // maintainer overlay (Ctrl+Shift+G or #guides)
  signature: null,
  touched: { last: false, first: false, middle: false, suffix: false, title: false, title2: false, seq: false, photo: false, signature: false },
  step: 0,
  tool: null,           // which tool panel the rail has open, if any
  // Cards print front-and-back side by side, to be cut out and stuck
  // together, unless the printer setting says both sides.
  print: { layout: readLayout(), paper: CONFIG.sheet.defaultPaper, copies: null, page: 0 },
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
function readLayout() { try { return localStorage.getItem(LAYOUT_KEY) === 'duplex' ? 'duplex' : 'pairs'; } catch { return 'pairs'; } }
function writeLayout(v) { try { localStorage.setItem(LAYOUT_KEY, v); } catch { /* private mode */ } }

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
       // Not knowing is not the same as knowing it is free. The register
       // is read once at startup, so this is normally over before it can
       // be seen — but while it is true, the number is not cleared and
       // the step does not open. Unreachable is different: that settles
       // to a warning, because a sheet being down cannot stop HR working.
       : state.dupShared === 'pending' ? 'Checking the shared register for this number…'
       : '',
    photo: state.photoError || (state.photo ? '' : 'Upload the new hire’s photo.'),
    signature: hasInk(state.signature) ? '' : 'Ask the new hire to sign: press Ctrl + D.',
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
// state.dupShared is one of four things: an entry, null for definitely
// not there, 'pending' for not known yet, or undefined for could not ask.
// Only an entry is a duplicate — 'pending' is a truthy string and used to
// sail straight through here as one.
const sharedDuplicate = () => (state.dupShared && typeof state.dupShared === 'object' ? state.dupShared : null);
function duplicate() { return queuedDuplicate() || sharedDuplicate() || state.dupLocal || null; }
function duplicateMessage() {
  const d = duplicate();
  if (d.queued) return `${d.id} is already waiting in this print, for ${d.name}. Use a different number.`;
  const where = d === state.dupShared ? 'in the shared register' : 'on this computer';
  return `${d.id} was already issued to ${d.name} on ${formatIssuedAt(d.issuedAt)} (${where}). Check the number with HR, or tick “Replacement card” below.`;
}

let dupToken = 0;

/**
 * Is this number already taken? Answered from memory when it can be.
 *
 * The register is read whole, once, so this is a Map lookup for every
 * number after the first — no round trip, no waiting, nothing to debounce.
 * Only a cold start, or a register that has not been read yet, goes to
 * the network, and until that answers the number is 'pending' rather
 * than clean.
 */
function refreshDuplicate() {
  const seq = seqNumber();
  state.dupLocal = Register.find(state.year, seq);
  if (!Register.shared || seq === null) { state.dupShared = null; return; }

  const known = Register.findSharedNow(state.year, seq);
  if (known !== undefined) { state.dupShared = known; return; }   // already know

  state.dupShared = 'pending';
  const token = ++dupToken;
  Register.loadIndex()
    .then(() => (token === dupToken ? Register.findSharedNow(state.year, seqNumber()) : null))
    .catch(() => undefined)                  // could not ask: warn, do not block
    .then((hit) => {
      if (token !== dupToken) return;
      state.dupShared = hit;
      settleDuplicate();
    });
}

/**
 * The register has answered. If it answered late — after the number was
 * accepted and the form moved on — the step it belongs to is no longer
 * finished, so go back to it and say why rather than letting a card go
 * out on a number somebody else already used.
 */
function settleDuplicate() {
  const p = problems();
  const stop = furthestStep(p);
  if (stop < state.step) {
    sayBlocked(stop, p);
    goStep(stop);
    focusProblem(stop, p);
    return;
  }
  render();
}

/* ---- steps ------------------------------------------------------- */

const stepProblems = (i, p = problems()) => STEPS[i].fields.filter((f) => p[f]);
const stepDone = (i, p) => stepProblems(i, p).length === 0;

// A step can be opened once every step before it is filled in.
const stepReachable = (i, p) => STEPS.slice(0, i).every((_, j) => stepDone(j, p));

/**
 * The furthest step that may be open: the first one that is not
 * finished, or the end if they all are.
 *
 * Going back is always allowed. Going forward stops here, and this is
 * the only place that decides it — the chips, the Next button and the
 * draft that comes back after a reload all go through goStep, so none
 * of them can route around it. They used to disagree: Next looked only
 * at the step you were on, so a draft restored onto step 4 with a name
 * too long for the card walked all the way to Check without ever
 * mentioning the name.
 */
function furthestStep(p = problems()) {
  const gap = STEPS.findIndex((_, i) => !stepDone(i, p));
  return gap < 0 ? LAST_STEP : gap;
}

/**
 * Open a step. `force` is for the two places that mean it: the finished
 * example, and cancelling an extra ID to go back to a print that is
 * already queued. Everything else is capped.
 */
function goStep(i, { focus = true, force = false } = {}) {
  if (signing.on) closeSigning({ keep: false, refocus: false });
  const want = Math.max(0, Math.min(LAST_STEP, i));
  state.step = force ? want : Math.min(want, furthestStep());
  if (state.step === LAST_STEP) buildReview();
  render();
  if (state.step !== LAST_STEP) state.side = 'front';
  const section = $(`step-${STEPS[state.step].id}`);
  section.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  if (focus && state.step !== LAST_STEP) {
    const step = STEPS[state.step];
    const land = 'land' in step
      ? (step.land && $(step.land))
      : section.querySelector('input:not([type=hidden]):not([hidden]), select, button:not([hidden])');
    if (land) land.focus({ preventScroll: true });
  }
}

/**
 * Say that the way on is shut, and why.
 *
 * The status line already carries the standing hint, and a live region
 * whose words do not change announces nothing — so a refusal gets its
 * own node, its own wording, and is written only when somebody actually
 * tries. Clearing it first makes a second identical refusal announce
 * again instead of passing in silence.
 */
let blockedAbout = null;      // the step the refusal on screen is about

function sayBlocked(i, p = problems()) {
  blockedAbout = i;
  const el = $('blocked');
  const first = stepProblems(i, p)[0];
  const text = i === state.step
    ? `Can’t continue yet. ${p[first] || `Finish ${STEPS[i].label.toLowerCase()}.`}`
    : `Can’t continue yet. Step ${i + 1}, ${STEPS[i].label}, needs fixing first.`;
  el.textContent = '';
  requestAnimationFrame(() => { el.textContent = text; $('status').hidden = true; });
}

/** Put the cursor on the thing that is missing, if it can be shown. */
function focusProblem(i, p = problems()) {
  const target = FIELD_INPUT[stepProblems(i, p)[0]];
  const el = target && $(target);
  if (el && !el.hidden && el.offsetParent !== null) el.focus();
}

/** Move on, or show what is still missing. */
function nextStep() {
  const p = problems();
  const stop = furthestStep(p);

  // something behind us came undone — a name that stopped fitting, a
  // number that turned out to be a duplicate once the register answered
  if (stop < state.step) {
    sayBlocked(stop, p);
    goStep(stop);
    focusProblem(stop, p);
    return;
  }

  const missing = stepProblems(state.step, p);
  if (missing.length) {
    for (const f of STEPS[state.step].fields) if (f in state.touched) state.touched[f] = true;
    state.touched.last = state.touched.last || STEPS[state.step].fields.includes('nameFit');
    render();
    sayBlocked(state.step, p);
    focusProblem(state.step, p);
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
        const stop = furthestStep(probs);
        if (i <= stop) { goStep(i); return; }
        // locked: go to what is blocking it and say which step that is
        sayBlocked(stop, probs);
        goStep(stop);
        focusProblem(stop, probs);
      });
      li.append(b);
      bar.append(li);
    });
  }
  const stop = furthestStep(p);
  [...bar.querySelectorAll('.step-chip')].forEach((b, i) => {
    const locked = i > stop;
    const done = stepDone(i, p) && i !== LAST_STEP;
    b.classList.toggle('is-current', i === state.step);
    // a step cannot be both finished and out of reach: locked wins, or
    // the chip claims to be done and refuses to open, which reads as a
    // broken button rather than as a step that is waiting on another
    b.classList.toggle('is-done', done && i !== state.step && !locked);
    b.classList.toggle('is-locked', locked);
    // aria-disabled, not disabled: a disabled button leaves the tab order
    // and cannot be asked why it will not open
    b.setAttribute('aria-disabled', String(locked));
    b.setAttribute('aria-current', i === state.step ? 'step' : 'false');
    b.setAttribute('aria-label', locked
      ? `${STEPS[i].label}. Locked until step ${stop + 1}, ${STEPS[stop].label}, is finished.`
      : `${STEPS[i].label}${done ? '. Finished.' : ''}`);
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
      ? 'Print both sides, then cut along the marks.'
      : 'Cut along the marks and stick each pair back to back.')
    : state.side === 'back'
      ? 'The back is the same on every card.'
      : '';
  document.querySelector('.segmented').hidden = onCheckStep;
  $('card-box').hidden = !state.templates || onCheckStep;
  $('card-loading').hidden = !!state.templates;
  if (!onCheckStep) $('sheet-box').hidden = true;      // the sheet belongs to the last step
  renderJob();
  $('card-loading').textContent = state.loadError || 'Loading the card template…';

  // example tag

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
  const titleFit = state.touched.title ? p.titleFit : '';
  $('title-preview').hidden = !titleFit;
  $('title-preview').textContent = titleFit;
  const dup = duplicate();
  // nothing to say when the check is working: a number that is already
  // taken turns the field red and names who has it. The only thing worth
  // a line here is the check not working.
  const seqHelp = !Register.shared ? 'Checked against this computer only.'
    : state.dupShared === undefined ? 'Could not reach the shared register — only this computer was checked.'
    : '';
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
    : ph ? { kind: 'ok', text: '' }
    : { kind: 'hint', text: 'A cut-out PNG, or a photo with a solid green background. Chest-up, facing forward.' };
  const photoMsg = $('photo-msg');
  photoMsg.className = `msg msg-${pm.kind}`;
  photoMsg.textContent = pm.text;
  if (pm.kind === 'error') photoMsg.setAttribute('role', 'alert'); else photoMsg.removeAttribute('role');
  $('photo-btn').textContent = state.checking ? 'Checking photo…' : ph ? 'Change photo' : 'Upload photo';

  // When the photo was turned away for something the prompt actually
  // fixes, put the fix in front of the operator instead of leaving a
  // sentence pointing at a collapsed panel they have no reason to open.
  const wantsPrompt = pm.kind === 'error' && state.photoFix === 'prompt';
  $('guide').classList.toggle('is-wanted', wantsPrompt);
  $('guide-label').textContent = wantsPrompt ? 'Get a photo the ID will accept' : 'No green background?';
  if (wantsPrompt && $('guide-body').hidden) setGuideOpen(true);
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
  $('f-signature').style.visibility = sigImage ? 'hidden' : '';   // keeps the box its height
  $('sig-image').hidden = !sigImage;
  if (sigImage && $('sig-image').dataset.for !== state.signature.fileName) {
    $('sig-image').src = state.signature.mask.toDataURL('image/png');
    $('sig-image').dataset.for = state.signature.fileName ?? 'scan';
  }
  $('sig-help').textContent = sigImage
    ? `Scanned: ${state.signature.fileName ?? 'signature'}. Press Ctrl + D to sign on screen instead.`
    : 'It prints in white on the card.';
  $('btn-clear-sig').disabled = !hasInk(state.signature);
  renderSignGuide();
  $('sig-msg').hidden = !shown('signature');
  $('sig-msg').textContent = shown('signature');

  // number line + status
  const missingHere = stepProblems(state.step, p);
  const missingAll = Object.keys(p).filter((k) => p[k]);
  // after cancelling an "Add another ID" the form is empty on purpose and
  // the job is whatever is queued, so nothing is outstanding
  const queueOnly = state.step === LAST_STEP && !!state.queue.length && !startedCard();
  $('status').textContent = !state.templates ? 'Loading the card template…'
    : state.step === LAST_STEP ? (missingAll.length && !queueOnly ? `Still needed: ${missingAll.map((k) => FIELD_LABEL[k]).join(', ')}.` : '')
    : missingHere.length ? `Still needed: ${missingHere.map((k) => FIELD_LABEL[k]).join(', ')}.`
    : '';

  $('opt-rotate').checked = state.rotateBack;
  $('opt-flip').value = state.flip;

  // one step at a time
  STEPS.forEach((s, i) => { $(`step-${s.id}`).hidden = i !== state.step; });
  renderStepBar(p);
  // a refusal is about a moment, and the moment ends when the thing it
  // named is fixed — leaving it up turns it into a standing accusation
  if (blockedAbout !== null && stepDone(blockedAbout, p)) {
    $('blocked').textContent = '';
    blockedAbout = null;
  }
  // one line under the buttons, not two saying the same thing: the quiet
  // standing hint gives way to the refusal while there is one
  $('status').hidden = blockedAbout !== null;
  $('btn-back').hidden = state.step === 0;
  $('btn-next').hidden = state.step === LAST_STEP;
  $('btn-next').textContent = state.step === LAST_STEP - 1 ? 'Check the card' : 'Next';
  // Next is never marked unavailable, neither with disabled nor with
  // aria-disabled. A disabled button is a dead end: it cannot be focused
  // and it cannot explain itself, so the one thing the operator needs —
  // what is missing — is the one thing it will not say. Pressing it on an
  // unfinished step is how you ask, and the refusal answers in #blocked.
  $('btn-next').disabled = !state.templates;

  document.body.classList.toggle('checking', state.step === LAST_STEP);
  renderTools();
  renderRegister();
  renderDesign();
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
  // a badge on the rail, and nothing at all when there is nothing to count
  $('rail-register-count').textContent = all.length;
  $('rail-register-count').hidden = !all.length;
  $('btn-register-csv').disabled = !all.length;
  $('register-note').textContent = all.length
    ? 'The most recent cards issued here. Every one is also written to the shared register, which is what the duplicate check reads.'
    : 'Nothing issued yet. Every card you download is listed here, and written to the shared register.';
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
for (const k of [...NAME_PARTS, 'seq']) $(`f-${k}`).addEventListener('blur', () => {
  // tidy the capitals once the field is left, rather than fighting the typing
  if (NAME_PARTS.includes(k)) {
    const fixed = autoCapitalise(state[k], { suffix: k === 'suffix' });
    if (fixed !== state[k]) { state[k] = fixed; $(`f-${k}`).value = fixed; saveDraft(); }
  }
  touch(k); render();
});
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
  if ('error' in result) { state.photo = null; state.photoError = result.error; state.photoFix = ''; }
  else if (result.check.ok) { state.photo = result; state.photoError = ''; state.photoFix = ''; }
  else {
    state.photo = null;
    state.photoError = result.check.problem ?? 'This photo can’t be used.';
    state.photoFix = result.check.fix ?? '';
  }
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
function setGuideOpen(open) {
  $('guide-body').hidden = !open;
  $('guide-teaser').hidden = open;
  $('guide-sign').textContent = open ? '−' : '+';
  $('btn-guide').setAttribute('aria-expanded', String(open));
}
$('btn-guide').addEventListener('click', () => {
  setGuideOpen($('guide-body').hidden);
});
$('btn-copy-prompt').addEventListener('click', async () => {
  try { await navigator.clipboard.writeText(PHOTO_PROMPT); $('copy-status').textContent = 'Copied.'; }
  catch { $('photo-prompt').focus(); $('photo-prompt').select(); $('copy-status').textContent = 'Selected. Press Ctrl+C or ⌘C to copy.'; }
});

// The pad on the step shows the signature; signing itself happens on the
// signing board (below), so the pad takes no strokes of its own.
const pad = createSignaturePad($('f-signature'), (sig) => {
  state.example = false; touch('signature'); state.signature = sig; state.side = 'front'; render();
}, { input: false });

/* ---- signing mode ------------------------------------------------
   A pen tablet is mapped to the whole screen, so the small pad on the
   step was a few centimetres of the tablet, and the pen's cursor — which
   Windows moves itself, ignoring any hold on the mouse — wandered off it
   and clicked whatever was there. So signing has a mode of its own:

     start   Ctrl+D, or Enter with nothing signed yet, or a click on the
             pad or on "Start signing"
     sign    the signing board fills the window and takes the pen from
             anywhere on it; points past its edge are pinned to the edge,
             the system cursor is hidden and a dot that cannot leave the
             board shows where the pen is. In the desktop app the mouse
             is also held on the board (ClipCursor), and the window fills
             the screen, because a pen ignores that hold: with nothing
             else on screen, a pen tap cannot land on another program.
             Nothing behind the board can be clicked.
     Enter      keep it and go on to Check
     Ctrl+Z     undo the last stroke (or the Clear)
     Backspace  clear and start again
     Esc        cancel: the signature from before stays
   Under the board the four keys are listed, and that is all they are: a
   pen's pointer roams the whole screen whatever the mouse is held to,
   so nothing in signing mode answers a pen or a mouse but the board. A
   finger can tap them, so a tablet PC without its keyboard is not stuck.
   The board opens empty every time: signing again replaces the
   signature rather than drawing over it.
   The cursor is let go when the mode ends, when the window loses focus
   and when the app closes (app/src-tauri/src/desktop.rs).
   ------------------------------------------------------------------ */

const board = createSignaturePad($('sign-board-canvas'), (sig) => renderBoardStatus(sig), {
  surface: $('sign-sheet'), clamp: true, cursor: $('sign-cursor'), fill: true,
});

function renderBoardStatus(sig, { warn = '', note = '' } = {}) {
  const strokes = sig?.strokes?.length || 0;
  $('sign-board-hint').hidden = strokes > 0;
  $('sheet-keep').toggleAttribute('data-off', !strokes);
  $('sheet-clear').toggleAttribute('data-off', !strokes);
  $('sheet-undo').toggleAttribute('data-off', !board.canUndo());
  const el = $('sign-sheet-status');
  el.dataset.tone = warn ? 'warn' : '';
  el.textContent = warn || note
    || (strokes ? `${strokes} stroke${strokes === 1 ? '' : 's'}. Press Enter to keep it, or keep signing.` : 'Waiting for the pen… sign anywhere in the box.');
}

const GUIDE_LAST = {
  unsigned: 'Press <kbd>Enter</kbd> to keep it and go to Check. <kbd>Esc</kbd> cancels.',
  signed: 'Signed. Press <kbd>Enter</kbd> to go on to Check, or <kbd>Ctrl</kbd> + <kbd>D</kbd> to sign again.',
};

function renderSignGuide() {
  const signed = hasInk(state.signature);
  const scanned = state.signature?.kind === 'image';
  const states = !signed ? ['active', '', ''] : scanned ? ['', '', 'active'] : ['done', 'done', 'active'];
  [...$('sign-guide').children].forEach((li, i) => {
    li.dataset.state = states[i];
    li.querySelector('.sign-guide-n').textContent = states[i] === 'done' ? '✓' : String(i + 1);
  });
  const last = signed ? GUIDE_LAST.signed : GUIDE_LAST.unsigned;
  if ($('sign-guide-3').innerHTML !== last) $('sign-guide-3').innerHTML = last;
  $('btn-sign-text').textContent = signed ? 'Sign again' : 'Start signing';
  $('sig-pad').setAttribute('aria-label', signed
    ? 'Signature, signed. Press Control D, or click here, to sign again.'
    : 'Signature, not signed yet. Press Control D, or click here, to sign.');
}

function openSigning() {
  if (signing.on || state.step !== SIGN_STEP) return;
  signing.on = true;
  document.body.classList.add('signing');
  if (document.activeElement && document.activeElement !== document.body) document.activeElement.blur();
  $('sign-sheet-who').textContent = printedName().trim() || 'the new hire';
  $('sign-sheet').hidden = false;
  board.setValue(null);                 // a fresh board: Esc keeps the signature there was
  renderBoardStatus(null);
  // A pen tablet covers every screen unless told otherwise, and full screen
  // covers only this one.
  $('sign-sheet-note').hidden = !(Desktop.available && window.screen.isExtended && (pens.tablets.length || pens.seen));
  $('sign-cursor').hidden = true;
  $('sign-sheet').focus({ preventScroll: true });
  // the window fills the screen first; "resize" below then holds the
  // cursor on the board where it ends up
  Desktop.fullScreen(true).then(holdOnBoard);
}

function closeSigning({ keep, refocus = true }) {
  if (!signing.on) return;
  if (keep) {
    // kept in the board's own pixels; the card and the pad each fit it
    state.signature = board.value();
    state.example = false; touch('signature'); state.side = 'front';
    pad.setValue(state.signature);
  }
  signing.on = false;
  $('sign-sheet').hidden = true;
  document.body.classList.remove('signing');
  Desktop.holdCursor(null);
  Desktop.fullScreen(false);
  render();
  // for the keyboard: on Next once signed (Enter moves on), else on Start signing
  if (refocus && state.step === SIGN_STEP) $(hasInk(state.signature) ? 'btn-next' : 'btn-sign').focus({ preventScroll: true });
}

function keepSigning() {
  if (!board.value()) { renderBoardStatus(null, { warn: 'Nothing signed yet. Sign in the box, or press Esc to cancel.' }); return; }
  closeSigning({ keep: true });
  nextStep();
}
function clearBoard() {
  if (!board.value()) return;
  board.clear();
  renderBoardStatus(null, { note: 'Cleared. Press Ctrl + Z to bring it back.' });
}

function clearSignature() {
  if (state.signature?.kind === 'image') { state.signature = null; touch('signature'); render(); }
  pad.clear();
}
$('btn-clear-sig').addEventListener('click', clearSignature);
$('btn-sign').addEventListener('click', openSigning);
const SHEET_ACTS = { keep: keepSigning, undo: () => board.undo(), clear: clearBoard, cancel: () => closeSigning({ keep: false }) };
$('sign-keys').addEventListener('pointerup', (e) => {
  if (e.pointerType !== 'touch') return;             // a pen or mouse presses nothing here
  const act = e.target.closest?.('[data-act]')?.dataset.act;
  if (act && signing.on) SHEET_ACTS[act]();
});
// nothing in signing mode is clicked: the board draws, the keys do the rest
$('sign-sheet').addEventListener('click', (e) => { e.preventDefault(); e.stopPropagation(); }, true);
$('sig-pad').addEventListener('click', openSigning);
$('sig-pad').addEventListener('keydown', (e) => {
  if (e.key === ' ' || e.key === 'Enter') { e.preventDefault(); e.stopPropagation(); openSigning(); }
});

// While signing, these keys and no others — before any other handler.
window.addEventListener('keydown', (e) => {
  if (!signing.on) return;
  e.preventDefault();
  e.stopImmediatePropagation();
  const ctrl = e.ctrlKey || e.metaKey;
  if (e.key === 'Enter') keepSigning();
  else if (e.key === 'Escape') closeSigning({ keep: false });
  else if (e.key === 'Backspace' || e.key === 'Delete') clearBoard();
  else if (ctrl && (e.code === 'KeyZ' || e.key.toLowerCase() === 'z')) board.undo();
}, true);

// On the signature step: Ctrl+D starts signing from anywhere, and so does
// Enter while nothing is signed. Once it is signed, Enter moves on.
window.addEventListener('keydown', (e) => {
  if (signing.on || state.step !== SIGN_STEP) return;
  const t = e.target;
  const typing = t.closest?.('input, textarea, select, [contenteditable="true"]');
  if ((e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey && (e.code === 'KeyD' || e.key.toLowerCase() === 'd')) {
    e.preventDefault();
    e.stopImmediatePropagation();
    openSigning();
  } else if (e.key === 'Enter' && !typing && !t.closest?.('a, label')) {
    const signed = hasInk(state.signature);
    const onSign = t === $('btn-sign') || t === $('sig-pad');
    if (signed && !onSign && t.tagName !== 'BUTTON') { e.preventDefault(); e.stopImmediatePropagation(); nextStep(); }
    else if (!signed && t.tagName !== 'BUTTON') { e.preventDefault(); e.stopImmediatePropagation(); openSigning(); }
    // a button (Sign again, Next, Clear…) does what it says
  }
}, true);

window.addEventListener('wheel', (e) => { if (signing.on) e.preventDefault(); }, { passive: false, capture: true });
window.addEventListener('contextmenu', (e) => { if (signing.on) e.preventDefault(); }, true);

// The window lets the mouse go when it loses focus; hold it again on return,
// and again whenever the board moves or grows (going full screen does both,
// in more than one step).
const holdOnBoard = () => { if (signing.on) Desktop.holdCursor($('sign-board')); };
window.addEventListener('focus', holdOnBoard);
window.addEventListener('resize', holdOnBoard);
new ResizeObserver(holdOnBoard).observe($('sign-board'));

/* ---- is there a pen tablet? ---------------------------------------
   The desktop app asks Windows for the pens it has registered, and asks
   again every few seconds while the signature step is open, so plugging
   one in shows up without a restart. A browser cannot ask; there, and
   for a tablet whose driver poses as a mouse, the first pen stroke or
   hover over the page counts too.
   ------------------------------------------------------------------ */

const pens = { tablets: [], seen: false };

function renderPenStatus() {
  const found = pens.tablets.length > 0 || pens.seen;
  const name = (pens.tablets.find((t) => !t.built_in) || pens.tablets[0])?.name;
  const text = found
    ? `Pen tablet detected${name ? ` · ${name}` : ''}`
    : Desktop.available
      ? 'No pen tablet detected · sign with the mouse'
      : 'No pen tablet detected yet · move the pen over the tablet to check';
  for (const [box, label] of [['pen-status', 'pen-status-text'], ['sheet-pen-status', 'sheet-pen-text']]) {
    $(box).dataset.state = found ? 'found' : 'none';
    $(label).textContent = text;
  }
}

async function refreshPens() {
  const before = pens.tablets.map((t) => t.name).join('|');
  pens.tablets = await Desktop.penTablets();
  if (pens.tablets.map((t) => t.name).join('|') !== before) renderPenStatus();
}

window.addEventListener('pointerover', (e) => {
  if (e.pointerType === 'pen' && !pens.seen) { pens.seen = true; renderPenStatus(); }
}, { passive: true, capture: true });

if (Desktop.available) {
  setInterval(() => { if (state.step === SIGN_STEP && !document.hidden) refreshPens(); }, 3000);
}
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

/**
 * Fill the form with the sample card and jump to the end.
 *
 * It has no button any more — HR has no use for a fake card in a tool that
 * issues real ones, and it was one more thing to explain. It stays here,
 * and on #example, because the golden-render test drives it: that test is
 * the thing that notices when a change moves a pixel of the printed card,
 * and it needs one card it can reproduce exactly.
 */
async function loadExample() {
  const photo = ASSETS.example ? await processPhotoUrl(ASSETS.example).catch(() => null) : null;
  for (const k of NAME_PARTS) { state[k] = EXAMPLE[k] ?? ''; $(`f-${k}`).value = state[k]; }
  state.role1 = { pick: EXAMPLE.title, custom: '' };
  state.role2 = { pick: '', custom: '' };
  state.showRole2 = false;
  state.seq = EXAMPLE.seq; state.year = THIS_YEAR;
  $('f-seq').value = state.seq;
  state.reissue = false; refreshDuplicate();
  state.photo = photo?.check.ok ? photo : null; state.photoError = ''; state.photoFix = '';
  state.adjust = { dx: 0, dy: 0, zoom: 1 };
  state.signature = EXAMPLE.signature;
  pad.setValue(EXAMPLE.signature);
  state.touched = { last: false, first: false, middle: false, suffix: false, title: false, title2: false, seq: false, photo: false, signature: false };
  state.example = true; state.side = 'front';
  goStep(LAST_STEP, { focus: false, force: true });   // the example is complete: show the finished card
}
if (location.hash === '#example') addEventListener('load', loadExample);

// #map downloads config.js's own geometry as a colour-coded picture.
// Nothing in the app reads one any more — the map is dragged on screen
// now — but it is still how the zones are looked at as a whole, and it
// is what test/map.mjs feeds back in to prove the mask path reproduces
// the card. Maintainers only; there is no button for it.
if (location.hash === '#map') addEventListener('load', () => {
  mapFromConfig().toBlob((blob) => downloadBlob('template-map.png', blob), 'image/png');
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
  // cancelling "Add another ID" leaves the form blank and the queue full,
  // so the card on screen is not always part of the job
  const probs = problems();
  const hasCurrent = Object.keys(probs).every((k) => !probs[k]);
  if (hasCurrent) {
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
  }
  const cards = jobCards();                 // after review.entry: the current card carries it
  const jobSize = Math.max(1, cards.length);
  const only = jobSize === 1 ? cards[0] : null;
  if (jobSize > 1 && state.print.layout !== 'single') {
    review.fileName = `apg-ids-${jobSize}-${new Date().toISOString().slice(0, 10)}.pdf`;
    review.title = `Alpha Premier IDs — ${jobSize} cards`;
  } else if (only) {
    review.fileName = pdfFileName(only.entry.year, only.entry.seq, only.entry);
    review.title = `${only.id} ${only.label}`;
  }
  renderPrint();

  $('step-check-h').textContent = hasCurrent ? 'Is everything correct?' : 'Ready to print';
  $('review-sub').hidden = hasCurrent;
  $('review-sub').textContent = hasCurrent ? ''
    : `Printing the ${jobSize} card${jobSize === 1 ? '' : 's'} already in this job.`;

  // a plain summary of the card on screen, each line editable in one click
  const summary = $('summary');
  summary.innerHTML = '';
  summary.hidden = !hasCurrent;
  if (hasCurrent && review.front.width) {
    // the last step shows the whole sheet, where a card is too small to
    // tell one face from another; this is the only readable look at it
    const fig = document.createElement('div');
    fig.className = 'summary-card';
    const img = document.createElement('img');
    img.width = 132; img.height = Math.round(132 * review.front.height / review.front.width);
    img.alt = 'The front of the card as it will print';
    img.src = review.front.toDataURL('image/png');
    fig.append(img);
    summary.append(fig);
  }
  const rows = !hasCurrent ? [] : [
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
      state.dupLocal = issued[issued.length - 1] ?? review.entry;
      for (const entry of issued) Register.noteShared(entry);   // the next card sees this one at once
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
  state.photo = null; state.photoError = ''; state.photoFix = ''; state.signature = null;
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
  const p = problems();
  if (Object.keys(p).some((k) => p[k])) { goStep(0); return; }   // nothing to bank yet
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

/** Anything at all typed, picked or uploaded into the card on screen? */
function startedCard() {
  return NAME_PARTS.some((k) => state[k].trim()) || state.seq.trim()
    || !!state.role1.pick || !!state.photo || hasInk(state.signature);
}

/**
 * Give up on the card being filled in and go back to the print. The queue
 * and the printing options are left exactly as they were, so this undoes
 * "Add another ID" rather than the whole job.
 */
function cancelAdding() {
  if (!state.queue.length) return;
  if (startedCard() && !confirm('Discard the ID you have started? The cards already in this print are kept.')) return;
  clearCard({ keepQueue: true });
  // the card on screen is blank now, so the cap would send us to step 1:
  // but the print this returns to is already made and waiting
  goStep(LAST_STEP, { focus: false, force: true });
  window.scrollTo({ top: 0, behavior: 'smooth' });
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
  $('copies-down').addEventListener('click', () => nudgeCopies(-1));
  $('copies-up').addEventListener('click', () => nudgeCopies(1));
  for (const [id, mode] of [['lay-pairs', 'pairs'], ['lay-duplex', 'duplex']]) {
    $(id).addEventListener('click', () => setLayout(mode));
  }
}

/** Switch between one sheet of pairs and two sides of the paper. */
function setLayout(mode) {
  if (state.print.layout === mode) return;
  state.print.layout = mode;
  state.print.page = 0;
  state.print.copies = null;          // the two layouts hold different numbers
  writeLayout(mode);
  renderPrint();
  render();                           // the caption under the sheet changes too
}

/** Step the copies box, starting from whatever it is showing now. */
function nudgeCopies(by) {
  const plan = currentPlan();
  const max = plan ? plan.cols * plan.rows * 20 : 200;
  state.print.copies = Math.max(1, Math.min(max, printCopies(plan) + by));
  renderPrint();
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
    ? (onCheck ? 'All of these print together, and are issued when you download.'
               : 'Finish this card and they all go on one sheet.')
    : '';
  $('btn-cancel-add').hidden = !(state.queue.length && !onCheck);
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
  $('preview-caption').textContent = `${side}${side ? 'page' : 'Page'} ${state.print.page + 1} of ${pages.length} · ${state.print.paper}`;
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
    $('lay-pairs').classList.toggle('on', plan.mode === 'pairs');
    $('lay-duplex').classList.toggle('on', plan.mode === 'duplex');
    $('lay-pairs').setAttribute('aria-selected', plan.mode === 'pairs');
    $('lay-duplex').setAttribute('aria-selected', plan.mode === 'duplex');
    $('flip-opts').hidden = plan.mode !== 'duplex';
    $('f-copies').max = String(per * 20);
    if (document.activeElement !== $('f-copies')) $('f-copies').value = String(copies);
    $('copies-down').disabled = copies <= 1;
    $('copies-up').disabled = copies >= per * 20;
    $('field-copies').hidden = false;
    const sheets = Math.ceil(total / per);
    const pagesPerSheet = plan.mode === 'pairs' ? 1 : 2;
    const turned = plan.turned ? ', turned on their side' : '';
    const orient = plan.landscape ? 'landscape' : 'portrait';
    $('field-copies').querySelector('.field-label').textContent =
      jobCards().length > 1 ? 'Copies of each card' : 'Copies of this card';
    // how many fit, and only the sheet count when it is more than one.
    // How to cut and assemble is said once, under the sheet itself.
    $('print-note').textContent = `${per} cards per ${state.print.paper} sheet`
      + (sheets > 1 ? ` · ${total} cards, ${sheets} sheets` : '')
      + '.';
  }
  if (review.fileName) $('review-file').textContent = printFileLine();
  schedulePreview();
}

/** Just how much paper this will be. Millimetres and DPI are not a
 *  decision anybody makes here, and the file name arrives at save time. */
function printFileLine() {
  const plan = currentPlan();
  if (!plan) return '2 pages';
  const cards = jobCards().length;
  const sheets = Math.ceil((cards * printCopies(plan, cards)) / (plan.cols * plan.rows));
  const pages = plan.mode === 'pairs' ? sheets : sheets * 2;
  return `${pages} page${pages === 1 ? '' : 's'} of ${state.print.paper}`;
}

$('btn-add-another').addEventListener('click', addAnother);
$('btn-cancel-add').addEventListener('click', cancelAdding);
$('prev-page').addEventListener('click', () => { state.print.page--; drawPrintPreview(); });
$('next-page').addEventListener('click', () => { state.print.page++; drawPrintPreview(); });

/* ---- the tool rail ------------------------------------------------
   The register, the printer setting and the card design used to be
   collapsed panels stacked under the form, which put them
   off the bottom of the screen and made them read as steps of the card
   being made. They are none of those things: they are occasional, and
   they belong beside the work rather than under it. One opens at a
   time, because two of them at once is the stack again.
   ------------------------------------------------------------------ */

const TOOLS = ['register', 'printer', 'design'];
const TOOL_TITLE = {
  register: 'Issued IDs',
  printer: 'Printer setting',
  design: 'Card design',
};

function openTool(name) {
  state.tool = state.tool === name ? null : name;
  renderTools();
  if (state.tool) $('drawer').scrollTop = 0;
}

function renderTools() {
  // the design can only be changed before an ID is started
  const designOK = designAllowed();
  $('rail-design').hidden = !designOK;
  if (!designOK && state.tool === 'design') state.tool = null;

  /* Editing the map and having the Card design drawer open are the same
     state, and this is where that is enforced rather than in each of the
     handlers that can close it.
     The Done button lives in that drawer, and the form stands down while
     the boxes are being dragged. So any route that closed the drawer
     without going through Done — the x, Escape, clicking another tool in
     the rail — left the boxes on the card, the form gone and no button
     anywhere to finish with. The only way out was to reload the app and
     lose the work. */
  if (TemplateEdit.open && state.tool !== 'design') stopAdjusting();
  document.body.classList.toggle('adjusting', TemplateEdit.open);

  $('drawer').hidden = !state.tool;
  if (state.tool) {
    const n = state.tool === 'register' ? Register.all().length : 0;
    $('drawer-title').textContent = n ? `${TOOL_TITLE.register} (${n})` : TOOL_TITLE[state.tool];
  }
  for (const name of TOOLS) {
    const on = name === state.tool;
    $(`tool-${name}`).hidden = !on;
    $(`rail-${name}`).classList.toggle('on', on);
    $(`rail-${name}`).setAttribute('aria-expanded', String(on));
  }
}

for (const name of TOOLS) $(`rail-${name}`).addEventListener('click', () => openTool(name));
$('drawer-close').addEventListener('click', () => { state.tool = null; renderTools(); render(); });
addEventListener('keydown', (e) => {
  if (e.key === 'Escape' && state.tool) { state.tool = null; renderTools(); render(); }
});

/* ---- the card design ---------------------------------------------
   Moving the map, or swapping the artwork, changes where everything on
   the card belongs, so both are offered on the first step of a blank
   card and nowhere else: once a photo has been placed against one map,
   the placement is already wrong for another.
   ------------------------------------------------------------------ */

let builtInTemplates = null;

/** Is there a card in progress that a change of design would spoil? */
function designAllowed() {
  return state.step === 0 && !startedCard() && !state.queue.length;
}

function renderDesign() {
  const allowed = designAllowed();
  if (!allowed && TemplateEdit.open) stopAdjusting();
  $('design-name').textContent = TemplateStore.name;
  $('btn-tpl-map').hidden = TemplateEdit.open;
  $('design-map-note').hidden = TemplateEdit.open;
  $('design-art').hidden = TemplateEdit.open;
  $('design-actions').hidden = !TemplateStore.isCustom || TemplateEdit.open;
  $('design-adjust').hidden = !TemplateEdit.open;
  $('btn-tpl-use').disabled = !($('f-tpl-front').files[0] && $('f-tpl-back').files[0]);
  $('tpl-overlay').hidden = !(TemplateEdit.open && allowed && state.side === 'front');
}

function fileToImage(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onerror = () => reject(new Error(`Could not read ${file.name}.`));
    reader.onload = () => loadImage(reader.result).then(
      resolve,
      () => reject(new Error(`${file.name} is not a picture the app can open.`)),
    );
    reader.readAsDataURL(file);
  });
}

async function useUploadedDesign() {
  const msg = $('design-msg');
  const frontFile = $('f-tpl-front').files[0];
  const backFile = $('f-tpl-back').files[0];
  if (!frontFile || !backFile) return;
  msg.textContent = 'Reading the artwork…';
  try {
    const [front, back] = await Promise.all([fileToImage(frontFile), fileToImage(backFile)]);
    const name = frontFile.name.replace(/\.[^.]+$/, '').replace(/[-_]+/g, ' ').trim() || 'Uploaded design';
    const result = await TemplateStore.adopt({ name, frontImage: front, backImage: back });
    if (result.error) { msg.textContent = result.error; return; }

    state.templates = TemplateStore.templates;
    // start reading the register now rather than on the first keystroke
    if (Register.shared) Register.loadIndex().then(refreshDuplicate).then(render).catch(() => render());
    TemplateStore.save();
    msg.textContent = TemplateStore.note || 'In use. Check the map against the new artwork.';
    showContrastWarnings(result.warnings);
    // the map is wherever it was, which is unlikely to suit new artwork
    startAdjusting();
  } catch (err) {
    msg.textContent = err.message;
  }
}

/**
 * Text that will not read against the new artwork.
 *
 * Four near-identical paragraphs is how this used to be said, and it read
 * as a wall of failure for what is usually one mistake. One sentence
 * names the fields and the fix; the ratios are there for whoever wants
 * to take them back to the designer.
 */
function showContrastWarnings(warnings) {
  const box = $('design-warn');
  box.hidden = !warnings.length;
  if (!warnings.length) return;
  const names = warnings.map((w) => w.label);
  const list = names.length > 1 ? `${names.slice(0, -1).join(', ')} and ${names.at(-1)}` : names[0];
  $('design-warn-head').textContent =
    `The ${list} would be hard to read on this artwork — put a darker shape behind ${warnings.length > 1 ? 'those boxes' : 'that box'}.`;
  $('design-warn-list').textContent = '';
  for (const w of warnings) {
    const li = document.createElement('li');
    li.textContent = `${w.label}: prints in ${w.ink}, contrast ${w.ratio}:1 against the artwork (needs ${w.need}:1).`;
    $('design-warn-list').append(li);
  }
  box.open = false;
}

/**
 * The map as it was when the editor opened — what Cancel puts back.
 *
 * Only the boxes, not the artwork. Artwork cannot be changed while the
 * editor is open (the upload section is hidden), so anything loaded this
 * session was loaded before it, and undoing that is what "Use the
 * built-in design" is for. Copying the boxes alone also keeps this cheap:
 * the artwork is two base64 PNGs and cloning those on every open would
 * cost megabytes for nothing.
 */
let editSnapshot = null;

function startAdjusting() {
  editSnapshot = TemplateStore.custom
    ? JSON.parse(JSON.stringify({ boxes: TemplateStore.custom.boxes, photoRect: TemplateStore.custom.photoRect }))
    : null;                        // nothing was overridden when we opened
  // nothing is committed yet: the first drag is what creates an override
  state.side = 'front';
  state.tool = 'design';
  TemplateEdit.show();
  updateDesignReadout();
  render();
}

function stopAdjusting() {
  TemplateEdit.hide();
  if (TemplateStore.isPristine) TemplateStore.clear();   // looked, moved nothing
  else TemplateStore.save();
  $('tpl-overlay').hidden = true;
}

/** Put the map back as it was when the editor opened, and close it. */
function cancelAdjusting() {
  if (!editSnapshot) {
    TemplateStore.clear();         // there was no override before: leave none
  } else {
    TemplateStore.beginEditing();
    TemplateStore.custom.boxes = editSnapshot.boxes;
    TemplateStore.custom.photoRect = editSnapshot.photoRect;
    TemplateStore.apply();
  }
  stopAdjusting();
  showContrastWarnings(TemplateStore.warnings());
  $('design-msg').textContent = 'Left as it was.';
  render();
}

function updateDesignReadout() {
  $('design-readout').textContent = TemplateEdit.open ? TemplateEdit.readout() : '';
}

function resetDesign() {
  if (!confirm('Go back to the card the app was built with — artwork and map?')) return;
  stopAdjusting();
  TemplateStore.clear();
  showContrastWarnings([]);
  state.templates = builtInTemplates;
  for (const id of ['f-tpl-front', 'f-tpl-back']) $(id).value = '';
  $('design-msg').textContent = 'Back to the built-in design.';
  render();
}

TemplateEdit.mount($('tpl-overlay'), () => { drawPreview(); updateDesignReadout(); });
$('btn-tpl-use').addEventListener('click', useUploadedDesign);
$('btn-tpl-map').addEventListener('click', startAdjusting);
$('btn-tpl-cancel').addEventListener('click', cancelAdjusting);
// Closing the drawer another way keeps the changes rather than discarding
// them: the preview has been showing the result the whole time, and
// throwing away work on a stray click is the worse of the two mistakes.
// Cancel is right there for the other direction.
$('btn-tpl-done').addEventListener('click', () => {
  stopAdjusting();
  showContrastWarnings(TemplateStore.warnings());     // the boxes may have been moved somewhere darker
  $('design-msg').textContent = 'Saved on this computer.';
  render();
});
$('btn-tpl-defaults').addEventListener('click', () => { TemplateStore.resetBoxes(); TemplateEdit.refresh(); drawPreview(); updateDesignReadout(); });
$('btn-tpl-reset').addEventListener('click', resetDesign);
for (const id of ['f-tpl-front', 'f-tpl-back']) $(id).addEventListener('change', render);

/* ---- updates (desktop app only) -----------------------------------
   The desktop app looks for a newer release on GitHub once at start-up
   and whenever "Check for updates" is pressed. Installing closes the app
   and opens the new version, so a card in progress is asked about first.
   Releases are signed; the app refuses an installer that is not.
   ------------------------------------------------------------------ */

const update = { state: 'idle', info: null, error: '', percent: 0 };

function renderUpdate() {
  const box = $('update');
  box.hidden = !Desktop.available;
  if (!Desktop.available) return;
  box.dataset.state = update.state;
  const btn = $('btn-update'), status = $('update-status');
  btn.disabled = update.state === 'checking' || update.state === 'installing';
  const v = update.info?.version;
  btn.textContent = {
    idle: 'Check for updates',
    checking: 'Checking for updates…',
    current: 'Check for updates',
    available: `Install version ${v} and restart`,
    installing: `Installing version ${v}… ${update.percent}%`,
    error: 'Check for updates',
  }[update.state];
  status.textContent = {
    idle: '',
    checking: '',
    current: 'Up to date.',
    available: `Version ${v} is available.`,
    installing: 'The app will close and reopen.',
    error: update.error,
  }[update.state];
}

async function checkForUpdate({ quiet = false } = {}) {
  if (update.state === 'checking' || update.state === 'installing') return;
  update.state = 'checking'; renderUpdate();
  try {
    update.info = await Desktop.checkUpdate();
    update.state = update.info ? 'available' : 'current';
  } catch (err) {
    update.state = quiet ? 'idle' : 'error';
    update.error = 'Could not check for updates. The computer may be offline, or GitHub has no update information yet. Try again later.';
    console.warn('update check failed:', err);
  }
  renderUpdate();
}

async function installUpdate() {
  if (startedCard() || state.queue.length) {
    if (!confirm(`Install version ${update.info.version} now? The app will close and reopen, and the ID you are working on will be lost. Download it first if you need it.`)) return;
  }
  update.state = 'installing'; update.percent = 0; renderUpdate();
  try {
    await Desktop.installUpdate((p) => { update.percent = p; renderUpdate(); });
  } catch (err) {
    update.state = 'error';
    update.error = `The update could not be installed: ${String(err)}`;
    renderUpdate();
  }
}

$('btn-update').addEventListener('click', () => (update.state === 'available' ? installUpdate() : checkForUpdate()));

/* ---- boot -------------------------------------------------------- */

new ResizeObserver(drawPreview).observe(previewBox);

$('app-version').textContent = `${APP_NAME} ${APP_VERSION}`;
renderSignGuide();
renderPenStatus();
refreshPens();
renderUpdate();
// look once at start-up, quietly: no network is not worth a red line
if (Desktop.available) setTimeout(() => checkForUpdate({ quiet: true }), 4000);
$('brand-logo').src = ASSETS.logo;
buildPrintControls();
const restored = restoreDraft();
if (restored) $('restored-note').hidden = false;

Promise.all([loadTemplates(), loadCardFonts()])
  .then(async ([templates]) => {
    builtInTemplates = templates;
    TemplateStore.builtIn = templates;
    // a design put in earlier is still the one in use
    try { await TemplateStore.restore(); } catch { /* fall back to the built-in */ }
    state.templates = TemplateStore.templates;
    // start reading the register now rather than on the first keystroke
    if (Register.shared) Register.loadIndex().then(refreshDuplicate).then(render).catch(() => render());
    // Only now: whether a name or a job title fits the card cannot be
    // answered without the fonts, and until it is answered an unfittable
    // name counts as finished. Choosing the step before this point is
    // what used to open step 4 over a broken step 1.
    if (restored) goStep(furthestStep(), { focus: false });
    if (state.step === LAST_STEP) buildReview();
    render();
  })
  .catch((err) => { state.loadError = `${err.message} Reload the page to try again.`; render(); });

render();
