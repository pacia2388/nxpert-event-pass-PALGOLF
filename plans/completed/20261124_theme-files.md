# Theme files: theme.palgolf.json / theme.nxpass.json

Status: ✅ Done (2026-11-24) — frontend unit tests and production build pass; API tests need LocalDB
(run locally); e2e not run

The web app fetched its look from `GET /api/theme` before booting. When the API answered slowly
(busy, starting), the 5 s wait ran out and the page came up PALGOLF although the server was set to
NXPass. Replace the single server-only `theme.json` with two shipped theme files, one per look, in
both the API and the web app; each side's settings name the file to use, so the web app never waits
on the API for its look. Each theme's icons, login image and display font now come from its file.

## Decisions

| Item | Decision |
|------|----------|
| Files | `theme.palgolf.json`, `theme.nxpass.json` (full themes, same content) in `backend/EventRegistration.Api/` and `frontend/web/public/` |
| API picks | `"theme"` in appsettings (same key as the web app) (default `theme.palgolf.json`; read at start-up) |
| Web picks | `theme` in `appSettings.json`; fetched from the web app itself, never from the API |
| Published | Yes, both files on both sides. A custom look goes in a new file name, which a deploy never touches |
| `theme.json` | Dropped (no longer read, git-ignored or excluded from publish) |
| Missing / broken file | PALGOLF (API logs a warning, web app logs to the console) |
| Assets | `public/themes/palgolf/`, `public/themes/nxpass/`: favicon, tab icon, home-screen icon, login image |
| Font | `fonts.stylesheet` (https, e.g. Google Fonts) loaded by the web app; Montserrat no longer in `index.html` |
| Not themed | PWA manifest icons (`icons/`) |
| `GET /api/theme` | Kept: shows the API's look (emails, pass page); the web app no longer calls it |

## Steps

### Backend ✅ Done (2026-11-24)

- [x] Ship `theme.palgolf.json` / `theme.nxpass.json`; `"theme"` in `appsettings.json`
- [x] Drop the `theme.json` publish exclusion and git-ignore entry
- [x] `customised` only when a value differs from the base (full files are not "customised")
- [x] `fonts.stylesheet` and `logos.appleTouchIcon` in the model and resolver; new asset paths
- [x] Test: shipped files read back as the built-in themes, without warnings; web copies identical

### Frontend ✅ Done (2026-11-24)

- [x] Copy the two files to `public/`; `theme` in `appSettings.json` (dev and alt)
- [x] Assets moved to `public/themes/<name>/`; service worker caches `/themes/**` and `theme.*.json`
- [x] `core/theme.ts` loads the named file (same site), icons, home-screen icon and font
- [x] Settings → Theme names the file
- [x] Unit and e2e tests updated

### Docs ✅ Done (2026-11-24)

- [x] `documentation/theming.md`, `deployment.md`, rules, deploy-iis and admin-guide skills

## Change log

| Date | Change |
|------|--------|
| 2026-11-24 | Plan written |
| 2026-11-24 | Per-theme asset folders, font stylesheet and home-screen icon added to the theme files |
| 2026-11-24 | Done |
| 2026-11-24 | API setting renamed `Theme:Path` → `theme`, the web app's key |
| 2026-11-24 | Settings → Theme section and the theme-template endpoint removed (dev-only; copy a shipped file instead) |
| 2026-11-24 | `index.html` no longer defaults to PALGOLF icons, name and bar colour; the last theme's are remembered and set before the app loads |
