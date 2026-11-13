# Event Category & Age Warnings

Status: ✅ Done (2026-11-13) — implemented and tested (API, unit, e2e).

Each event has an optional **category**: **Senior**, **Regular**, or none. The category shows as a
pill next to the status pill (e.g. "Open") on event cards. A signed-in participant whose age does
not match the category gets a warning before registering.

## Decisions

| Item | Decision |
|------|----------|
| DB column | `Events.Category` — `nvarchar(10)`, **NULL** = no category |
| Values | `Senior` \| `Regular` \| null; anything else is a 400 with a `message` |
| Senior age | 65 or older on the day of registering (`today`, local time) |
| Mismatch | Senior event + age < 65, or Regular event + age ≥ 65 |
| Warning | Confirm dialog on **Register** (account flow); safe option **Cancel** pre-selected; "Register anyway" continues. Not enforced on the server |
| Unknown age | Guests and users with no birthdate: no warning |
| Privacy | Birthdate stays out of event DTOs; the page reads the user's own birthdate via `GET /api/auth/me` |
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
| Frontend | `core/event-category.ts` | `categoryAgeWarning(category, birthdate, today)` + label |
| Frontend | `pages/admin/event-form` | Category toggle |
| Frontend | `pages/admin/events-list`, `pages/events-browse` | Category pill |
| Frontend | `pages/public-event` | Confirm dialog before account registration |
| Docs | `documentation/database-table.md`, `.claude/` guides and rules | Category column; admin and participant guides; validation and registration-flow rules |

## Test seams

- API (xUnit): create/update/read category; invalid value rejected.
- `categoryAgeWarning` (Vitest): category + birthdate + today → warning or none.
- Playwright: participant under 65 sees the warning on a Senior event; admin card shows the pill.

## Steps ✅ Done (2026-11-13)

- [x] Backend: field, migration, DTOs, validation (API test first)
- [x] Frontend: age-check function (unit test first)
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
