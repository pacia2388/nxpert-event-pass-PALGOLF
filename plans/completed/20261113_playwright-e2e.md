# Plan: Playwright end-to-end tests

| | |
|---|---|
| **Status** | ✅ Done (2026-11-13) |
| **Last updated** | 2026-11-13 |
| **Related** | `documentation/e2e-testing.md`, `.github/workflows/ci.yml` |

## Goal

Test the Angular app against the real API in a browser. Until now there were only unit tests
(Vitest in `frontend/web`, xUnit against the API).

This file stands in for a GitHub issue: issues are disabled on the repository.

## Decisions

| # | Decision |
|---|---|
| 1 | First flows: login and role routing, guest registration, admin confirm, participant My Events. |
| 2 | Playwright starts its own API (port 5188) on a throwaway LocalDB database `EventPassE2E_<run id>`, plus `ng serve` on 4300. Dev servers (5088 / 4200) and the dev database are never touched. The database is dropped after the run. |
| 3 | The API runs as environment `E2E`, not Development: `Program.cs` loads `appsettings.{env}.Local.json` after env vars, so a local Development file would override the e2e settings. Email, Viber and SMS are off, so messages land in `backend/EventRegistration.Api/outbox/`. |
| 4 | The app is pointed at the e2e API by rewriting `appSettings.json` in the browser (`page.route`), so no rebuild is needed. |
| 5 | Each test creates its own event through the API, so tests are independent and run in parallel. Sign-ins use the seeded `admin@demo.com` and `alice@example.com`. |
| 6 | Selectors use roles and labels; no `data-testid`. |
| 7 | CI runs the suite in a Windows job (LocalDB needs Windows). |

## Steps

### Scaffolding ✅ Done (2026-11-13)

- [x] Add `@playwright/test`, `playwright.config.ts`, `e2e/tsconfig.json`, npm scripts and ignores
- [x] Fixtures: e2e API rewrite, admin and public API contexts, data and outbox helpers
- [x] Setup project saving admin and participant sessions
- [x] Global teardown dropping the database

### Specs ✅ Done (2026-11-13)

- [x] `anonymous/login.spec.ts`: sign-in, generic error, signed-out redirects, return URL
- [x] `participant/role-routing.spec.ts`: admin and merchant pages send a participant Home
- [x] `anonymous/guest-registration.spec.ts`: touched-only errors, format errors, pending with no pass
- [x] `admin/confirm-registration.spec.ts`: confirm sends the pass; cancel only for pending
- [x] `participant/my-events.spec.ts`: register, pending, cancel with "Keep it" focused

### App fixes found by the suite ✅ Done (2026-11-13)

- [x] Attendance row checkboxes had no accessible name (`[attr.aria-label]` sat on the host)
- [x] The event page jumped back to the Invite tab after Confirm, Check in or Cancel

### CI and docs ✅ Done (2026-11-13)

- [x] `e2e` job in `.github/workflows/ci.yml`, uploading the report on failure
- [x] `documentation/e2e-testing.md` and the `frontend/web/README.md` section

## Follow-ups

- `pages/team/team.html` uses the same `[attr.aria-label]` pattern on `mat-checkbox`.
- More flows: check-in scanner, meal stubs, sub-events, CSV import.

## Change log

| Date | Change |
|---|---|
| 2026-11-13 | Plan written and carried out. |
