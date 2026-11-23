# Configurable Categories

Status: ✅ Done (2026-11-22) — implemented and tested (API, unit, e2e).

Today the category list is hard-coded as **Senior** / **Regular** in both the API and the web app.
Each client should set its own list in a JSON file, or turn categories off by leaving the list empty.

## What exists today

One list is shared by **events** and **teams**:

| Place | Hard-coded today |
|-------|------------------|
| `Models/Entities.cs` `TeamCategories` | `Senior` / `Regular`; `MaxMembers`: Senior 8, else 10 |
| `Models/Entities.cs` `EventCategories` | `Senior` / `Regular` / null; `Clean` matches ignoring case |
| `EventsController.ValidateCategory` | Message "Category must be Senior, Regular, or none." |
| `TeamsController` create/update | Message "Category must be Senior or Regular."; cap from `MaxMembers` |
| `Entities.cs` `Team.Category` | Default `Regular`, `nvarchar(20)`, not null |
| `Entities.cs` `Event.Category` | `nvarchar(10)`, null = no category |
| `core/event-category.ts` | `EventCategory` type + labels |
| `core/models.ts` | `TeamCategory` type, `TEAM_CATEGORIES` |
| `shared/category-pill.ts` | Icons: Senior `elderly`, Regular `person` |
| `admin/event-form` | Fixed options: No category / Senior / Regular |
| `admin/teams` (`.ts`, `.html`) | Options "Senior (8 members)" / "Regular (10 members)", default `Regular`, Senior highlight |
| `core/category-warning.service.ts` | Compares event and team category (no hard-coded names; keeps working) |

Event and team categories must stay one list: the register warning compares the two.

## Decisions

| Item | Decision |
|------|----------|
| File | `backend/EventRegistration.Api/categories.json`, loaded with `AddJsonFile("categories.json", optional: true, reloadOnChange: true)`. A separate file, not a section of `appsettings.json`: .NET merges arrays by index across `appsettings.*.json`, so a client could not shrink or empty the list from `appsettings.Production.json` |
| Shape | `{ "Categories": { "Items": [ { "Name": "Senior", "Icon": "elderly", "TeamMaxMembers": 8 }, { "Name": "Regular", "Icon": "person", "TeamMaxMembers": 10 } ], "DefaultTeamMaxMembers": 10 } }` (one `Categories` section, so it binds like other settings) |
| Off | Missing file or empty `Items` = feature off: no category on the event form, team form, cards, or register warning |
| Fields | `Name` required, 1–20 chars, unique ignoring case. `Icon` optional (Material icon name; default `label`). `TeamMaxMembers` optional, ≥ 1 (default `DefaultTeamMaxMembers`, itself default 10) |
| Bad file | API fails at startup with a clear message (duplicate names, blank name, cap < 1) |
| Changes | Read on each request (`IOptionsMonitor`), so an edit applies without a restart |
| Web app | Gets the list from a new `GET /api/settings/categories` (anonymous; Browse Events needs it). Loaded once and cached. Not in `appSettings.json`: the server validates, so it must own the list |
| Server checks | Event: blank or a configured name (ignoring case, saved canonical). Team: a configured name, required only when the list is not empty. An edit that keeps the stored value is allowed even if it was removed from the file |
| Removed names | Old events/teams keep their stored value. Pill shows the name with the default icon; team cap uses `DefaultTeamMaxMembers` |
| DB | Widen `Events.Category` from `nvarchar(10)` to `nvarchar(20)` (one migration) to match team and the 20-char name limit. `Team.Category` default changes from `Regular` to `""` (no category); no migration needed |
| Messages | Error text lists the configured names, e.g. "Category must be Senior, Regular, or none." |
| Deploy | Left out of `dotnet publish` (`CopyToPublishDirectory="Never"`), so a deploy never overwrites the client's copy. A new install copies it once; until then categories are off (decided 2026-11-22) |

## Open questions

1. ~~**Publish behaviour**~~ — Decided 2026-11-22: `categories.json` and `menu.json` are both left
   out of publish/build output.
2. ~~**Team category when off**~~ — Recommended default taken (not answered): teams have no category
   (`""`) and use `DefaultTeamMaxMembers`.
3. ~~**Login page**~~ — Recommended default taken (not answered): out of scope.

Also asked for: a guide on updating each configuration file and publishing to an environment
(`documentation/configuration-and-publishing.md`).

## Changes

### Backend ✅ Done (2026-11-22)

- [x] `Models/CategoryOptions.cs`: `CategoryOptions` (`Items`, `DefaultTeamMaxMembers`, `Errors()`),
      `CategoryItem`, `CategoryOptionsValidator` (startup + on change).
- [x] `Program.cs`: `AddJsonFile("categories.json", optional, reloadOnChange)`, bound to the
      `Categories` section, `ValidateOnStart`.
- [x] Static `TeamCategories` / `EventCategories` replaced by the `CategoryCatalog` singleton
      (`Enabled`, `Names`, `List`, `TeamMaxMembers`, `TryEventCategory`, `TryTeamCategory`) on
      `IOptionsMonitor<CategoryOptions>`.
- [x] `EventsController`: create/update use the catalog; an edit may keep the stored value.
- [x] `TeamsController`: create/update validation, caps (list, detail, update, add member);
      `mine/category` answers 204 for a team with no category; messages without a category name.
- [x] `SettingsController`: `GET /api/settings/categories` (`AllowAnonymous`) → `[{ name, icon, teamMaxMembers }]`.
- [x] `Team.Category` default `""`; `TeamUpsertRequest.Category` nullable; migration
      `WidenEventCategory` (`Events.Category` 10 → 20).
- [x] `categories.json` with today's values; `.csproj` `CopyToPublishDirectory="Never"`.

### Frontend ✅ Done (2026-11-22)

- [x] `core/category.service.ts`: loads once; `list`, `enabled`, `icon(name)`, `names(current)`.
- [x] `EventCategory` / `TeamCategory` are `string`; `TEAM_CATEGORIES` and `EVENT_CATEGORY_LABELS` removed.
- [x] `category-pill.ts`: icon from the service (default `label`).
- [x] `event-form`: options from the service (+ the event's own); Category hidden when none.
- [x] `teams`: options with "(N members)"; hidden (field and column) when none; new team starts in
      the first listed category; Senior-only highlight dropped.
- [x] `team.html`, `invite-teams-dialog.ts`: no "team" label / separator for a team with no category.
- [x] `category-warning.service.ts`: unchanged (no event category = no warning).

### Docs (same change) ✅ Done (2026-11-22)

- [x] `documentation/configuration-and-publishing.md`: every config file and how to publish to an environment.
- [x] `.claude/rules/validation.md`: Event and Team category rows.
- [x] `.claude/rules/registration-flow.md`: categories come from `categories.json`.
- [x] `.claude/skills/admin-guide/reference.md`, `participant-guide/reference.md`.
- [x] `.claude/rules/deployment.md`, `demo-and-config.md` + `deploy-iis` skill; `documentation/deployment.md`.
- [x] `documentation/database-table.md`: `Events.Category` length, category source.

## Test seams

- xUnit `CategoryCatalogTests` (no database): matching, blank, removed names kept on edit, empty
  list, caps and default, icons, file errors. Used instead of a second API fixture with another
  file, which would need its own database.
- xUnit `EventCategoryTests` (API): public list endpoint, error text names the list, event and
  team keep a category taken off the list (team gets the default cap).
- Vitest: `CategoryService` (list, removed name, empty, load failure); Teams page (choices and
  caps, default from a late list, hidden when none).
- Playwright `e2e/admin/event-category.spec.ts`: event form with an empty list (no choice) and a
  custom list (stubbed endpoint); existing category specs pass with the default file.

## Steps ✅ Done (2026-11-22)

- [x] Answer open questions
- [x] Backend: options, service, endpoint, migration (API tests first)
- [x] Frontend: service, pill, event form, teams form (unit tests first)
- [x] Docs
- [x] Run API, unit and e2e tests (277 API, 286 unit, 34 e2e pass)

## Change log

| Date | Change |
|------|--------|
| 2026-11-22 | Plan created |
| 2026-11-22 | Publish: `categories.json` left out. Questions 2–3 took the recommended defaults. Shape nested under one `Categories` section. Guide added to scope. Built in worktree `nxpert-PALGOLF-categories` (branch `configurable-categories`) |
| 2026-11-22 | Implemented: catalog service, public list endpoint, forms driven by the list, migration, guide. 277 API, 286 unit, 34 e2e tests pass |
