# Menu Config (application-wide on/off)

Status: ✅ Done (2026-11-22) — implemented and tested (unit + e2e).

Each client should be able to switch whole menus (modules) on or off for everyone, from a JSON
file, without a rebuild. This is not per user: roles still decide who sees an enabled menu; the
file only removes menus for all users.

## What exists today

| Place | What it does |
|-------|--------------|
| `pages/home/home.ts` | Hard-coded launcher tiles per role (admin, merchant, participant, confirmer); some tiles commented out to "disable" them |
| `app.html` (header user menu) | **My Account** (not merchants), **Settings** (admins) |
| `app.routes.ts` + `core/guards.ts` | Role guards only; a hidden tile's page is still reachable by URL |
| `core/config.ts` | Loads `public/appSettings.json` at boot (runtime, no rebuild) — the pattern to reuse |
| `guards.ts` `homeFor` | Merchants land on `/merchant/scan`, everyone else on `/home` |

## Menus

| Id | Tile / link | Routes it covers | Roles |
|----|-------------|------------------|-------|
| `dashboard` | Dashboard | `admin/dashboard` | Admin |
| `calendar` | Calendar | `calendar` | Admin, Participant |
| `mailPlanner` | Mail Planner | `mail-planner` | Admin |
| `mealScanner` | Meal Scanner | `merchant/scan` | Admin, Merchant |
| `users` | Users | `admin/users` | Admin |
| `teams` | Teams | `admin/teams` | Admin |
| `myTeam` | My Team | `team` | Participant, Captain |
| `voucher` | Voucher | `voucher` | Participant, Captain |

Never configurable (always on): Home, Login, Register, Verify, public event page (`e/:slug`),
invite (`r/:token`), **My Account** and **Settings** (header menu), and the core event menus —
this is an event-management app (decided 2026-11-22):

| Always on | Routes |
|-----------|--------|
| Events (admin) | `admin/events`, `admin/events/new`, `admin/events/:id`, `admin/events/:id/edit` |
| Check-in scanner | `scanner/:eventId`, `scanner/:eventId/tv` |
| Confirm Registrations | `admin/events` (confirmer view) |
| Browse Events | `events` |
| My Events (Home tile restored) | `my-events` — users land here after registering, verifying, from notifications and Browse Events |

An entry for any always-on menu in the file is treated as an unknown id: ignored with a console
warning.

## Decisions

| Item | Decision |
|------|----------|
| File | `frontend/web/public/menu.json`, next to `appSettings.json`; read at runtime, no rebuild |
| Shape | `{ "menus": { "dashboard": true, "voucher": false, … } }` — one boolean per menu id |
| Default | A missing id = **enabled**, so a new menu shows until a client turns it off. A missing file = everything enabled (app still boots, console warning) |
| Bad file | Invalid JSON or a non-boolean value: app still boots with everything enabled and logs a console error (the menu file must never lock users out) |
| Unknown id | Ignored with a console warning (catches typos) |
| Loading | `loadMenus()` in `core/config.ts`, run next to `loadAppSettings()` in `main.ts` (same `cache: 'no-cache'`); a module-level set read synchronously by `menuEnabled(id)` in the launcher and guards |
| Launcher | `home.ts` gives each tile its menu id and filters out disabled ones. Category tabs already hide when empty. My Events tile restored (always on) |
| Header | No change: **My Account** and **Settings** are always on (decided 2026-11-22) |
| Direct URLs | New `menuGuard('<id>')` added to each covered route (before the role guard); a disabled menu redirects to `homeFor` |
| Landing | `homeFor` sends merchants to `/home` when `mealScanner` is off (the launcher then shows an empty state) |
| Empty launcher | When a user has no enabled tiles, Home shows "No modules are available." |
| Server | **Pages only** (decided 2026-11-22). The API is not changed: a signed-in user can still call a disabled module's API directly. Revisit if a client needs a module truly off |
| Deploy | Left out of the production build (`angular.json` production `assets` ignore), so a deploy never overwrites the server copy; copied once on a new install. Not secret, but it is public (decided 2026-11-22) |

## Open questions

1. ~~**API enforcement**~~ — Decided 2026-11-22: hide pages only; no API change.
2. ~~**Settings / My Account**~~ — Decided 2026-11-22: always on, not in the file.
3. ~~**One file or `appSettings.json`**~~ — Decided 2026-11-22: separate `menu.json`.
4. ~~**Events vs Confirm Registrations**~~ — Moot (2026-11-22): both are always on.
5. ~~**Calendar, Mail Planner, My Events**~~ — Decided 2026-11-22: Calendar and Mail Planner stay
   switchable. My Events became always on with a Home tile (it is part of the registration flow).

All open questions answered.

## Changes

### Frontend ✅ Done (2026-11-22)

- [x] `public/menu.json`: every id, all `true`.
- [x] `core/config.ts`: `MENU_IDS`, `menuEnabled(id)`, `loadMenus()` (tolerant of missing/bad file); `main.ts` runs it at boot.
- [x] `core/guards.ts`: `menuGuard(id)`; `homeFor` handles `mealScanner` off.
- [x] `app.routes.ts`: add `menuGuard` to each route in the Menus table.
- [x] `pages/home/home.ts`: `menu` id per tile, filter by `menuEnabled`; My Events tile restored; empty state in `home.html`.
- [x] Removed the commented-out My Events / Settings / My Account tile code (Reminders comment kept).

### Docs (same change) ✅ Done (2026-11-22)

- [x] `.claude/rules/deployment.md` + `deploy-iis` skill: keep the server's `menu.json` like `appSettings.json`.
- [x] `.claude/rules/demo-and-config.md`: mention `menu.json`.
- [x] `.claude/skills/admin-guide/reference.md`: menus can be switched off per installation.
- [x] `documentation/menu-config.md` listing ids and examples; `documentation/deployment.md` keep/restore notes.

## Test seams

- Vitest (`core/menus.spec.ts`): `config.ts` loading — missing file, bad JSON, non-boolean, unknown id, missing id = enabled.
- Vitest: `menuGuard` — enabled passes, disabled redirects to `homeFor`.
- Vitest: `home.ts` — disabled tile hidden; tab hidden when its tiles are all off; empty state.
- Playwright (`e2e/admin/menu-config.spec.ts`): a routed `menu.json` turning off `dashboard` and
  `users` removes tiles and redirects the URLs home; always-on ids in the file are ignored; the
  shipped file shows the usual tiles.

## Steps ✅ Done (2026-11-22)

- [x] Answer open questions
- [x] Config loading + `menuGuard` (unit tests first)
- [x] Launcher, header, routes (unit tests first)
- [x] Docs
- [x] Run unit and e2e tests; verify in the running app with a few menus off

## Change log

| Date | Change |
|------|--------|
| 2026-11-22 | Plan created |
| 2026-11-22 | Open question 1 answered: hide pages only, API unchanged |
| 2026-11-22 | Open question 2 answered: My Account and Settings always on; removed from the menu list |
| 2026-11-22 | Open question 3 answered: separate `menu.json` |
| 2026-11-22 | Core event menus (Events, check-in scanner, Confirm Registrations, Browse Events) always on; question 4 moot; question 5 added |
| 2026-11-22 | Question 5 answered: Calendar, Mail Planner, My Events stay switchable. Plan ready to build |
| 2026-11-22 | Implemented. My Events made always on with a Home tile (switching it off broke post-registration redirects; 3 e2e failures). 294 unit, 35 e2e tests pass |
| 2026-11-22 | `menu.json` left out of the production build so a deploy never overwrites the server copy; console message covers a missing file |
