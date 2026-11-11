# Roles, capabilities and user registration

## Roles

Roles are **hard-coded**. There is no roles or permissions table. A user has exactly one role, stored
as a string in `Users.Role` (see [database-table.md](database-table.md)).

| Role | Purpose |
|---|---|
| `SuperAdmin` | Same access as Admin wherever checked. **Cannot be assigned from the app** (not in the role picker, rejected by the API); only by editing the database. An existing SuperAdmin keeps the role when edited. |
| `Admin` | Runs the system: events, users, settings, attendance, scanners, mail. |
| `Participant` | Default role. Registers for events and uses their pass. |
| `TL` (Team Lead) | Assignable by an admin on the Users page (form, CSV import). Leads one team, assigned by an admin on the Teams page. **The app shows a TL only the Team page** (`/team`), where they add Participants to their team, and the prototype Voucher page (`/voucher`). Lands on `/team`. |
| `Merchant` | Vendor account that only scans and redeems meal stubs. Lands on `/merchant/scan`. |

Plus one **permission** (not a role), the only per-user switch:

| Permission | Stored as | Effect |
|---|---|---|
| **Can confirm registrations** | `Users.CanConfirmRegistrations` (bit) → JWT claim `perm:confirm-registrations=true` | Gives a Participant the Attendance actions and read access to event lists/details. Admin and SuperAdmin always have it. Changes apply at the next sign-in. |

## Capability matrix

✓ allowed · ✗ blocked · *own* = only their own registrations · – not applicable

| Capability | SuperAdmin | Admin | Participant + Can confirm | Participant | Merchant | Not signed in |
|---|---|---|---|---|---|---|
| Browse events / open public event page | ✓ | ✓ | ✓ | ✓ | ✓ | public page only |
| Register for an event with an account | ✓ | ✓ | ✓ | ✓ | ✓ (API allows) | – |
| Register as guest, edit by link, offline guest sync | – | – | – | – | – | ✓ |
| My Events: view answers, edit answers, resend pass, cancel | *own* ¹ | any | *own* | *own* | *own* | – |
| Submit feedback / see own feedback | ✓ | ✓ | ✓ | ✓ | ✓ | – |
| See all feedback for an event | ✓ | ✓ | ✗ | ✗ | ✗ | – |
| Calendar | ✓ (all events) | ✓ (all events) | ✓ (all events) | ✓ (own) | ✗ | – |
| Mail inbox / My Reminders | ✓ | ✓ | ✓ | ✓ | ✓ (API only) | – |
| Mail planner: create, edit, send plans | ✓ | ✓ | ✗ ² | ✗ ² | ✗ | – |
| My Account (edit profile) | ✓ | ✓ | ✓ | ✓ | ✗ (UI only) | – |
| VOUCHER: generate / preview / download own baggage vouchers (Participant and TL roles only) | ✗ | ✗ | ✓ | ✓ | ✗ | – |
| Event list and details, offline bundle, face descriptors | ✓ | ✓ | ✓ | ✗ | ✗ | – |
| Attendance: list, confirm, check in, resend pass, kit, meals, export | ✓ | ✓ | ✓ | ✗ | ✗ | – |
| Attendance: cancel pending registration | ✓ | ✓ | ✓ | ✗ | ✗ | – |
| Attendance: remove (delete) a registration | ✓ | ✓ | ✗ | ✗ | ✗ | – |
| Viber broadcast to registrants | ✓ | ✓ | ✓ | ✗ | ✗ | – |
| Create, edit, close, reopen, delete events | ✓ | ✓ | ✗ | ✗ | ✗ | – |
| Invitations, contacts, pasted images | ✓ | ✓ | ✗ | ✗ | ✗ | – |
| QR check-in scanner, face TV, check-in API | ✓ | ✓ | ✗ | ✗ | ✗ | – |
| Meal-stub scanner | ✓ | ✓ | ✗ | ✗ | ✓ | – |
| Dashboard | ✓ | ✓ | ✗ | ✗ | ✗ | – |
| Users: create, import, edit role and permission, delete | ✓ | ✓ | ✗ | ✗ | ✗ | – |
| Settings | ✓ | ✓ | ✗ | ✗ | ✗ | – |
| Teams: create, edit, delete, assign lead, add / remove members | ✓ | ✓ | ✗ | ✗ | ✗ | – |

**Team Lead (`TL`)** is not a column above. A TL can do exactly this in the app:

| Capability | TL |
|---|---|
| See their own team and its members (`GET /api/teams/mine`) | ✓ |
| Add a Participant to their team by name + email (`POST /api/teams/mine/members`); an unknown email creates the account | ✓, up to the cap (Senior 8, Regular 10) |
| Send a registration notice to selected **unregistered** members of their own team (`POST /api/teams/mine/registration-notice`): emails a single-use, 7-day link to `/register?team=…` where the member completes the account and sets their own password. Registered members are exempt | ✓ |
| Remove members, rename the team, change its category | ✗ (admin only) |

**Participants on the Team page** (`/team`, "My Team" tile): view-only. They see their team name,
category, member count and the members table (name, email, added, notified status), via
`GET /api/teams/mine`. No add-member panel, no registration notice button, no checkboxes; the
write endpoints are TL-only on the server. A Participant in no team sees "You are not in a team yet".
| Voucher page (`/voucher`, prototype): enter a Philippine Airlines booking reference, get a golf equipment voucher code. Also open to Participants; reached from the account menu | ✓ |
| Every other page (Home, Browse Events, My Events, Calendar, Mail Planner, Edit account, admin pages, scanners) | ✗, redirected to `/team` ³ |

¹ Bug: SuperAdmin is left out of the ownership checks in `RegistrationsController` (it checks
`IsInRole(Admin)` only), so a SuperAdmin cannot act on other people's registrations there.
² The `/mail-planner` page opens for any non-merchant user, but the mail-plan API is Admin-only.
³ UI only. The participant API endpoints still accept a TL token (see Known security gaps).

Callers outside the role system:

| Caller | Access | How |
|---|---|---|
| facerog-engine | `GET /api/face-engine/events/{id}/gallery`, `GET /api/face-engine/events/finished` | Shared key in `X-Face-Engine-Key` (`FaceEngine:ApiKey`) |
| Anyone with a pass link | `GET /api/passes/{token}` and QR / meal-stub images | The GUID token is the key |

### Where it is enforced

| Layer | Location | Mechanism |
|---|---|---|
| Backend (authoritative) | `backend/EventRegistration.Api/Controllers/*` | `[Authorize(Roles = ...)]`, `[Authorize(Policy = "CanConfirmRegistrations")]`, `User.IsInRole(...)` inside actions |
| Backend policy | `Program.cs` (`AddAuthorization`) | `CanConfirmRegistrations` = Admin OR SuperAdmin OR claim |
| Backend role list | `Models/Entities.cs` (`Roles`), `UsersController.AssignableRoles` | Admin, Participant, TL, Merchant assignable |
| Frontend (UI only) | `frontend/web/src/app/core/guards.ts` | `authGuard`, `adminGuard`, `confirmerGuard`, `nonMerchantGuard`, `merchantGuard`, `isTeamLead`, `voucherGuard`; `homeFor` picks the landing page |
| Frontend routes | `frontend/web/src/app/app.routes.ts` | Guards attached per route |
| Frontend helpers | `frontend/web/src/app/core/auth.service.ts`, `models.ts` | `isAdmin`, `isMerchant`, `canConfirm`; role type union |

Changing a role's capabilities means editing both projects and redeploying.

### Frontend routes by guard

| Route | Guard | Who gets in |
|---|---|---|
| `/login`, `/register`, `/verify/:token`, `/e/:slug`, `/r/:token` | none | Everyone |
| `/home`, `/events`, `/my-events` | `authGuard` | Any signed-in user except TL |
| `/calendar`, `/mail-planner`, `/account` | `nonMerchantGuard` | Signed in, not Merchant, not TL |
| `/admin/events`, `/admin/events/:id` | `confirmerGuard` | Admin, SuperAdmin, Can confirm (not TL) |
| `/team` | `participantOrTlGuard` | TL (view + manage own team) · Participant (view own team, read-only) |
| `/voucher` (prototype) | `voucherGuard` | Participant, TL |
| `/admin/dashboard`, `/admin/users`, `/admin/teams`, `/admin/settings`, `/admin/events/new`, `/admin/events/:id/edit`, `/scanner/:eventId`, `/scanner/:eventId/tv` | `adminGuard` | Admin, SuperAdmin |
| `/merchant/scan` | `merchantGuard` | Merchant, Admin, SuperAdmin |

---

## How user accounts are created

Every path goes through the backend. Each creates a row in `Users`.

| # | Path | Who | Endpoint / code | Role given | Password |
|---|---|---|---|---|---|
| 1 | **Self-registration** (`/register` page) | Anyone | `POST /api/auth/register` (`AuthController`) | Participant | Chosen by the user |
| 2 | **Admin creates a user** (Users page form) | Admin, SuperAdmin | `POST /api/users` (`UsersController`) | Admin, Participant, TL or Merchant; Can confirm optional | Set by the admin |
| 3 | **CSV import** (≤ 5000 rows) | Admin, SuperAdmin | `POST /api/users/import` (`UsersController`) | From CSV column (blank = Participant); Can confirm always off | Shared default |
| 4 | **Team Lead adds a member** (Team page) | TL | `POST /api/teams/mine/members` (`TeamsController`) | Participant (never from input); joins the TL's team | Shared default |
| 5 | **Seeder** (empty database only) | System at startup | `Data/DbSeeder.cs` | 1 Admin + 5 demo Participants | Fixed demo passwords |

These do **not** create a user account:

| Path | What it does |
|---|---|
| Guest event registration (`POST /api/registrations/guest/{slug}`) | Creates a `Registrations` row with `Guest*` fields, no user |
| Offline guest sync (`POST /api/registrations/offline-sync`) | Uploads guest registrations captured offline (Pending) |
| Invite link (`POST /api/auth/invite/{token}`) | Signs in an existing invitee; otherwise sends them to the event page |
| Email verification (`POST /api/auth/verify`) | Marks an existing user verified (only when `App:RequireEmailVerification=true`) |

## How people register for events

| Path | Who | Result |
|---|---|---|
| Account registration (`POST /api/registrations`) | Signed-in user | Registration linked to `UserId`, **Pending** |
| Guest registration (`/e/{slug}`) | Anyone, no account | Guest registration, **Pending**; editable later by link |
| Offline guest capture + sync | Device without internet | Queued on device, synced as **Pending** |
| Invite link | Invited email | Pre-fills the event page; invite-only events accept only the invited email |

All new registrations are **Pending**. No pass is sent until an Admin or a user with *Can confirm
registrations* confirms it (see `.claude/rules/registration-flow.md`).

---

## Known security gaps

| Issue | Where | Rule broken |
|---|---|---|
| Seeder runs in every environment, so a new Production DB gets the demo admin and demo users | `Program.cs` calls `DbSeeder.SeedAsync` without an environment check | Seed demo admin in Development only |
| All CSV-imported users share one short, numeric default password with no forced change | `UsersController.DefaultImportPassword` | Password policy (min 8, letter + digit) |
| Accounts a Team Lead creates share one short default password with no forced change | `TeamsController.DefaultMemberPassword` | Password policy (min 8, letter + digit) |
| A TL is limited to the Team page in the SPA only; participant API endpoints still accept a TL token | `core/guards.ts` vs. controllers | Server is authoritative |
| Self-registration returns 409 "account already exists" | `AuthController.Register` | Register must not reveal existing emails |
| Invite link signs the invitee in from the link alone; token not single-use or time-limited | `AuthController.ResolveInvite` | Invite tokens single-use, time-limited; no auth from link alone |
| SuperAdmin missing from ownership checks | `RegistrationsController` | Consistency (functional bug) |
