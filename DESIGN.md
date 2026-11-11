---
name: NXPass
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
---

# Design System: NXPass

This records the look the Angular app has today. PRODUCT.md marks every brand element as a
placeholder, so treat this as a description of the incumbent, not a commitment.

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
