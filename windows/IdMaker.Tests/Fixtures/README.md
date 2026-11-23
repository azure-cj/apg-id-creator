# Fixtures

`web-reference.json` holds values captured from the web app this one
replaces (version 1.20.0), driven in headless Chromium with its example
card loaded: the fitted text, the example photo's placement, the QR
modules from the vendored qrcode-generator, the sheet plans, and the
answers of the small naming functions.

The native app is held to these, so the card it prints is the card the
web app printed. They are a record of what was, not something to
regenerate: if a number here is ever wrong for the new app, change the
test and say why.
