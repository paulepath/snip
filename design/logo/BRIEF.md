# Snip logo brief

Snip is a screenshot tool for Windows (D:\code\snip, README.md): Print Screen freezes the screen, you drag a region,
and the shot opens in an editor with arrows, boxes, pen, text, highlighter, pixelate and numbered steps. It sits in
the tray. It also takes shots of the Pi Dash, a wall display.

It belongs to a family of the same author's apps, which share a look:
- **Total Git** (D:\code\total-git, site/ and design/): the app Snip's colours come from.
- **Panehouse** (D:\code\panehouse, site/img/mark.svg and wordmark.svg, design/logo): navy layers, amber/violet/green.
- Snip's palette (src/Snip/Palette.cs), dark / light: backdrop #121417 / #E9ECEF, text #E6E8EB / #1F2328,
  accent #5AA9F2 / #0969DA, success #4CC38A / #1A7F37. The editor's red arrow is #F87171.
- Today's app icon (src/Snip/Assets/snip.png): a blue rounded square, white crop corners, a red arrow. You may keep,
  refine or replace that idea.

## Where it will be used

- The GitHub README header, on GitHub's light and dark themes.
- A product website later, laid out like panehouse.e-path.co.uk and totalgit.e-path.co.uk (dark page, hero with the
  mark and wordmark, favicon, og image).
- Probably the app and tray icon, so the mark must still read at 16 px.

## What to make: 3 distinct concepts

Each concept is a different idea, not three colourways of one. For each, in
`design/logo/concepts/<you>/<n>-<short-name>/` (`<you>` is claude, codex or agy; `<n>` is 1, 2, 3):

| File | What |
|---|---|
| `mark-dark.svg` | the mark alone, square viewBox, for dark backgrounds |
| `mark-light.svg` | the mark alone, for light backgrounds |
| `lockup-dark.svg` | mark + the word "Snip", horizontal, for dark backgrounds |
| `lockup-light.svg` | the same for light backgrounds |
| `README.md` | 2-4 sentences: the idea, and why it fits Snip |

Rules:
- Hand-written, self-contained SVG: no raster images, no external fonts or links. Transparent background.
  For the wordmark use a system font stack (`font-family="Segoe UI, Inter, Helvetica, Arial, sans-serif"`) or,
  better, draw the letters as paths.
- Check the mark at 16, 32 and 256 px in your head (or render it): no hairlines, no detail that vanishes.
- Don't copy another product's logo. Don't touch anything outside your own folder.
