# Theming: PALGOLF / NXPass

Status: ✅ Done (2026-11-23) — implemented and tested (API, unit, e2e, browser)

One build wears either the current look (**PALGOLF**, default) or the original look
(**NXPass**). A developer picks it with a `theme.json` in the API site folder after publishing;
publishing never outputs that file. Admins download a filled-in template from Settings. Full
reference: [documentation/theming.md](../documentation/theming.md).

## Decisions

| Item | Decision |
|------|----------|
| Where the theme lives | `theme.json` in the API site folder (`Theme:Path`), excluded from publish and git |
| How the web app gets it | Public `GET /api/theme`, read before the app boots; on failure the SCSS defaults (PALGOLF) stay |
| Template | `GET /api/settings/theme-template` (admin), the full PALGOLF theme plus `$help`; download from Settings → Theme |
| Format | `base` + optional per-key overrides; invalid values dropped with a logged warning; bad JSON → PALGOLF |
| Edits | Applied on the next request, no restart |
| Login page | Poster layout kept, recoloured; image, alt, title, lines and caption from the theme |
| Brand text | Optional `appName` / `footerText`; otherwise the configured name |
| Covered | Web app colours, fonts, layout variants, logos, tab icon, browser bar, emails, pass page, meal stubs, voucher |
| Not covered | PWA manifest, `PALGOLF-` voucher code prefix, UI import/export, themes in the database |

## Steps

### Backend ✅ Done (2026-11-23)

- [x] `Services/Theming`: definition, built-in themes, resolver with validation, store re-reading the file
- [x] `GET /api/theme` (anonymous) and `GET /api/settings/theme-template` (admin)
- [x] `theme.json` excluded from publish (`.csproj`) and git
- [x] `Branding.AppName` from the theme
- [x] Emails, pass page and meal stubs use theme colours; header logo in emails
- [x] API tests (`ThemeTests`, 10)

### Frontend ✅ Done (2026-11-23)

- [x] `core/theme.ts`: load before boot, apply CSS variables, layout variants, icons, browser bar, name
- [x] Brand literals in SCSS replaced with tokens; NXPass layout variants
- [x] Login poster, header logo, voucher colours from the theme
- [x] `layout.loginImage` `poster` / `logo`: NXPass shows its logo centred with the words, not full-bleed
- [x] NXPass icons in `public/themes/nxpass/`
- [x] Settings → Theme: name, source, template download
- [x] Unit tests (theme, login, app, settings)
- [x] Playwright specs
- [x] Browser check at 1440px and 375px in both themes (login, Home, Settings)

### Docs ✅ Done (2026-11-23)

- [x] `documentation/theming.md`, `documentation/deployment.md`
- [x] `.claude/rules/deployment.md`, `.claude/rules/demo-and-config.md`
- [x] `.claude/skills/deploy-iis`, `.claude/skills/admin-guide/reference.md`

## Follow-ups

- A per-theme PWA manifest.

## Change log

| Date | Change |
|------|--------|
| 2026-11-23 | Plan created; backend, frontend theme loading, recolour, login/header/voucher, Settings template and docs landed |
| 2026-11-23 | Playwright specs (4); browser check found the NXPass logo filling the whole login panel, so `layout.loginImage` (`poster` / `logo`) was added |
