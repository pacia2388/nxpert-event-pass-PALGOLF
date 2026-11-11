# Plan: Invite teams from the invitation composer

| | |
|---|---|
| **Status** | ✅ Done (2026-11-11) |
| **Last updated** | 2026-11-11 |
| **Related** | `plans/team-page.md` |

## Goal

When composing an event invitation (event → **Invite** tab), an admin can click **Invite teams**
inside the To field and tick members of a team, or a whole team, to add them as recipients.

## Decisions

| # | Decision |
|---|---|
| 1 | **Members only.** The Team Lead is not a `TeamMembers` row and is not listed in the picker. |
| 2 | **One chip per person.** Picked members become ordinary email chips in To; there is no team group token. The invite goes to who was on the team when it was picked. |
| 3 | **No backend change.** The picker uses the existing `GET /api/teams` and `GET /api/teams/{id}` (Admin, SuperAdmin). |
| 4 | The button is in **To only**, not CC. |

## Behaviour

- The picker lists every team with its category and member count.
- A team's members load the first time the team is opened or ticked.
- Ticking a team picks all its members; ticking again clears them. A partly picked team shows a
  partial checkbox.
- Members already in To are shown ticked, disabled and marked "Already added".
- A team with no members cannot be ticked. A member without a usable email cannot be picked.
- **Add N to To** adds the picked addresses; the composer validates and de-duplicates them as usual.
- The server's limit of 500 recipient entries per send still applies.

## Steps

### Frontend ✅ Done (2026-11-11)
- [x] Selection helpers: `frontend/web/src/app/core/team-invite.util.ts`.
- [x] Picker dialog: `frontend/web/src/app/shared/invitation-composer/invite-teams-dialog.ts`.
- [x] **Invite teams** button in the To field: `shared/invitation-composer/invitation-composer.{ts,html,scss}`.

### Tests ✅ Done (2026-11-11)
- [x] `core/team-invite.util.spec.ts`
- [x] `shared/invitation-composer/invite-teams-dialog.spec.ts`
- [x] `shared/invitation-composer/invitation-composer.spec.ts`

### Docs ✅ Done (2026-11-11)
- [x] `documentation/events.md` (Invite tab row).
- [x] Change-log row in `plans/team-page.md`.

## Change log

| Date | Change |
|---|---|
| 2026-11-11 | Plan created and built: selection helpers, picker dialog, To-field button, specs and docs. |
