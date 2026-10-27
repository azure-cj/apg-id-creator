/* ------------------------------------------------------------------
   Alpha Premier ID Maker — card configuration
   ------------------------------------------------------------------
   Every measurement is in millimetres from the card's top-left corner
   unless it says otherwise. Change numbers here; the drawing code in
   card.js reads them and never hard-codes a position.
   ------------------------------------------------------------------ */

const APP_NAME = 'Alpha Premier ID Maker';
const APP_VERSION = '1.9.0';          // stamped into every PDF's metadata

const CONFIG = {
  card: {
    widthMm: 54,                      // CR80 (ISO/IEC 7810 ID-1): 85.60 x 53.98 mm
    heightMm: 85.6,
    printDpi: 300, // the PDF is rendered at this resolution
  },

  // Fonts drawn on the card. The files live in assets/fonts and are
  // registered under these family names by loadCardFonts() in card.js.
  fonts: {
    name:   { family: 'ID Poppins',    weight: 700 },
    title:  { family: 'ID Montserrat', weight: 800 },
    number: { family: 'ID Arimo',      weight: 400 },
  },

  // Parts of the front template that must sit IN FRONT of the photo:
  // the dark band at the bottom and the thin gold line above it.
  // Traced from front-template.png. Points are [x, y] in mm.
  frontOutline: {
    polygons: [
      [[36.7, 57.04], [54, 57.04], [54, 85.6], [0, 85.6], [0, 65.016], [28.79, 65.016]],
      [[28.45, 59.124], [35.56, 59.124], [35.56, 59.427], [28.58, 59.427], [23.03, 65.016], [22.61, 65.016]],
    ],
  },

  // Where the photo may appear. It steps around the logo at the top
  // left and stops at the thin black edge on the right.
  photoArea: {
    polygon: [[19.5, 10.071], [53.5, 10.071], [53.5, 67.473], [17, 67.473], [17, 17.624], [19.5, 17.624]],
  },

  // Text boxes.
  //   vAlign 'bottom'   the lowest descender of the last line sits on the
  //                     box bottom; extra lines grow upward.
  //   vAlign 'top'      the cap height of the first line sits on the box
  //                     top; extra lines grow downward.
  //   vAlign 'baseline' the baseline sits on the box bottom.
  // Sizes are in points; the fitter steps down from maxSizePt to
  // minSizePt in quarter-point steps and wraps up to maxLines.
  text: {
    name: {
      box: { x: 34.4, y: 59.719, w: 18, h: 5.237 },
      font: 'name', color: '#E8CD77', align: 'center', vAlign: 'bottom',
      maxSizePt: 6, minSizePt: 4.5, maxLines: 3, lineHeight: 1, uppercase: false,
    },
    title: {
      box: { x: 34.4, y: 65.459, w: 18, h: 3.223 },
      font: 'title', color: '#FFFFFF', align: 'center', vAlign: 'top',
      maxSizePt: 4.5, minSizePt: 3.5, maxLines: 2, lineHeight: 1.1, uppercase: true,
    },
    employeeNo: {
      box: { x: 28.6, y: 78.047, w: 23, h: 1.511 },
      font: 'number', color: '#FFFFFF', align: 'right', vAlign: 'baseline',
      maxSizePt: 4, minSizePt: 3.5, maxLines: 1, lineHeight: 1, uppercase: false,
    },
  },

  // How the separate name fields are combined for the card.
  //   cardFormat  placeholders {first} {middle} {last} {suffix};
  //               runs of spaces collapse, so a missing part leaves no gap.
  //               Surname-first would be '{last}, {first} {middle} {suffix}'.
  //   middle      'initial' -> "V."   'full' -> "Villanueva"   'none'
  name: {
    cardFormat: '{first} {middle} {last} {suffix}',
    middle: 'initial',
    maxLengths: { last: 40, first: 40, middle: 40, suffix: 10 },
    // Capitalise a name that was typed without the shift key. See
    // autoCapitalise() below for the rule, and set this to false to leave
    // every name exactly as it was typed.
    autoCapitalise: true,
  },

  // Job titles offered in the dropdown. Add, remove or reorder freely —
  // this is the whole list HR sees. A title typed under "Other…" is
  // remembered on that computer and joins the list below these.
  //
  // Someone with two roles picks a second title; composeTitle() merges
  // them. When both end in the same word the leading parts are combined
  // ("IT Associate" + "Admin Associate" -> "IT/Admin Associate");
  // otherwise they are joined whole ("IT Associate / Driver").
  titles: {
    list: [
      'IT Associate',
      'Marketing Associate',
      'HR Associate',
    ],
    mergeJoiner: '/',        // between the differing parts of merged titles
    pairJoiner: ' / ',       // between two titles that share nothing
    maxRemembered: 20,
    storageKey: 'apg-id-titles',
  },

  employeeNo: {
    prefix: 'APG',
    digits: 3,                                  // 122 -> "122", 7 -> "007"
    label: 'EMPLOYEE NO: {prefix} - {year} - {seq}',
    placeholder: '___',
    yearsBack: 2,                               // year selector offers current-2 … current+1
    yearsAhead: 1,
  },

  // Printing several cards on one sheet of paper. The grid is worked out
  // from the paper and card size, trying the card both ways round and the
  // paper both ways round, and keeping whichever fits the most.
  sheet: {
    papers: {
      A4:     { label: 'A4 (210 × 297 mm)',       widthMm: 210, heightMm: 297 },
      Letter: { label: 'Letter (216 × 279 mm)',   widthMm: 216, heightMm: 279 },
      Legal:  { label: 'Legal (216 × 356 mm)',    widthMm: 216, heightMm: 356 },
      A3:     { label: 'A3 (297 × 420 mm)',       widthMm: 297, heightMm: 420 },
    },
    defaultPaper: 'A4',
    marginMm: 6,                      // most office printers cannot print nearer the edge
    gapMm: 4,                         // space between cards, room for the cutter
    markMm: 3, markOffsetMm: 1,       // crop-mark length and distance from the card
  },

  // Register of issued IDs, used to catch duplicate numbers.
  //   Every downloaded card is recorded in this browser (localStorage).
  //   To share one list across HR computers, deploy the Google Apps Script
  //   in register/ and paste its web-app URL into the app itself
  //   (Issued IDs → Shared register). It is kept per computer in
  //   localStorage under urlStorageKey.
  //
  //   `url` below is only a fallback default and should normally stay ''.
  //   That URL grants read and write access to the register to anyone who
  //   has it, so do not commit it to the repository.
  register: {
    storageKey: 'apg-id-register',
    urlStorageKey: 'apg-id-register-url',
    url: '',
    timeoutMs: 20000,       // Apps Script is slow to wake: a cold call can take 10s+
  },

  qr: {
    square: { x: 2, y: 66.567, size: 8.8 },       // white square incl. quiet zone
    content: 'https://alphapremiergroup.com/',
    errorCorrection: 'M',
    background: '#FFFFFF',
    color: '#000000',
  },

  // Placement of the uploaded cut-out. Measured from the sample card:
  // the top of the head sits at 15.4 mm, the head is centred at 36.6 mm
  // and is 15 mm wide. The body must reach at least reachY so that it
  // continues behind the dark band.
  photo: {
    headTop: 15.509,
    headCenterX: 36.6,
    headWidth: 15,
    reachY: 66.466,
    minHeadPx: 180,   // below this the face prints a little soft (warning only)
    maxFileMb: 15,
    // manual adjustment after automatic placement
    nudgeStepMm: 0.5,     // arrow buttons move this much
    zoomMin: 0.7,         // 70 % … 150 % of the automatic size
    zoomMax: 1.5,
    zoomStep: 0.05,
  },

  // The signature is trimmed to its ink, fitted inside the box without
  // stretching, centred and drawn in white. Strokes are de-jittered and
  // drawn as curves whose width runs from strokeMm (slow pen) down to
  // minStrokeMm (fast pen).
  signature: {
    box: { x: 34.9, y: 69.084, w: 17, h: 5.64 },
    color: '#FFFFFF',
    strokeMm: 0.38,
    minStrokeMm: 0.16,
  },
};

// Example data shown by the "Show an example" link.
const EXAMPLE = {
  last: 'Aureo',
  first: 'Christopher Joseph',
  middle: 'Villanueva',
  suffix: '',
  title: 'IT Associate',
  seq: '122',
  signature: {
    strokes: [
      Array.from({ length: 36 }, (_, i) => ({ x: 20 + i * 7, y: 70 + Math.sin(i / 3.2) * 26 - i * 0.6 })),
      [{ x: 92, y: 18 }, { x: 98, y: 60 }, { x: 108, y: 112 }],
      [{ x: 210, y: 92 }, { x: 290, y: 88 }, { x: 330, y: 86 }],
    ],
  },
};

// The prompt HR pastes into Gemini / ChatGPT to turn any phone photo into
// a green-screen portrait the app can cut out itself. Keep photo-prompt.txt
// in the folder in sync with this text.
const PHOTO_PROMPT = `Edit this photo for a company ID card.

1. Keep the person exactly as they are: same face, features, skin tone, hair, glasses and expression. Do not beautify, slim, age or change their identity.
2. Show them from the chest up, facing the camera, centred, with the whole head and a little space above the hair.
3. Keep their clothes as they are.
4. Replace the background with one solid, flat, pure green colour (#00FF00). No shadows, gradients, patterns or checkerboard.
5. Portrait orientation, at least 1200 pixels tall. Save as PNG.`;

/* ---- unit helpers shared by every module ------------------------- */

const PT_TO_MM = 25.4 / 72;                       // 1 pt = 0.3528 mm
const mmToPx = (mm, pxPerMm) => mm * pxPerMm;
const printPxPerMm = () => CONFIG.card.printDpi / 25.4;   // 11.81 px/mm at 300 DPI

function cardSizePx(pxPerMm) {
  return {
    w: Math.round(CONFIG.card.widthMm * pxPerMm),
    h: Math.round(CONFIG.card.heightMm * pxPerMm),
  };
}

// "EMPLOYEE NO: APG - 2026 - 122"  (seq null -> "___")
function employeeNoLine(year, seq) {
  const s = seq === null ? CONFIG.employeeNo.placeholder
                         : String(seq).padStart(CONFIG.employeeNo.digits, '0');
  return CONFIG.employeeNo.label
    .replace('{prefix}', CONFIG.employeeNo.prefix)
    .replace('{year}', String(year))
    .replace('{seq}', s);
}

// "APG-2026-122"
function employeeId(year, seq) {
  return `${CONFIG.employeeNo.prefix}-${year}-${String(seq).padStart(CONFIG.employeeNo.digits, '0')}`;
}

const collapseSpaces = (s) => s.replace(/\s+/g, ' ').trim();

/* ---- names ------------------------------------------------------- */

// "Villanueva" -> "V."   (first letter of the first word, plus a period)
function middleInitial(middle) {
  const word = collapseSpaces(middle).split(' ')[0] ?? '';
  return word ? `${word[0].toLocaleUpperCase('en-US')}.` : '';
}

/**
 * The name as it is printed on the card, from its parts.
 * parts: { last, first, middle, suffix } — any may be missing.
 */
function composeName(parts = {}) {
  const middle = CONFIG.name.middle === 'none' ? ''
    : CONFIG.name.middle === 'full' ? collapseSpaces(parts.middle || '')
    : middleInitial(parts.middle || '');
  const filled = CONFIG.name.cardFormat
    .replace('{first}', collapseSpaces(parts.first || ''))
    .replace('{middle}', middle)
    .replace('{last}', collapseSpaces(parts.last || ''))
    .replace('{suffix}', collapseSpaces(parts.suffix || ''));
  // tidy up what the missing parts left behind: double spaces, a stray
  // leading comma, a space before a comma
  return collapseSpaces(filled).replace(/\s+,/g, ',').replace(/^,\s*/, '').replace(/,$/, '');
}

/**
 * One job title from one or two roles.
 *   ('IT Associate', 'Admin Associate') -> 'IT/Admin Associate'
 *   ('IT Associate', 'Driver')          -> 'IT Associate / Driver'
 *   ('IT Associate', '')                -> 'IT Associate'
 */
function composeTitle(first, second) {
  const a = collapseSpaces(first || '');
  const b = collapseSpaces(second || '');
  if (!a || !b) return a || b;
  if (a.toLocaleLowerCase('en-US') === b.toLocaleLowerCase('en-US')) return a;

  const aw = a.split(' ');
  const bw = b.split(' ');
  let shared = 0;                      // words both titles end with
  while (shared < aw.length - 1 && shared < bw.length - 1
    && aw[aw.length - 1 - shared].toLocaleLowerCase('en-US') === bw[bw.length - 1 - shared].toLocaleLowerCase('en-US')) {
    shared++;
  }
  if (!shared) return `${a}${CONFIG.titles.pairJoiner}${b}`;
  const tail = aw.slice(aw.length - shared).join(' ');
  const headA = aw.slice(0, aw.length - shared).join(' ');
  const headB = bw.slice(0, bw.length - shared).join(' ');
  return `${headA}${CONFIG.titles.mergeJoiner}${headB} ${tail}`;
}

// For file names: letters only, accents flattened. "Dela Cruz-Santos" -> "DelaCruzSantos"
function fileSafeName(s) {
  return collapseSpaces(s || '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/[^A-Za-z]/g, '');
}

/* Suffixes that are roman numerals: "iii" is III, never "Iii". */
const ROMAN_SUFFIX = /^(i{1,3}|iv|vi{0,3}|ix|xi{0,3})$/i;

/**
 * Capitalise a name typed in lower case, for the operator who did not
 * reach for the shift key.
 *
 * It only fires when the field holds no capital at all. That one rule is
 * what makes it safe on an ID card: a name whose own spelling is not Title
 * Case — dela Cruz, van Dijk, bin Rashid, MacArthur, III — already carries
 * a capital somewhere, so it is left exactly as typed. It also means
 * nobody can get stuck: type one capital anywhere in the field and that
 * field is yours.
 *
 * Words are split on anything that is not a letter, so o'brien becomes
 * O'Brien and mary-jane becomes Mary-Jane, and the match is Unicode-aware
 * so pi\u00f1on becomes Pi\u00f1on rather than being torn apart at the \u00f1.
 */
function autoCapitalise(value, { suffix = false } = {}) {
  const v = String(value ?? '');
  if (!CONFIG.name.autoCapitalise) return v;
  if (v !== v.toLowerCase()) return v;                 // a capital: hands off
  return v.replace(/\p{Ll}+/gu, (word) => (
    suffix && ROMAN_SUFFIX.test(word)
      ? word.toUpperCase()
      : word[0].toLocaleUpperCase() + word.slice(1)
  ));
}
