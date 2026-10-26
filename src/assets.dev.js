/* ------------------------------------------------------------------
   Asset paths for DEVELOPMENT (served over HTTP from the folder root).
   build.mjs replaces this file with the same object holding base64
   data: URLs so the built HTML works offline from a double-click.
   Font pairs are [latin, latin-ext].
   ------------------------------------------------------------------ */
const ASSETS = {
  front: '../assets/front-template.png',
  back: '../assets/back.png',
  example: '../assets/example-cut-out.png',
  fonts: {
    name:   ['../assets/fonts/poppins-700-latin.woff2',    '../assets/fonts/poppins-700-latin-ext.woff2'],
    title:  ['../assets/fonts/montserrat-800-latin.woff2', '../assets/fonts/montserrat-800-latin-ext.woff2'],
    number: ['../assets/fonts/arimo-400-latin.woff2',      '../assets/fonts/arimo-400-latin-ext.woff2'],
  },
};
