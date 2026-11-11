# Plan: VOUCHER page (airline baggage, golf equipment)

| | |
|---|---|
| **Status** | ✅ Done (2026-11-11) |
| **Last updated** | 2026-11-11 |
| **Owner** | _TBD_ |

> **Keep this plan current.** When work starts, update this file first: set **Status**, answer the
> open questions, and tick steps as they land. Section markers: `✅ Done (YYYY-MM-DD)` when complete,
> `🚧 In progress` while partly done. Add a change-log row for each update.

## Goal

Give **Participants** and **Team Leads (TL)** a **VOUCHER** module where they generate airline
baggage vouchers for golf equipment.

- Top of page: the signed-in user's information and an entry form with one input (**airline ticket
  number**) and one button (**Generate**).
- **Generate** saves a new voucher. It **fails if that airline ticket number was already used**.
- On success the new voucher appears in the **voucher table** at the bottom of the page.
- Each table row has **Preview** and **Download (PNG image)** action buttons.
- The voucher (preview and download) shows **only**: full name, airline ticket number, and a QR
  code (dummy for now).

## Decisions (from review, 2026-11-11)

| # | Question | Answer |
|---|----------|--------|
| 1 | Who sees it? | **Participant** and **TL** roles only. |
| 2 | Menu label | **VOUCHER** |
| 3 | Ticket number format | No airline format check yet. Alphanumeric + dashes, length limited (4–30). |
| 4 | Output | Preview + Download action per table row. Download is a **PNG image** (decided 2026-11-11; no new dependency). |
| 5 | Tie to an event? | No. Stand-alone voucher generation page. |
| 6 | Persist? | Yes. Each Generate saves a row in a `Vouchers` table; duplicates are refused. |
| 7 | Voucher content | Full name, airline ticket number, QR code. Footer: voucher no. + generated date/time (approved on sample, 2026-11-11). |

## Design

### Data — new `Voucher` entity (`Models/Entities.cs`, `Data/AppDbContext.cs`)

| Column | Type | Notes |
|--------|------|-------|
| `Id` | int, PK | Voucher number shown as `GV-{Id:D6}` (derived, no column). |
| `UserId` | int, FK → Users | Owner. Cascade on user delete, like other user-owned rows. |
| `TicketNumber` | nvarchar(30), required | Stored trimmed + upper-cased. **Unique index.** |
| `CreatedAt` | datetime2 | UTC, set by server. |

- Migration `AddVouchers` (`dotnet ef migrations add AddVouchers`). Applied on API start by the
  existing `Database.MigrateAsync()` in `Program.cs`.
- The unique index is the real duplicate guard (two clicks / two users at once). The controller
  also checks first so the normal case returns a friendly message.

### API — new `VouchersController` (`api/vouchers`)

`[Authorize(Roles = "Participant,TL")]` (use `Roles.Participant` / `Roles.TeamLead` constants).
Ownership: every query filters on `this.UserId()`.

| Method | Route | Body | Result |
|--------|-------|------|--------|
| `GET` | `/api/vouchers` | — | `VoucherDto[]` for the current user, newest first. |
| `POST` | `/api/vouchers` | `{ ticketNumber }` | `201` + `VoucherDto`; `409` if ticket number already used; `400` if invalid. |

`VoucherDto`: `id`, `voucherNo` (`GV-000123`), `fullName`, `ticketNumber`, `createdAt`.

`CreateVoucherRequest` validation (L2 annotations + server trim/upper-case):

| Field | Rule |
|-------|------|
| `TicketNumber` | Required, trimmed, upper-cased, 4–30 chars, `^[A-Za-z0-9-]+$`, not dashes only. |

- Duplicate check: `AnyAsync(v => v.TicketNumber == normalized)` → `409` ProblemDetails
  *"This airline ticket number already has a voucher."* Also catch the unique-index
  `DbUpdateException` and return the same `409`.
- **Duplicate scope = global** (any user), since a ticket number belongs to one passenger.
  Confirmed 2026-11-11.

### Frontend

**Guard** (`core/guards.ts`): add `participantOrTlGuard` — passes when role is `Participant` or
`TL`, else bounces to `homeFor(auth)` (same shape as the other guards). `AuthService` has
`isTeamLead` but no `isParticipant` yet — add `isParticipant = computed(() => role === 'Participant')`.

**Route** (`app.routes.ts`):

```ts
{
  path: 'voucher',
  canActivate: [participantOrTlGuard],
  loadComponent: () => import('./pages/voucher/voucher').then((m) => m.VoucherComponent),
},
```

**Launcher card** (`pages/home/home.ts`), participant list (TL uses the same list):

```ts
{ title: 'VOUCHER', subtitle: 'Golf Baggage', icon: 'luggage', link: '/voucher' }
```

**API service** (`core/api.service.ts`) + **model** (`core/models.ts`): `getMyVouchers()`,
`createVoucher(ticketNumber)`, `Voucher` interface.

**Page** `pages/voucher/voucher.{ts,html,scss}`:

1. **User information** card (read-only): full name, email (from `api.getMyProfile()`, i.e.
   `GET /api/auth/me`), link to *Edit account*.
2. **Entry form**: *Airline ticket number* input + **Generate** button only. Button disabled while
   invalid or while saving (loading indicator, as on event save). On `409` show the server message
   under the field and as a toast; keep the input. On success clear the input, toast, and prepend
   the new row to the table.
3. **Voucher table** (`mat-table`, newest first): Voucher No. · Airline ticket number · Generated
   on · Actions. Empty state: "No vouchers yet." Actions per row:
   - **Preview** (`visibility` icon): opens a dialog showing the voucher sheet (full name, ticket
     number, QR).
   - **Download** (`download` icon): builds and saves `voucher-<ticket>.png`.

**QR code (dummy)**: reuse installed `qrcode` package. Add to `core/qr.util.ts`:

```ts
// shortcut: dummy payload, not validated anywhere; replace with a server token when vouchers are scanned.
export function voucherQrDataUrl(voucherNo: string, width = 260): Promise<string> {
  return QRCode.toDataURL(`VOUCHER:${voucherNo}`, { errorCorrectionLevel: 'Q', margin: 2, width });
}
```

The payload must not start with `REG:`, so the check-in scanner reports it as invalid.

**Download — PNG image (decided 2026-11-11).** No new dependency. Add `downloadVoucherPng(voucher)`
to `core/qr.util.ts` beside `downloadPass()`:

1. Create an off-screen `<canvas>` (e.g. 800 × 1000 px, white background).
2. Draw text: title "Airline Baggage Voucher — Golf Equipment", full name, airline ticket number,
   footer small at the bottom: `Voucher No. GV-000001 · Generated yyyy-MM-dd HH:mm` (local time of
   `createdAt`). Sample: `documentation/samples/sample-voucher.png`.
3. Load `voucherQrDataUrl(voucherNo, 600)` into an `Image` and `drawImage` it centered.
4. `canvas.toDataURL('image/png')` → `<a download="voucher-<ticket>.png">` click, same as
   `downloadPass()`.

Preview dialog shows the same PNG the download saves (`voucherPngDataUrl`), so both match exactly. Prints fine from any image viewer; a fixed
page-size PDF was considered and dropped (would need `jspdf` or a server PDF library).

Display values with Angular interpolation only (no `[innerHTML]`), per security rules.

## Steps

### Backend ✅ Done (2026-11-11)

- [x] `Voucher` entity + `DbSet<Voucher> Vouchers`, unique index on `TicketNumber`, `MaxLength(30)`.
- [x] Migration `AddVouchers`.
- [x] `CreateVoucherRequest` (annotations) + `VoucherDto`.
- [x] `VouchersController`: `GET` mine, `POST` create with duplicate → `409`, role-restricted.
- [x] Quick test: create, duplicate (same user and other user) → `409`, invalid chars → `400`,
  Admin/Merchant → `403`.

### Frontend ✅ Done (2026-11-11)

- [x] `isParticipant` in `AuthService`; `participantOrTlGuard` in `core/guards.ts`.
- [x] `/voucher` route; **VOUCHER** launcher card for Participant/TL.
- [x] `Voucher` model + `getMyVouchers` / `createVoucher` in `ApiService`.
- [x] `pages/voucher`: user info, entry form (Generate only), voucher table with Preview/Download.
- [x] `voucherQrDataUrl` in `core/qr.util.ts`.
- [x] Preview dialog.
- [x] `downloadVoucherPng` (canvas PNG) in `core/qr.util.ts`; wire the row Download button.
- [x] Check at phone width (date column hidden < 600px so actions stay visible).
- [x] Click Download in a browser, open the PNG, scan the QR with a phone (expect `VOUCHER:GV-…`). Confirmed working by the user 2026-11-11.

### Docs ✅ Done (2026-11-11)

- [x] Add VOUCHER page to the participant guide in `documentation/` and the `participant-guide`
  skill; add the `Vouchers` table/endpoints to any table/endpoint reference doc.

### Later (not in v1)

- Real QR token + voucher scanner/verification; admin list of vouchers; delete/void a voucher;
  airline-specific ticket number formats.

## Open questions

- ~~**A. Duplicate scope**~~ — answered 2026-11-11: block a ticket number used by **anyone**
  (global unique index on `TicketNumber`).
- ~~**B. Download format**~~ — answered 2026-11-11: **PNG image** (canvas, no new dependency).

No open questions remain.

## Change log

| Date | Change |
|------|--------|
| 2026-11-11 | Draft plan created. |
| 2026-11-11 | Review answers applied: Participant + TL only, label VOUCHER, saved `Vouchers` table with duplicate-ticket check, table with Preview/Download per row, voucher shows full name + ticket no. + QR only, `jspdf` for PDF. |
| 2026-11-11 | Duplicate scope confirmed: ticket number blocked when used by anyone. |
| 2026-11-11 | Stale items fixed: `jspdf` no longer assumed — download format (PNG vs PDF) is open question B with options table; noted `isParticipant` must be added; profile endpoint named. |
| 2026-11-11 | Download format decided: PNG image via canvas (`downloadVoucherPng`); PDF/`jspdf` dropped. Status ready to build. |
| 2026-11-11 | Sample PNG approved (`documentation/samples/sample-voucher.png`); footer keeps voucher no. + generated date/time. |
| 2026-11-11 | Implemented: `Vouchers` table + `AddVouchers` migration, `VouchersController` (GET mine, POST with 409 on duplicate), `participantOrTlGuard`, `/voucher` route + launcher card, page + preview dialog (separate .html/.scss), canvas PNG download, docs (`database-table.md`, `roles.md`, participant-guide skill). API tests and UI checks passed on a temporary database. |
| 2026-11-11 | Fix: successful Generate now calls `form.resetForm()` so the cleared ticket field shows no required error. |
| 2026-11-11 | Download + phone QR scan confirmed by the user. All steps done; plan moved to `plans/completed/20261111_baggage-voucher.md`. |
