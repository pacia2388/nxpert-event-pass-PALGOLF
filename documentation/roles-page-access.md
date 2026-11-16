# Roles and page access

Short reference of which pages each role can open, and whether it can **edit**, only **view**, or
gets **mixed** access (some parts editable, some read-only or hidden). For the full capability
matrix, API enforcement and known gaps, see [roles.md](roles.md).

## Roles

| Role | Who | Lands on |
|---|---|---|
| **Admin / SuperAdmin** | Runs the system. SuperAdmin = Admin, set only in the database. | `/home` |
| **Participant + Can confirm** | Participant with the *Can confirm registrations* permission. | `/home` |
| **Participant** | Default role. Registers for events, uses their pass. | `/home` |
| **Captain** | Leads one team; adds Participants to it. | `/home` |
| **Merchant** | Vendor; scans meal stubs only. | `/merchant/scan` |

## Page access

**Edit** = full read/write · **View** = read-only · **Mixed** = see notes · **—** = no access
(redirected to the role's landing page)

| Page | Route | Admin / SuperAdmin | Participant + Can confirm | Participant | Captain | Merchant |
|---|---|---|---|---|---|---|
| Home (launcher) | `/home` | View | View | View | View | View |
| Browse Events | `/events` | Edit ¹ | Edit ¹ | Edit ¹ | Edit ¹ | Edit ¹ (not in menu) |
| Calendar | `/calendar` | Mixed ² | View (all events) | View (own) | View (own) | — |
| Mail Planner | `/mail-planner` | Edit | — | — | — | — |
| My Account (account menu) | `/account` | Edit | Edit | Edit | Edit | — |
| My Team | `/team` | — | Mixed ³ | View | Mixed ³ | — |
| Voucher (prototype) | `/voucher` | — | Edit | Edit | Edit | — |
| Events list | `/admin/events` | Edit | Mixed ⁴ | — | — | — |
| Event detail / Attendance | `/admin/events/:id` | Edit | Mixed ⁴ | — | — | — |
| Create / edit event | `/admin/events/new`, `/admin/events/:id/edit` | Edit | — | — | — | — |
| Dashboard | `/admin/dashboard` | View | — | — | — | — |
| Users | `/admin/users` | Edit | — | — | — | — |
| Teams | `/admin/teams` | Edit | — | — | — | — |
| Settings (account menu) | `/admin/settings` | Edit | — | — | — | — |
| QR check-in scanner | `/scanner/:eventId` | Edit | — | — | — | — |
| Face check-in TV | `/scanner/:eventId/tv` | View | — | — | — | — |
| Meal-stub scanner | `/merchant/scan` | Edit | — | — | — | Edit |

Public pages (no sign-in): `/login`, `/register`, `/verify/:token`, `/e/:slug` (event page, guest
registration), `/r/:token` (edit own guest registration by link).

## Mixed-access notes

1. **Browse Events** — everyone can view published and closed public events and register
   themselves for open ones; closed events can't be registered for. Nothing else is editable.
2. **Calendar (Admin)** — sees all events plus admin-only controls (invitations). Confirmers see
   all events read-only; Participants and Captains see their own and invited events plus the
   same published and closed public events as Browse Events.
3. **My Team** — Captain: view own team **and** add members / send registration notices; cannot
   remove members, rename, or change category (Admin does that on `/admin/teams`). A Participant
   with *Can confirm* is still a Participant here: **view only**.
4. **Events list / Event detail (Can confirm)** — view events and use Attendance actions: confirm,
   check in, resend pass, cancel **pending** registrations, kit/meals, export. Hidden from them:
   create/edit/delete event, delete registrations, invitations, meal-stub setup, feedback list.

## Pages removed from the menu

These are commented out of the Home launcher (`pages/home/home.ts`) because they are not needed.
The routes still exist, so a direct URL or an in-page link still opens them.

| Page | Route | Was shown to | Still linked from |
|---|---|---|---|
| My Events | `/my-events` | Everyone except Merchant | Browse Events ("Registered" button), public event page, notifications |
| Reminders (Mail Planner) | `/mail-planner` | Participant, Can confirm, Captain | none |

## Notes

- Page guards (`frontend/web/src/app/core/guards.ts`) are **UI only**; the API
  (`[Authorize(Roles=…)]` in `backend/EventRegistration.Api/Controllers`) is authoritative.
- *Can confirm* changes apply at the user's next sign-in.
- Captain: [roles.md](roles.md) says Captain is limited to `/team` and `/voucher`, but the current
  guards (`authGuard`, `nonMerchantGuard`) let a Captain open Home, Browse Events, My Events,
  Calendar, Mail Planner and My Account too. The table above reflects the current code.
