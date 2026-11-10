# Plan: Teams module (team page)

| | |
|---|---|
| **Status** | Not started (draft plan) |
| **Last updated** | 2026-11-10 |
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
| Category | nvarchar(20) | | **`Senior` · `Regular`** (C# constants class `TeamCategories`, same pattern as `EventStatus`) |
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
- Unique `(TeamId, UserId)`: a user is listed once per team.
- Index on `TeamMembers.UserId` (membership lookup at sign-in).
- Index on `Teams.TeamLeadUserId`.

### Relationships

```
Users 1─* Teams (as TeamLeadUserId)
Teams 1─* TeamMembers *─1 Users
```

## Open questions (answer when the module starts)

1. Can a user belong to more than one team?
2. Can a TL lead more than one team?
3. Is the TL also listed in `TeamMembers`, or implied by `TeamLeadUserId`?
4. Must `TeamLeadUserId` have role `TL` (enforced on the server), and what happens to the team if that user's role changes?
5. Who manages teams: Admin / SuperAdmin only?
6. What can a TL do beyond a Participant (see team members, their registrations, attendance, confirm for team only)?
7. What does a team member see on the team page?
8. Do admins pass the `isTeamLead` / `isTeamMember` guards?
9. How is membership sent to the SPA: a JWT claim / `AuthResponse` field (`teamIds`), or a `GET /api/teams/mine` call? A claim only refreshes at next sign-in.

## Steps

### Backend
- [ ] Add `Team`, `TeamMember` entities and `TeamCategories` constants (`Models/`).
- [ ] Register `DbSet`s and relationships in `AppDbContext` (keys, FKs, unique index).
- [ ] Add EF migration (`dotnet ef migrations add Teams`).
- [ ] `TeamsController` (`[Authorize(Roles = Admin, SuperAdmin)]`): list, get, create, update, delete teams; add / remove members.
- [ ] Request DTOs with validation (description length, category value, user ids exist, TL user has role `TL`).
- [ ] TL / member read endpoint, e.g. `GET /api/teams/mine`, with ownership checks.
- [ ] Expose membership to the SPA (answer to question 9).

### Frontend
- [ ] Models for team header and members in `core/models.ts`; API calls in `core/api.service.ts`.
- [ ] Admin page: Teams list + form (description, category, TL picker filtered to `TL` users, member picker).
- [ ] Team page for TLs and members; routes guarded with `isTeamLead` / `isTeamMember`.
- [ ] Replace the `isTeamMember` shortcut with a real membership check.
- [ ] Add menu / home tiles for the new pages.

### Docs
- [ ] Add `Teams` and `TeamMembers` to `documentation/database-table.md`.
- [ ] Update TL and team capabilities in `documentation/roles.md`.

## Change log

| Date | Change |
|---|---|
| 2026-11-10 | Draft plan created: tables specified, guards and TL role already in place. |
