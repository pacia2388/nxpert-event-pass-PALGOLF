# Plan: Recolour the app to the PAL GOLF palette

| | |
|---|---|
| **Status** | Not started |
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

## Open questions

- Does the client supply a square app icon, or is a plain navy tile with "PAL GOLF" acceptable?
- Do emails take the new colour too (`EmailTemplates.cs` accent `#0d9488`)?

## Steps

### Frontend

- [ ] Material theme primary in `frontend/web/src/styles.scss` (`primary: #0d9488`) and the
      hard-coded teal and green values in that file (brand gradient, buttons, chips, progress bar).
- [ ] Header, navigation and footer in `frontend/web/src/app/app.scss`; drop the login-only
      footer override once the footer is navy everywhere.
- [ ] Other auth pages that share `.auth-shell` and `.auth-card` (register, verify, invite).
- [ ] `theme-color` in `frontend/web/src/index.html` and `theme_color` / `background_color` in
      `frontend/web/public/manifest.webmanifest`.
- [ ] App icons in `frontend/web/public/icons/` and `favicon.ico`.
- [ ] Voucher image colours in `frontend/web/src/app/core/qr.util.ts` (`#1a3d7c`).

### Backend

- [ ] Email accent in `backend/EventRegistration.Api/Services/EmailTemplates.cs`, if agreed.

### Docs

- [ ] Replace the teal tokens in `DESIGN.md` and remove "prototype placeholder" wording in
      `PRODUCT.md`.

## Change log

| Date | Change |
|---|---|
| 2026-11-11 | Plan created as the follow-up to the login redesign. |
