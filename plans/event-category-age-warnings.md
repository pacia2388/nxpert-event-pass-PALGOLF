# Event Category & Age Warnings

Status: ✅ Done (2026-11-16) — implemented and tested (API, unit, e2e). Warning switched from age to team category on 2026-11-16.

Each event has an optional **category**: **Senior**, **Regular**, or none. The category shows as a
pill next to the status pill (e.g. "Open") on event cards. A signed-in player whose team category
does not match the event category gets a warning before registering.

## Decisions

| Item | Decision |
|------|----------|
| DB column | `Events.Category` — `nvarchar(10)`, **NULL** = no category |
| Values | `Senior` \| `Regular` \| null; anything else is a 400 with a `message` |
| Mismatch | Event category ≠ the player's team category (Senior / Regular). Age is not used |
| Message | "This is a Senior event, but your team is in the Regular category." Title "Different category". No age wording |
| Warning | Confirm dialog on **Register** (account flow); safe option **Cancel** pre-selected; "Register anyway" continues. Not enforced on the server |
| No team | Guests, admins/merchants, and players not on a team: no warning |
| Team lookup | On each **Register** click the page reads only the player's team category via `GET /api/teams/mine/category` (Captain or Participant; 204 = no team), so a sign-in by invite link or a team change after the page loaded is seen |
| Pill | Admin Events list cards and Browse Events cards: "Senior" / "Regular" (no age range on the label) |
| Event form | Category toggle: No category / Senior / Regular |

## Affected modules

| Area | File | Change |
|------|------|--------|
| Backend | `Models/Entities.cs` | `Event.Category`, `EventCategories` (values + validation) |
| Backend | `Migrations/` | `EventCategory` migration (nullable column) |
| Backend | `Models/Dtos.cs` | `Category` on `EventUpsertRequest`, `EventDto` |
| Backend | `Controllers/EventsController.cs` | Validate + save on create/update; map in DTO projections |
| Frontend | `core/models.ts` | `category?: EventCategory \| null` |
| Frontend | `core/event-category.ts` | `categoryTeamWarning(eventCategory, teamCategory)` + label |
| Backend | `Controllers/TeamsController.cs` | `GET /api/teams/mine/category` → `TeamCategoryDto` |
| Frontend | `core/auth.service.ts` | `myTeamCategory()` (replaces `myBirthdate()`), `canHaveTeam` |
| Frontend | `pages/admin/event-form` | Category toggle |
| Frontend | `pages/admin/events-list`, `pages/events-browse` | Category pill |
| Frontend | `pages/public-event` | Confirm dialog before account registration |
| Docs | `documentation/database-table.md`, `.claude/` guides and rules | Category column; admin and participant guides; validation and registration-flow rules |

## Test seams

- API (xUnit): create/update/read category; invalid value rejected.
- `categoryTeamWarning` (Vitest): event category + team category → warning or none.
- `AuthService.myTeamCategory` (Vitest): team category, or null for no team / failure / other roles.
- Playwright: Regular-team player warned on a Senior event (and vice versa); matching team and no team register without a warning; admin card shows the pill.

## Steps ✅ Done (2026-11-13)

- [x] Backend: field, migration, DTOs, validation (API test first)
- [x] Frontend: age-check function (unit test first)
- [x] Frontend: team-category check replaces the age check (unit + e2e first)
- [x] Frontend: event form toggle + card pills
- [x] Frontend: confirm dialog on register
- [x] Playwright e2e
- [x] Docs
- [x] Verify in the running app

## Change log

| Date | Change |
|------|--------|
| 2026-11-13 | Plan created |
| 2026-11-13 | Implemented: category column, form toggle, card pills, register warning; 7 API, 7 unit, 4 e2e tests |
| 2026-11-13 | Label shortened from "Senior (65+)" to "Senior" on the pill and form toggle |
| 2026-11-13 | Plan corrected: form uses a toggle; `.claude/` guides and rules were updated |
| 2026-11-16 | Warning now compares the event category with the player's team category, not age; message names categories only; no team = no warning. 6 + 6 unit, 4 e2e tests |
| 2026-11-16 | Review fixes: category-only endpoint, lookup on Register (not page load), own role check, `TeamCategory` type. +1 API test |
