# Plan: Recolour the app to the PAL GOLF palette

| | |
|---|---|
| **Status** | In progress: frontend, backend and docs done; app icons wait on client artwork |
| **Last updated** | 2026-11-11 |
| **Related** | Login redesign (2026-11-11), `DESIGN.md`, `PRODUCT.md` |

## Goal

Every page uses the palette of the 78th PAL Interclub Golf Tournament 2027 poster, as the login
page already does. Today the rest of the app still wears the teal placeholder theme.

This file stands in for a GitHub issue: issues are disabled on the repository.

## Decisions

| # | Decision |
|---|---|
| 1 | The palette is the login page's: navy `#051f63`, PAL blue `#0f5fb8` for actions and focus, red `#e02a2a` to gold `#e1b115` kept for one accent (progress), not for buttons. |
| 2 | The login page's `--pg-*` values move to app-wide tokens in `styles.scss`; the login-scoped copies are then removed. |
| 3 | App icons and favicon need artwork from the client (a logo or mark separate from the poster). No PAL logo is redrawn. |
| 4 | Filled buttons are solid PAL blue (no gradient). The header runs navy to PAL blue; the footer is navy on every page. |
| 5 | "Progress" for the red-to-gold accent means Material progress bars, the event-card capacity bar and the login stroke. |
| 6 | Green, amber and red status colours are unchanged: they carry state, not brand. |
| 7 | The other auth pages take the login page's navy ground through `.auth-shell`. |

## Open questions

- Does the client supply a square app icon, or is a plain navy tile with "PAL GOLF" acceptable?
- ~~Do emails take the new colour too?~~ Yes (2026-11-11).

## Steps

### Frontend 🚧 In progress

- [x] Material theme primary in `frontend/web/src/styles.scss` (`primary: #0d9488`) and the
      hard-coded teal and green values in that file (brand gradient, buttons, chips, progress bar).
- [x] Header, navigation and footer in `frontend/web/src/app/app.scss`; drop the login-only
      footer override once the footer is navy everywhere.
- [x] Other auth pages that share `.auth-shell` and `.auth-card` (register, verify, invite).
- [x] `theme-color` in `frontend/web/src/index.html` and `theme_color` / `background_color` in
      `frontend/web/public/manifest.webmanifest`.
- [ ] App icons in `frontend/web/public/icons/` and `favicon.ico`. `icon.svg` is recoloured (navy
      to PAL blue); the PNG icons still need client artwork.
- [x] Component styles under `frontend/web/src/app/` (calendar, mail planner, users, teams,
      event pages, invitation composer, scanner, home).
- [x] Voucher image colours in `frontend/web/src/app/core/qr.util.ts` (`#1a3d7c`).

### Backend ✅ Done (2026-11-11)

- [x] Email accent in `backend/EventRegistration.Api/Services/EmailTemplates.cs`, the mail-plan
      reminder note, the public pass page header and the default meal-stub colour.

### Docs ✅ Done (2026-11-11)

- [x] Replace the teal tokens in `DESIGN.md` and remove "prototype placeholder" wording in
      `PRODUCT.md`.

## Change log

| Date | Change |
|---|---|
| 2026-11-11 | Plan created as the follow-up to the login redesign. |
| 2026-11-11 | Frontend recoloured and docs updated. Decisions 4 to 7 added. Open: PNG app icons, email accent. |
| 2026-11-11 | Emails, the public pass page and the default meal-stub colour recoloured. Open: PNG app icons. |
