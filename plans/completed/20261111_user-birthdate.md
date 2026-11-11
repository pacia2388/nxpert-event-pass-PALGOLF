# User Birthdate

Status: ✅ Done (2026-11-11) — implemented, documented and verified in the app.

Add a **Birthdate** to user accounts. Required in the UI (admin user form, My Account, sign-up) and
in CSV import (**last column**), **nullable in the database** so existing users stay valid. CSV files
from the old template are rejected.

## Decisions

| Item | Decision |
|------|----------|
| DB column | `Users.Birthdate` — `date`, **NULL** allowed (`DateOnly?` in C#) |
| Wire format | `"yyyy-MM-dd"` (default `DateOnly` JSON) |
| Date input | Material `mat-datepicker` (calendar toggle, opens on year view) on admin form, My Account and sign-up; `shared/birthdate.ts` converts Date ↔ yyyy-MM-dd in local time |
| Admin user form | Required |
| Self sign-up (`/register`) | Required |
| My Account (profile update) | Required — existing users with no birthdate must fill it before saving |
| CSV import | New last column `Birthdate` in the template, **required** header and value; accepts `yyyy-MM-dd` and `M/d/yyyy` (Excel default) |
| CSV header | **Fixed** to the template, in order: `First Name, Middle Initial (optional), Last Name, Email, Username (optional), ID No (optional), Designation (optional), Organization (optional), Phone (optional), Role (optional), Notification Channel (optional), Birthdate`. Unmarked = required. Blank optional values keep today's defaults: Username auto-generated, Role = Participant, Notification Channel = Email; Phone is still required when the channel is Viber. Compared after trimming and ignoring case (and a UTF-8 BOM from Excel); anything else rejects the file |
| Validation | Valid date, not in the future, not before 1900-01-01 (server-side; client mirrors it) |
| Privacy | Birthdate is personal data — **only** in user admin, My Account, and user CSV export. Not in check-in, face TV, attendance, offline cache, mail audiences, or public pages |

## Open questions

- [x] **Self sign-up (`/register`)**: yes, birthdate required. (Sign-up always creates a
      **Participant**, `CanConfirmRegistrations = false` — `AuthController.Register`.)
- [x] **Old CSV / blank birthdate**: old CSVs must fail. The header row must include a
      `Birthdate` column (error like the First/Last/Email check), and a row with a blank or invalid
      birthdate fails that row. Enforced client-side (header) and server-side (each row).
- [x] **User list CSV export**: add `Birthdate` as last column.
- [x] **CSV header is fixed to the template**: the header row must match the downloadable template
      exactly — same 12 columns, same order. Any missing, extra, renamed or reordered column rejects
      the whole file. Header aliases (`HEADER_MAP`) are removed. A file from the Users **Export** is
      not an import file and will be rejected.

## Affected modules

### Backend (`backend/EventRegistration.Api`)

| File | Change |
|------|--------|
| `Models/Entities.cs` — `User` | Add `public DateOnly? Birthdate { get; set; }` |
| `Migrations/` | New migration `UserBirthdate` (nullable `date` column, no data backfill) |
| `Models/Dtos.cs` — `UserDto` | Add `DateOnly? Birthdate` |
| `Models/Dtos.cs` — `AdminUserUpsertRequest` | Add `DateOnly? Birthdate` |
| `Models/Dtos.cs` — `UserImportRow` | Add `string? Birthdate` (raw text; parsed server-side) |
| `Models/Dtos.cs` — `ProfileDto`, `ProfileUpdateRequest` | Add `DateOnly? Birthdate` |
| `Controllers/UsersController.cs` | Map in DTO projection (~l.30); require + validate on Create (~l.150) and Update (~l.340); parse/validate in Import (~l.260), blank or bad date = row error |
| `Controllers/AuthController.cs` | `GET me` returns it (~l.106); `PUT me` requires + validates (~l.160) |
| `Controllers/AuthController.cs` — `register` | Require + validate; add `DateOnly? Birthdate` to `RegisterRequest` |
| `Data/DbSeeder.cs` | Skipped: demo users have no birthdate; they must add one before saving My Account |

Not touched (confirm birthdate is **not** added): `AuthResponse`/JWT, `CheckInController`,
`EventsController` (l.345 member list), `AttendeeQuery`, `OfflineMemberDto`, `MailPlanAudience`,
`FaceMatcher`, guest registration (`GuestRegisterRequest`).

### Frontend (`frontend/web/src/app`)

| File | Change |
|------|--------|
| `core/models.ts` | Add `birthdate?: string \| null` to `UserDto`, `UserImportRow`, `ProfileUpdate` (+ profile DTO), and admin upsert type |
| `core/api.service.ts` | No change expected (passes objects through) — verify |
| `pages/admin/users/user-form-panel.{ts,html}` | `<input type="date">`, required, `max` = today, `min` = 1900-01-01; send on create/update |
| `pages/admin/users/users.ts` | Add `Birthdate` last column to `exportCsv()` |
| `pages/admin/users/user-import-dialog.ts` | Replace `HEADER_MAP` with one ordered column list (the single source for `TEMPLATE_HEADERS` and the header check); label optional columns `(optional)` in the template header; append `Birthdate` + sample `1990-01-31` as last column; header row must equal the list (trim, case-insensitive, strip BOM) else `parseError` naming the expected header and the first mismatch (missing / extra / wrong position); map cells by position |
| `pages/admin/users/user-import-dialog.html` | Add Birthdate column to preview table |
| `pages/account/account.{ts,html}` | Date field, required, prefilled from `GET me`, sent in `PUT me` |
| `pages/register/register.{ts,html}` | Required date input, sent in register request |

### Docs

| File | Change |
|------|--------|
| `documentation/database-table.md` — Users matrix | Add `Birthdate` row (`date`, nullable) |
| `.claude/skills/admin-guide/reference.md` (~l.310) | CSV import: header must match the template exactly (order included), Birthdate last and required; add Birthdate to user form fields |
| `.claude/skills/participant-guide` | My Account: birthdate field |
| `.claude/rules/validation.md` | Add Birthdate row to canonical field rules |

## Steps

### Backend ✅ Done (2026-11-11)
- [x] Entity property + migration `UserBirthdate`
- [x] DTOs (`UserDto`, `AdminUserUpsertRequest`, `UserImportRow`, `ProfileDto`, `ProfileUpdateRequest`)
- [x] Shared validation helper (not future, ≥ 1900) used by Users create/update, `PUT me`, import
- [x] Import: parse `yyyy-MM-dd` / `M/d/yyyy` (`InvariantCulture`), row error on bad value
- [x] `GET me` / `PUT me`
- [x] `register` (self sign-up)

### Frontend ✅ Done (2026-11-11)
- [x] Models
- [x] Admin user form (required date input)
- [x] My Account (required date input)
- [x] Self sign-up page (required date input)
- [x] CSV import: fixed ordered header (no aliases), optional columns labelled `(optional)`, `Birthdate` last, clear mismatch error, preview column
- [x] Users CSV export last column

### Docs & verification ✅ Done (2026-11-11)
- [x] Update docs listed above
- [x] API and web build clean; header check logic verified with a scratch script (exact, BOM, case, trailing comma, missing, extra, swapped, old template)
- [x] Run API + web (verified by the user, 2026-11-11); check: create/edit user, My Account save, sign-up, import template round-trip (valid / blank / bad birthdate), old CSV without the column is rejected, reordered / extra / renamed / missing column is rejected, Excel-saved template (BOM) is accepted

## Change log

| Date | Change |
|------|--------|
| 2026-11-11 | Plan created |
| 2026-11-11 | Manual run-through passed (user); plan completed and moved to `plans/completed/` |
| 2026-11-11 | Birthdate fields switched from native date input to `mat-datepicker` |
| 2026-11-11 | Implemented backend (migration `UserBirthdate`, `BirthdateRules`), frontend, docs; manual check pending |
| 2026-11-11 | Template header marks optional columns with `(optional)` |
| 2026-11-11 | CSV header fixed to the template: missing, extra or reordered columns reject the file; aliases removed |
| 2026-11-11 | Resolved: sign-up requires birthdate; Birthdate CSV header and value required (old CSVs fail); export gets Birthdate last |
