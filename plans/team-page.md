# Plan: Teams module (team page)

| | |
|---|---|
| **Status** | 🚧 In progress |
| **Last updated** | 2026-11-11 |
| **Owner** | _TBD_ |

> **Keep this plan current.** When work on the Teams module starts, update this file first: set
> **Status**, resolve or answer the open questions, and adjust the tables and steps to match what is
> actually built. Tick steps as they land.
>
> **Section markers:** when a section is complete, add `✅ Done (YYYY-MM-DD)` to its heading
> (e.g. `### Backend ✅ Done (2026-11-12)`); use `🚧 In progress` while it is partly done.

## Goal

Let admins group users into teams, each led by a Team Lead (`TL` role), and give TLs and team
members their own pages. The data model is deliberately generic so it can be extended later.

### Scope of the first build (2026-11-11)

- **Admins** (Admin / SuperAdmin) create, edit and delete teams, assign the Team Lead, and add or
  remove members on the admin **Teams** page.
- A **TL can open only the Team page** (`/team`). Home, Browse Events, My Events, Calendar, Mail
  Planner and Edit account all redirect there. This is enforced in the SPA (routes, landing page,
  menu); the API endpoints a TL token can call are unchanged.
- On the Team page the TL **adds Participants by first name, M.I., last name and email**:
  - unknown email → a new Participant account is created with the shared default temporary
    password (no forced change, no email sent) and joins the team;
  - email of an existing **Participant** → that user joins the team, account untouched;
  - email of any other role, or of someone already in a team → refused.
- The TL page is add-only; removing members and editing the team stay with admins.
- **Member caps: Senior = 8, Regular = 10** (`TeamCategories.MaxMembers`). The TL is not counted.
- Not built yet: a page for team *members* (the `isTeamMember` shortcut stays).

## What already exists

- Role `TL` (Team Lead): `Roles.TeamLead = "TL"` in `backend/EventRegistration.Api/Models/Entities.cs`;
  assignable on the Users page and by CSV import. Same capabilities as Participant for now.
- Frontend: `AuthService.isTeamLead` / `isTeamMember` signals and the `isTeamLead` / `isTeamMember`
  route guards in `frontend/web/src/app/core/guards.ts`.
- **Temporary:** `isTeamMember` returns true for TLs only (`shortcut:` comment in `auth.service.ts`).
  Replace it once the tables below exist.

## Data model

Header / details: one `Teams` row per team, one `TeamMembers` row per user in the team.

### `Teams` (header)

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | int | | PK, identity |
| CompanyId | int | ✓ | Tenant id, same as `Users` / `Events` |
| Description | nvarchar(200) | | Team name / description. Required, trimmed, 2–200 chars |
| Category | nvarchar(20) | | **`Senior` · `Regular`** (C# constants class `TeamCategories`, same pattern as `EventStatus`). Sets the member cap: Senior 8, Regular 10 |
| TeamLeadUserId | int | | FK → `Users.Id`, **Restrict** on delete. The user should have role `TL` |
| CreatedAt | datetime2 | | DB default `GETUTCDATE()` |
| CreatedByUserId | int | | Admin who created it (no FK, like `Events.CreatedByUserId`) |

### `TeamMembers` (details)

Generic membership list; add columns later (member role in team, joined/left dates, etc.).

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | int | | PK, identity |
| TeamId | int | | FK → `Teams.Id`, **Cascade** on delete |
| UserId | int | | FK → `Users.Id`, **Restrict** on delete |
| AddedAt | datetime2 | | DB default `GETUTCDATE()` |

Indexes and constraints:
- **Unique** index on `TeamMembers.UserId`: a user is in at most one team (question 1).
- **Unique** index on `Teams.TeamLeadUserId`: a TL leads at most one team (question 2).
- Index on `TeamMembers.TeamId` (member list and cap count).

### Relationships

```
Users 1─* Teams (as TeamLeadUserId)
Teams 1─* TeamMembers *─1 Users
```

## Open questions

Answered (2026-11-11):

1. Can a user belong to more than one team? **No**, at most one (unique index).
2. Can a TL lead more than one team? **No**, one team per TL (unique index).
3. Is the TL also listed in `TeamMembers`? **No**, implied by `TeamLeadUserId`; the TL does not
   count toward the member cap.
4. Must `TeamLeadUserId` have role `TL`? **Yes**, enforced on create / update. While a user leads a
   team an admin cannot change their role or delete them; reassign the team first.
5. Who manages teams? **Admin / SuperAdmin** (create, edit, delete, add / remove members). A TL can
   only add members to their own team.
8. Do admins pass the `isTeamLead` / `isTeamMember` guards? **No**; admins use the admin Teams page.
9. How is membership sent to the SPA? **`GET /api/teams/mine`** (204 when the TL has no team yet).

Still open:

6. What can a TL do beyond adding members (see their registrations, attendance, confirm for team only)?
7. What does a team member see on the team page?

## Steps

### Backend ✅ Done (2026-11-11)
- [x] Add `Team`, `TeamMember` entities and `TeamCategories` constants (`Models/`).
- [x] Register `DbSet`s and relationships in `AppDbContext` (keys, FKs, unique index).
- [x] Add EF migration (`dotnet ef migrations add Teams`).
- [x] `TeamsController` (`[Authorize(Roles = Admin, SuperAdmin)]`): list, get, create, update, delete teams; add / remove members.
- [x] Request DTOs with validation (description length, category value, user ids exist, TL user has role `TL`).
- [x] TL read endpoint `GET /api/teams/mine` and add-member endpoint `POST /api/teams/mine/members`, scoped to the caller's own team.
- [x] Expose membership to the SPA (answer to question 9).
- [x] Member caps (Senior 8, Regular 10) on both add paths and on category change.
- [x] `UsersController`: a lead or member keeps their role; a lead cannot be deleted; deleting a member drops the membership.

### Tests ✅ Done (2026-11-11)
- [x] `backend/EventRegistration.Api.Tests` (xUnit, real API over HTTP, own throwaway database): 33 tests for the TL add-member flow, admin team management and the Users rules.
- [x] `frontend/web/src/app/core/guards.spec.ts` (Vitest via `ng test`): 11 tests for the TL lock-down.

### Frontend 🚧 In progress
- [x] Models for team header and members in `core/models.ts`; API calls in `core/api.service.ts`.
- [x] Admin page: Teams list + form (description, category, TL picker filtered to `TL` users, member picker).
- [x] Team page for TLs (`/team`, `isTeamLead`); a TL is redirected there from every other page.
- [ ] Team page for members (question 7 still open).
- [ ] Replace the `isTeamMember` shortcut with a real membership check.
- [x] Add the admin "Teams" home tile.

### Docs ✅ Done (2026-11-11)
- [x] Add `Teams` and `TeamMembers` to `documentation/database-table.md`.
- [x] Update TL and team capabilities in `documentation/roles.md`.

## Change log

| Date | Change |
|---|---|
| 2026-11-10 | Draft plan created: tables specified, guards and TL role already in place. |
| 2026-11-11 | Work started. Scope set: admin team management, TL-only Team page with add-member (new account or existing Participant), member caps Senior 8 / Regular 10. Questions 1-5, 8, 9 answered; indexes changed to unique per user and per lead. |
| 2026-11-11 | Backend, tests and docs done; admin Teams page and TL Team page built. Left: a page for team members and the real `isTeamMember` check. |
| 2026-11-11 | Teams can now be invited to events: the invitation composer's **Invite teams** picker reads `GET /api/teams` and `GET /api/teams/{id}` (see `plans/invite-teams.md`). No Teams API change. |
