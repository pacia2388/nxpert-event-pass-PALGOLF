# Roles, capabilities and user registration

## Roles

Roles are **hard-coded**. There is no roles or permissions table. A user has exactly one role, stored
as a string in `Users.Role` (see [database-table.md](database-table.md)).

| Role | Purpose |
|---|---|
| `SuperAdmin` | Same access as Admin wherever checked. **Cannot be assigned from the app** (not in the role picker, rejected by the API); only by editing the database. An existing SuperAdmin keeps the role when edited. |
| `Admin` | Runs the system: events, users, settings, attendance, scanners, mail. |
| `Participant` | Default role. Registers for events and uses their pass. |
| `TL` (Team Lead) | Assignable by an admin on the Users page (form, CSV import). **Same capabilities as Participant for now**; wherever this doc says Participant, TL is included. |
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

¹ Bug: SuperAdmin is left out of the ownership checks in `RegistrationsController` (it checks
`IsInRole(Admin)` only), so a SuperAdmin cannot act on other people's registrations there.
² The `/mail-planner` page opens for any non-merchant user, but the mail-plan API is Admin-only.

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
| Frontend (UI only) | `frontend/web/src/app/core/guards.ts` | `authGuard`, `adminGuard`, `confirmerGuard`, `nonMerchantGuard`, `merchantGuard` |
| Frontend routes | `frontend/web/src/app/app.routes.ts` | Guards attached per route |
| Frontend helpers | `frontend/web/src/app/core/auth.service.ts`, `models.ts` | `isAdmin`, `isMerchant`, `canConfirm`; role type union |

Changing a role's capabilities means editing both projects and redeploying.

### Frontend routes by guard

| Route | Guard | Who gets in |
|---|---|---|
| `/login`, `/register`, `/verify/:token`, `/e/:slug`, `/r/:token` | none | Everyone |
| `/home`, `/events`, `/my-events` | `authGuard` | Any signed-in user |
| `/calendar`, `/mail-planner`, `/account` | `nonMerchantGuard` | Signed in, not Merchant |
| `/admin/events`, `/admin/events/:id` | `confirmerGuard` | Admin, SuperAdmin, Can confirm |
| `/admin/dashboard`, `/admin/users`, `/admin/settings`, `/admin/events/new`, `/admin/events/:id/edit`, `/scanner/:eventId`, `/scanner/:eventId/tv` | `adminGuard` | Admin, SuperAdmin |
| `/merchant/scan` | `merchantGuard` | Merchant, Admin, SuperAdmin |

---

## How user accounts are created

Every path goes through the backend. Each creates a row in `Users`.

| # | Path | Who | Endpoint / code | Role given | Password |
|---|---|---|---|---|---|
| 1 | **Self-registration** (`/register` page) | Anyone | `POST /api/auth/register` (`AuthController`) | Participant | Chosen by the user |
| 2 | **Admin creates a user** (Users page form) | Admin, SuperAdmin | `POST /api/users` (`UsersController`) | Admin, Participant, TL or Merchant; Can confirm optional | Set by the admin |
| 3 | **CSV import** (≤ 5000 rows) | Admin, SuperAdmin | `POST /api/users/import` (`UsersController`) | From CSV column (blank = Participant); Can confirm always off | Shared default |
| 4 | **Seeder** (empty database only) | System at startup | `Data/DbSeeder.cs` | 1 Admin + 5 demo Participants | Fixed demo passwords |

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
| Self-registration returns 409 "account already exists" | `AuthController.Register` | Register must not reveal existing emails |
| Invite link signs the invitee in from the link alone; token not single-use or time-limited | `AuthController.ResolveInvite` | Invite tokens single-use, time-limited; no auth from link alone |
| SuperAdmin missing from ownership checks | `RegistrationsController` | Consistency (functional bug) |
