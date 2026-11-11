---
name: PAL GOLF
description: Event registration and check-in, as currently implemented in the Angular app
colors:
  primary: "#0d9488"
  primary-deep: "#0f766e"
  primary-container: "#ccf2ec"
  on-primary-container: "#064e46"
  primary-soft: "#e6f6f3"
  gradient-green: "#22c55e"
  success: "#15803d"
  success-soft: "#e8f7ee"
  warning: "#b45309"
  warning-soft: "#fef4e2"
  error: "#dc2626"
  error-deep: "#b91c1c"
  error-soft: "#fdecec"
  app-bg: "#f1f7f4"
  surface: "#ffffff"
  surface-low: "#f8fbfa"
  surface-high: "#f1f6f4"
  table-header: "#f6faf8"
  neutral-soft: "#eef2f6"
  ink: "#0f172a"
  ink-secondary: "#334155"
  ink-muted: "#475569"
  outline: "#94a3b8"
  outline-button: "#cbd5e1"
  outline-variant: "#e2e8f0"
  # Login page only (the --pg-* properties on .login-shell). See "Login Page (Scoped Exception)".
  pg-navy: "#051f63"
  pg-navy-mid: "#0a2d7a"
  pg-sky: "#1a5aa8"
  pg-blue: "#0f5fb8"
  pg-blue-deep: "#0b4a94"
  pg-red: "#e02a2a"
  pg-gold: "#e1b115"
  pg-ink: "#0b1b3f"
  pg-ink-soft: "#46557a"
  pg-line: "#cdd7e8"
typography:
  headline:
    fontFamily: "Roboto, sans-serif"
    fontSize: "26px"
    fontWeight: 800
    lineHeight: 1.2
    letterSpacing: "-0.4px"
  title:
    fontFamily: "Roboto, sans-serif"
    fontSize: "22px"
    fontWeight: 700
    lineHeight: 1.27
    letterSpacing: "-0.2px"
  body:
    fontFamily: "Roboto, sans-serif"
    fontSize: "14px"
    fontWeight: 400
    lineHeight: 1.43
  label:
    fontFamily: "Roboto, sans-serif"
    fontSize: "12px"
    fontWeight: 700
    letterSpacing: "0.3px"
  # Login page only.
  login-heading:
    fontFamily: "Montserrat, Roboto, sans-serif"
    fontSize: "27px"
    fontWeight: 800
    lineHeight: 1.2
    letterSpacing: "-0.01em"
  login-details:
    fontFamily: "Montserrat, Roboto, sans-serif"
    fontSize: "13px"
    fontWeight: 600
    lineHeight: 1.35
rounded:
  xs: "8px"
  sm: "10px"
  md: "12px"
  table: "14px"
  card: "18px"
  dialog: "20px"
  pill: "999px"
spacing:
  xs: "8px"
  sm: "12px"
  md: "18px"
  lg: "22px"
components:
  button-filled:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.surface}"
    rounded: "{rounded.pill}"
  button-filled-warn:
    backgroundColor: "{colors.error}"
    textColor: "{colors.surface}"
    rounded: "{rounded.pill}"
  button-outlined:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink-secondary}"
    rounded: "{rounded.pill}"
  button-outlined-hover:
    backgroundColor: "#f3fbf8"
    textColor: "{colors.primary}"
  button-text:
    textColor: "{colors.primary-deep}"
    rounded: "{rounded.pill}"
  card:
    backgroundColor: "{colors.surface}"
    rounded: "{rounded.card}"
  field-outlined:
    backgroundColor: "{colors.surface}"
    rounded: "{rounded.md}"
  status-pill:
    backgroundColor: "{colors.neutral-soft}"
    textColor: "{colors.ink-muted}"
    typography: "{typography.label}"
    rounded: "{rounded.pill}"
    padding: "3px 10px"
  status-pill-ok:
    backgroundColor: "{colors.success-soft}"
    textColor: "{colors.success}"
  status-pill-pending:
    backgroundColor: "{colors.warning-soft}"
    textColor: "{colors.warning}"
  status-pill-closed:
    backgroundColor: "{colors.error-soft}"
    textColor: "{colors.error-deep}"
  status-pill-brand:
    backgroundColor: "{colors.primary-soft}"
    textColor: "{colors.primary-deep}"
  page-title-icon:
    backgroundColor: "#ecf8f4"
    textColor: "{colors.primary}"
    rounded: "13px"
    size: "44px"
    padding: "10px"
  # Login page only.
  login-frame:
    backgroundColor: "{colors.surface}"
    rounded: "{rounded.dialog}"
  login-button:
    backgroundColor: "{colors.pg-blue}"
    textColor: "{colors.surface}"
    rounded: "{rounded.pill}"
    height: "50px"
  login-button-hover:
    backgroundColor: "{colors.pg-blue-deep}"
  login-button-loading:
    backgroundColor: "{colors.pg-blue-deep}"
    textColor: "{colors.surface}"
  login-button-disabled:
    backgroundColor: "#dde4f0"
    textColor: "#6a7793"
  login-alt-button:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.pg-blue-deep}"
    rounded: "{rounded.pill}"
    height: "46px"
  login-alt-button-hover:
    backgroundColor: "#f0f5fc"
---

# Design System: PAL GOLF

This records the look the Angular app has today. PRODUCT.md marks the teal colour and the
Roboto/Material look on every page other than login as placeholders, so treat the system below
as a description of the incumbent, not a commitment. The login page alone follows the client's
tournament poster; it is recorded as a scoped exception in its own section at the end. The
app-wide recolour to the poster's palette is planned in `plans/pal-golf-recolour.md` and is not
built.

The app name and the footer line are runtime settings, not hard-coded: `appName` and
`footerText` in `frontend/web/public/appSettings.json`, read at boot into `BRANDING`
(`frontend/web/src/app/core/config.ts`). A missing or blank value falls back to "PAL GOLF" and
"Powered by N-PAX - NXPERT SYSTEM". Screens show the name by binding to it, never by typing it.

## Overview

**Creative North Star: "The Front Desk"**

A tidy, well-lit registration counter. Everything is labelled, status is visible at a glance,
and the tone is friendly but businesslike. White cards sit on a pale mint page under a
teal-to-green gradient header; the header and the filled buttons carry the colour, and the
working surfaces stay white.

The feel is soft and friendly: pill buttons, generously rounded cards, light layered shadows
and heavy headings. It is built on Angular Material 3 (light scheme only, density 0) with
global overrides in `frontend/web/src/styles.scss`, so every Material component picks up the
same shapes and colours.

**Key Characteristics:**
- White cards on a pale mint page, gradient header on top.
- Teal carries action; green, amber and red carry status.
- Pill buttons and pills for status; rounded rectangles for everything that holds content.
- Bold headings (700 to 800) over regular 14px body text.
- Light scheme only; no dark theme exists.

## Colors

A single teal accent with a green partner in gradients, on slate text and near-white surfaces.

### Primary
- **Counter Teal** (`primary`): filled buttons, active tabs, progress bars, icons, focus.
- **Deep Teal** (`primary-deep`): text buttons, primary outlined buttons, text on teal tints.
- **Teal Tints** (`primary-container`, `primary-soft`): selected and "registered" backgrounds.
- **Gradient Green** (`gradient-green`): only as the end stop of the brand gradient
  (`--brand-grad`, 135deg from `primary`), used on page-title text and brand accents.

### Secondary
- **Confirmed Green** (`success` on `success-soft`): confirmed, checked-in, ok.
- **Pending Amber** (`warning` on `warning-soft`): pending.
- **Closed Red** (`error`, `error-deep` on `error-soft`): errors, closed, destructive buttons.

### Neutral
- **Mint Page** (`app-bg`): the page behind every card.
- **White** (`surface`): cards, tables, dialogs, menus, fields.
- **Soft Surfaces** (`surface-low`, `surface-high`, `table-header`, `neutral-soft`): header
  bands, hover rows, neutral pills and tags.
- **Slate Ink** (`ink`, `ink-secondary`, `ink-muted`): headings, outlined-button text, secondary
  text.
- **Outlines** (`outline`, `outline-button`, `outline-variant`): field strokes, outlined button
  borders, dividers.

### Named Rules
**The State Colour Rule.** Green, amber and red mean confirmed, pending and closed. They always
appear as a dark tone on its own soft tint, never as a decorative accent.

**The White Workbench Rule.** Content surfaces are white. Material's green palette is overridden
precisely so surfaces do not pick up a tint.

## Typography

**Display Font:** Roboto (with sans-serif)
**Body Font:** Roboto (with sans-serif)

**Character:** One family throughout. Hierarchy comes from weight and size, with headings set
heavy and slightly tightened against plain regular body text.

### Hierarchy
- **Headline** (800, 26px, 1.2, -0.4px): the page title, filled with the brand gradient.
- **Title** (700 to 800, 22px, -0.2px): card titles, dialog titles, the brand name in the header.
- **Body** (400, 14px, 20px line): Material `body-medium`; most supporting text sits at
  12.5px to 13.5px.
- **Label** (700, 12px, 0.3px): table headers, status pills; buttons are 700 with 0.2px tracking.

### Named Rules
**The Heavy Heading Rule.** Headings and anything clickable are bold (700+). Regular weight is
for reading text only.

## Layout

- App shell: a sticky gradient header (84px min height, 40px side padding, 16px under 900px)
  above the page content.
- Pages open with a page title (icon tile plus heading, 22px below), then an optional toolbar row
  (12px gap, wraps, 20px below), then cards or a table.
- Card lists use an auto-fill grid: `minmax(300px, 1fr)` columns with an 18px gap.
- Responsive changes are max-width based. 600px is the main phone breakpoint; 900px and 1100px
  adjust the header and wider grids.
- The PWA is portrait-oriented; scanner and attendee screens are phone-first, admin screens are
  desktop-first.

## Elevation & Depth

Lightly lifted. Cards rest on a two-layer shadow over the mint page and gain a deeper one on
hover; overlays use progressively larger shadows. Hairline borders do as much work as the
shadows.

### Shadow Vocabulary
- **Card rest** (`--card-shadow`: `0 1px 2px rgba(15,23,42,0.04), 0 6px 20px rgba(15,23,42,0.06)`):
  every card.
- **Card hover** (`--card-shadow-hover`: `0 6px 14px rgba(15,23,42,0.08), 0 18px 40px rgba(15,23,42,0.12)`):
  clickable cards.
- **Filled button** (`0 4px 12px rgba(13,148,136,0.26)`, hover `0 6px 16px rgba(13,148,136,0.34)`):
  a teal-tinted glow; red-tinted on warn buttons.
- **Menu** (`0 12px 32px rgba(15,23,42,0.14)`) and **Dialog** (`0 24px 60px rgba(15,23,42,0.18)`).
- **Header** (`0 2px 10px rgba(13,148,136,0.18)`).

## Shapes

Two shapes only. Things you press or read as a tag are pills (999px). Things that hold content
are rounded rectangles that grow with size: 8 to 10px for small tiles and inputs inside grids,
12px for fields and menus, 14px for tables, 18px for cards, 20px for dialogs. Cards and tables
carry a faint teal hairline (`1px solid rgba(13,148,136,0.14)`); neutral dividers use
`rgba(15,23,42,0.08)`.

## Components

### Buttons
Soft and friendly: pills with bold labels.
- **Shape:** full pill (999px), set through Material button overrides.
- **Filled:** a 135deg gradient from teal to green (`#0d9488` to `#22b07d`), white text, teal
  glow. Hover darkens slightly (`brightness(0.95)`) and deepens the glow.
- **Filled warn:** solid red, white text.
- **Outlined:** 1.5px slate border, slate text; hover turns border and text teal on a `#f3fbf8`
  fill. Warn variant goes red on `error-soft`.
- **Text:** deep teal label.
- Transitions are 0.15s ease.

### Chips
- **Status pill:** 12px bold text in a pill, 3px by 10px padding. Neutral by default; `ok`,
  `pending`, `closed` and `brand`/`registered` variants follow the State Colour Rule.
- **Method tag:** a smaller 11px pill next to a status pill showing how a check-in was recorded.

### Cards / Containers
- **Corner Style:** 18px.
- **Background:** white.
- **Shadow Strategy:** card rest, card hover when clickable.
- **Border:** faint teal hairline.
- **Tables:** white, 14px corners, a pale header band with 12px bold slate labels, rows tint on
  hover.

### Inputs / Fields
- **Style:** Material outlined fields, 12px corners, white fill.
- **Focus:** Material's primary (teal) outline.
- **Error:** Material's error (red) outline with helper text.

### Navigation
- **Header:** sticky horizontal gradient (teal to light green), white text. A Home button, brand
  badge and user chip sit on translucent white "glass" tiles
  (`rgba(255,255,255,0.18)` with a `0.22` white border); hover and active go to `0.28`.
- **Tabs:** bold labels, teal active indicator, hairline under the tab bar.
- Under 900px the header tightens and the brand subtitle hides.

### Page Title
A 44px soft teal icon tile (13px corners) beside a gradient-filled 26px heading. Every page
opens with it.

### Dialogs and Menus
White, 20px and 12px corners, heavy title, 8px gap between actions.

## Do's and Don'ts

### Do:
- **Do** style Material components through the global overrides in `styles.scss` so every screen
  stays consistent.
- **Do** use `--card-shadow`, `--card-shadow-hover`, `--brand-grad`, `--tint-primary` and the
  `--mat-sys-*` tokens instead of new literals.
- **Do** show registration state with `.status-pill` and its existing variants.
- **Do** open each page with the page title pattern.
- **Do** keep pills for buttons and tags, rounded rectangles for containers.

### Don't:
- **Don't** tint content surfaces; they are white by rule.
- **Don't** use green, amber or red for decoration; they mean state.
- **Don't** add square or sharp-cornered buttons.
- **Don't** assume a dark theme; `color-scheme` is light only.

## Login Page (Scoped Exception)

`/login` is the one page that does not follow the Front Desk system. It wears the client's
tournament poster: the poster is the whole brand statement, shown beside a plain, fast sign-in
form. Everything in this section applies to that page only.

**The Login Only Rule.** The `pg-*` colours are declared as `--pg-*` custom properties on
`.login-shell` (`frontend/web/src/app/pages/login/login.scss`) and exist nowhere else. Do not
use them, Montserrat, or the navy ground on any other page until the recolour in
`plans/pal-golf-recolour.md` lands. Every other page keeps the teal tokens above.

### Colours
- **Poster Navy ground** (`pg-sky` to `pg-navy-mid` at 44% to `pg-navy`, top to bottom): the page
  behind the frame. `pg-navy` is sampled from the poster's lower band; it also backs the poster
  panel and colours the heading.
- **White card** (`surface`): the sign-in card and its fields stay white, including autofilled
  fields.
- **PAL Blue** (`pg-blue`, hover and loading `pg-blue-deep`): the Sign in button, the focus
  ring, the caret, and Material's primary inside the page (`--mat-sys-primary` is re-pointed to
  it, so focused fields and the password toggle turn blue instead of teal).
- **Red to Gold** (`pg-red` through `#e8742a` to `pg-gold`): the poster's arc. It appears in
  exactly one place, the signing-in progress stroke.
- **Ink and lines** (`pg-ink`, `pg-ink-soft`, `pg-line`): card text, supporting text and field
  icons, the divider and the secondary button border.

**The One Arc Rule.** Red-to-gold is the signing-in stroke and nothing else. It is never a
button, heading, border or decoration.

### Typography
- **Montserrat** (with Roboto, sans-serif) is loaded in `frontend/web/src/index.html` at 600
  and 800 only, and is used in two places: the card heading and the phone details
  band.
- **Heading** (`login-heading`: 800, 27px, 1.2, -0.01em, navy; 24px at 900px and below): "Sign in".
- **Details band** (`login-details`: 600, 13px, 1.35, white on navy): division names at 600, dates
  at 800, venue at 600, 12px, uppercase with 0.2em tracking in a pale blue (`#b8cdf2`).
- **Roboto elsewhere**: supporting line (14.5px, `pg-ink-soft`), fields, button labels (500,
  16px on Sign in, 14.5px on the secondary button), divider label (12.5px).

### Layout
- **Desktop (above 900px):** one centred frame of two columns. Left, the square poster, sized
  `min(600px, 100dvh - 150px, 54vw)` so the whole frame stays in view; right, a white card of
  equal height, 340px to 440px wide, 40px padding, content centred vertically. The page fills
  the window from top to footer, 32px by 20px padding.
- **Phone and tablet (900px and below):** stacked, frame up to 460px wide, centred when the
  window is taller than the frame and pinned to the top when it is not; page padding 16px by
  12px, card padding 28px by 24px.
- **Tall tablets (600px to 900px wide, 1000px or taller):** still stacked, but the whole square
  poster is shown.
- The frame is white with 20px corners (18px when stacked), clipped, on a deep navy shadow
  (`0 2px 6px rgba(2,10,40,0.3), 0 30px 70px rgba(2,10,40,0.45)`).
- The app header is not shown here; it only renders after sign-in.

### The Poster
- `frontend/web/public/brand/pal-interclub-2027-poster.jpg` (1200 by 1200) is supplied by the
  client and is the only brand image on the page. There is no separate logo, illustration or
  background art.
- **Whole** on desktop and tall tablets: shown square and complete.
- **Cropped** on phones: the top band only (4:3, anchored to the top: logo, title, golfer),
  with a fade from 68% down into `pg-navy`. The dates and venue the crop removes are set as
  live text on navy directly below, in the details band.
- The poster's wording is always present as text after the image. Where the whole poster shows,
  that text is visually hidden and read by screen readers; on phones the dates and venue become
  visible and only the title stays hidden, because it is still in the cropped image.

**The Whole Poster Rule.** Never crop the poster without setting what the crop removes as live
text, and never add a second brand image beside it.

### Components
- **Sign in button:** a pill (999px), 50px tall, solid `pg-blue`, white 500 16px label with a
  trailing arrow, blue glow (`0 8px 18px rgba(15,95,184,0.3)`). Hover: `pg-blue-deep`, lifts 1px,
  deeper glow, arrow slides 3px. Active: settles back.
- **Create account button:** a pill, 46px tall, white, 1.5px `pg-line` border, `pg-blue-deep`
  500 14.5px label with a leading icon. Hover: `#f0f5fc` fill, `pg-blue` border. It sits under a
  hairline divider labelled "New here?".
- **Fields:** the app's Material outlined fields (12px corners, white), each with a leading
  icon in `pg-ink-soft`; the password field has a show/hide toggle.
- **Progress stroke:** a 4px track (`#e4eaf5`) along the top edge of the card, with a
  red-to-gold bar 42% wide sweeping across it.

### States
- **Loading (signing in):** the progress stroke appears, the button label becomes "Signing in…"
  without the arrow, and the button stays in the action colour (`pg-blue-deep`, white text,
  progress cursor) so it does not read as refused.
- **Disabled (form incomplete):** grey button (`#dde4f0` fill, `#6a7793` text), no glow, default
  cursor.
- **Error:** a missing username or password shows Material's red outline with a helper line
  under the field. A failed sign-in is reported in the app's toast; the card itself does not
  change.
- **Focus:** both buttons take a 2px `pg-blue` outline offset 3px; fields take Material's
  outline in `pg-blue`. Text selection is pale blue (`#cfe0f7`) with `pg-ink` text.

### Motion
- **Frame rise:** on load the frame fades in and rises 16px over 0.7s
  (`cubic-bezier(0.16, 1, 0.3, 1)`), once.
- **Poster settle:** the poster eases from 1.06 scale to rest over 1.2s on the same curve, once.
- **Progress sweep:** 1.1s, `cubic-bezier(0.65, 0, 0.35, 1)`, repeating while signing in.
- **Button:** 0.15s ease on lift, glow, fill and arrow.
- **Reduced motion:** the rise and settle are removed, the progress stroke becomes a full-width
  static red-to-gold bar, and the Sign in button's transitions are dropped.

### Page-scoped overrides
- **Footer:** while the login page is open the app footer sits on `pg-navy` instead of the
  frosted white bar, with no blur or shadow, a faint white top border
  (`rgba(255,255,255,0.12)`) and its text in solid pale blue (`#c3d4f3`) instead of the brand
  gradient. The rule lives in `frontend/web/src/app/app.scss`
  (`.app-content:has(.login-shell) + .branding-footer`). The wording is still `footerText`.
- **Browser toolbar:** the page sets the `theme-color` meta tag to the ground's sky blue while
  it is open and restores the app's own value (teal) when it closes
  (`frontend/web/src/app/pages/login/login.ts`).
