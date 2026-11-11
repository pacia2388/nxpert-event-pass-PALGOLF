# Plan: Team page — "Send registration notice"

| | |
|---|---|
| **Status** | ✅ Done (2026-11-11) — built, API tests pass, manually tested |
| **Last updated** | 2026-11-11 |
| **Parent plan** | [team-page.md](20261111_team-page.md) |

## Goal

On the TL **Team page** (`/team`), the TL tells team members to **register in the app** (user
account registration — **not** event registration). No event is involved.

- **Send registration notice** button beside the `0 / 10 members` counter emails the **checked**
  members a link to the **user registration page**.
- Members table gets a **checkbox column** (left) with **check-all** in the header.
- `TeamMembers` gets **`NotificationFlag`**; the table shows a **Notified** column.

## The catch: members already have an account

When the TL adds a member, `TeamsController.AddMine` **already creates the user account**
(role Participant, temp password `char`, no ID No. / Designation / Birthdate / photo).
The public sign-up `POST /api/auth/register` then refuses that email:
`409 "An account with that email already exists."` So a plain link to `/register` fails for every
team member.

### Options

| | How | Pros | Cons |
|---|---|---|---|
| **A (recommended)** | Email carries a personal link `/register?team={token}`. Register page loads with name + email pre-filled (email locked); member fills ID No., Designation, Birthdate, password, photo, etc. Server **completes the existing account** instead of creating one. | Member does the full sign-up form; sets own password; no orphan accounts; TL flow unchanged | New token + 2 small auth endpoints |
| B | TL add stops creating the account; it only stores name + email as a pending member. `/register` with that email links the new user to the team. | Clean "register = create" | Rewrites TL add flow, `TeamMembers.UserId` becomes nullable, 33 existing tests change, admin Teams page changes |
| C | No register page. Email says "your account exists; sign in with temp password `char` and complete your profile under Edit account". | Smallest | Not "registration"; emails a shared password; profile fields stay optional |

Plan below follows **A** (chosen 2026-11-11).

### Registered vs unregistered

- **Unregistered** = an account the TL's add created (`RegistrationToken` set at creation, no
  expiry yet). Only these can get the notice.
- **Registered** = an existing Participant who joined, or a member who completed the link
  (`RegistrationToken` cleared). Exempt: checkbox disabled, server returns 400 if sent.
- Members added before this change count as registered (no token).

### Flow

Admin creates user with role TL, then a team with that TL → TL signs in, adds members on `/team`
→ TL checks unregistered members, clicks **Send registration notice** → member opens
`/register?team={token}` (signed out), completes the form → account updated, member signed in,
link spent, row shows **Registered**.

## Security (option A)

Per `.claude/rules/security.md` (invite/verification tokens):
- Token is a random GUID per member, **single-use** (cleared on completion) and **time-limited**
  (7 days). Re-sending mints a new token and invalidates the old one.
- The link alone never signs the member in; they must submit the form (with a new password).
- Prefill endpoint returns only first name, M.I., last name, email — nothing else.
- Endpoints sit under `/api/auth/*`, so they fall under the auth throttle.
- Email template HTML-encodes names; URL built server-side from `App:FrontendBaseUrl`.
- Password policy enforced server-side, same as `Register`.

## Data model ✅ Done (2026-11-11)

`TeamMembers` (existing table) — new columns:

| Column | Type | Null | Notes |
|---|---|---|---|
| NotificationFlag | bit | | Default `0`. Set to `1` when the registration notice email to this member is sent successfully |
| RegistrationToken | uniqueidentifier | ✓ | Current link token; cleared when the member completes registration |
| RegistrationTokenExpiresAt | datetime2 | ✓ | Sent time + 7 days |

Index: unique filtered index on `RegistrationToken` (not null).

- [x] Add the 3 properties to `TeamMember` (`Models/Entities.cs`); configure index in `AppDbContext`.
- [x] EF migration `dotnet ef migrations add TeamMemberRegistrationNotice` (flag default `false`).
- [x] `NotificationFlag` added to `TeamMemberDto` (`Models/Dtos.cs`) and `ToMemberDto` (token never sent to the SPA).
- [x] `documentation/database-table.md`: add the columns.

## Backend ✅ Done (2026-11-11)

### Send (TL)

- [x] `POST /api/teams/mine/registration-notice` body `{ userIds: int[] }`
      (`[Authorize(Roles = TL)]`, in `TeamsController`):
  - caller's team, else 409 "You have not been assigned a team yet.";
  - `userIds` empty → 400 "Select at least one member."; de-duplicated; **every id must be a member
    of the caller's own team**, else 400 and nobody is emailed;
  - per member: new `RegistrationToken` + expiry, URL
    `{App:FrontendBaseUrl}/register?team={token}`, send `EmailTemplates.TeamRegistrationNotice`
    via `EmailService.SendAsync`, `await Task.Delay(200)` (same throttle as the invite mailer);
  - success → `NotificationFlag = true`; a send that throws keeps the old flag, counted in `failed`;
  - returns `{ sent, failed, members: TeamMemberDto[] }`.
- [x] Inject `EmailService` + `IConfiguration` into `TeamsController`.

### Email template

- [x] `EmailTemplates.TeamRegistrationNotice(string memberName, string teamName, string leadName, string registerUrl)`
      using the existing `Shell(...)` and button style (same layout as `Invitation` / `Verification`):
      title "Complete your registration", "Hi {name}, {lead} added you to team {team}. Please
      complete your registration.", **Register now** button, paste-link fallback, "link expires in 7 days".
      All values HTML-encoded.

### Complete registration (member, anonymous)

- [x] `GET /api/auth/team-registration/{token:guid}` → `{ firstName, middleInitial, lastName, email }`;
      404 "This link is invalid or has expired." when unknown, expired, or already used.
- [x] `POST /api/auth/team-registration/{token:guid}` body = `RegisterRequest` fields minus email:
  - same validation as `Register` (names, ID No., Designation, Birthdate, password policy, photo);
  - updates the existing user (names, `FullName`, SenateNo, Level, Birthdate, Phone,
    Organization, Photo, **new password hash**); role untouched;
  - clears `RegistrationToken` / expiry (single use);
  - returns `AuthFor(user)` so the member is signed in, same as normal register.
- Shared validation: pull the checks at the top of `Register` into one private method used by both.

## Frontend ✅ Done (2026-11-11)

### Team page (`frontend/web/src/app/pages/team/`)

- [x] `core/models.ts`: `notificationFlag: boolean` on the team member model.
- [x] `api.service.ts`: `sendTeamRegistrationNotice(userIds)`.
- [x] Members table, columns `| ☐ (check all) | Name | Email | Added | Notified |`:
  - `mat-checkbox` per row bound to a `selected` signal (`Set<number>` of userIds);
  - header `mat-checkbox` = check all (checked when all, `indeterminate` when some);
    `aria-label` "Select all members" / "Select {name}";
  - **Notified**: `mark_email_read` icon + "Sent" when `notificationFlag`, else "Not yet" (muted);
  - default selection on load: members with `notificationFlag == false`; newly added member checked.
- [x] Button **Send registration notice (N)** (`mail` icon) in `.tp-head` beside `.tp-count`;
      disabled when nothing selected or sending.
- [x] Confirm dialog "Email the registration link to N members?" — Cancel pre-selected / Send
      (existing confirm dialog component, if one exists; else `MatDialog` with separate `.html`).
- [x] After send: replace members from response, reset selection, snackbar
      "Registration notice sent to N members." (+ "M failed") or server error.
- [x] `team.scss`: narrow checkbox column; header wraps on phone width.

### Register page (`frontend/web/src/app/pages/register/`)

- [x] Read `?team=` query param. When present: call the GET endpoint, pre-fill names + email,
      lock the email field, title "Complete your registration"; submit goes to the POST endpoint.
- [x] Invalid/expired token: show the 404 message with a "Sign in" link.
- [x] No `?team=`: page behaves exactly as today.
- [x] Route keeps `guestGuard` (a signed-in user opening the link is redirected as today).

## Tests ✅ Done (2026-11-11)

- [x] API tests in `TeamRegistrationNoticeTests.cs` (7 tests) pass — full suite 44/44 after fixing
      the test fixture's missing birthdate.
- [x] Manual test of the full flow (user).
- API (`TeamLeadAddsMembersTests` style):
  - TL sends to 2 of 3 members → `sent == 2`, only those 2 flagged, outbox has 2 emails;
  - userId from another team → 400, nobody emailed; empty `userIds` → 400; non-TL → 403;
  - GET with a valid token → prefill; POST completes → profile saved, can log in with the new
    password, token cleared; second POST with same token → 404;
  - expired token → 404; re-send invalidates old token.
- [ ] Frontend: register page with `?team=` pre-fills and locks email (Vitest) — skipped for now.

## Docs ✅ Done (2026-11-11)

- [x] `documentation/roles.md`: TL can send the registration notice to own team.
- [x] `team-page.md`: change-log row linking this plan.

## Out of scope

- Viber / SMS (email only).
- Date of last notice (`NotifiedAt`) and manual flag reset.
- Event registration of any kind.

## Change log

| Date | Change |
|---|---|
| 2026-11-11 | Draft plan created (event-based). |
| 2026-11-11 | Added recipient checkboxes with check-all, `TeamMembers.NotificationFlag`, Notified column. |
| 2026-11-11 | Rewritten: notice is about **user** registration, not events. Event picker removed. Added option A token link to complete the account the TL already created, since `/register` rejects existing emails. |
| 2026-11-11 | Option A built: migration `TeamMemberRegistrationNotice`, send + complete endpoints, team page checkboxes / Notified column, register page `?team=` mode. Registered members exempt (token-based). Tests written, not run. |
| 2026-11-11 | API tests pass (44/44); manual test done by user. Plan complete, moved to `plans/completed/`. Frontend Vitest for `?team=` still skipped. |
