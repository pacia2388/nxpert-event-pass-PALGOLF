# Block guest registration for account emails (duplicate attendee bug)

Status: ✅ Done (2026-11-11)

## Problem

Flow: Admin creates TL → TL adds an unregistered member (account auto-created, Participant, no
ID No.) → admin invites that email to an event → member opens the invite and fills the **public
(guest) form**.

- Test case 1: guest form saved a row with `UserId = null`, `GuestEmail = member email`. Admin
  confirms it.
- Test case 2: member completes the account and signs in. Event page shows **unregistered**; they
  register again → second row (`UserId = member`) → admin confirms → **two attendee entries**.

## Root cause

- `RegistrationsController.GuestRegister` stores guest rows (`UserId = null`) even when the email
  belongs to an existing account.
- Every "am I registered" check uses `r.UserId == uid` only, so the guest row is invisible to the
  account and the duplicate guard never fires.

## Decision

Refuse the guest form when the email already exists in `Users` (option B, chosen over linking
guest rows to accounts). The member signs in, or completes the TL registration link first.

| Trade-off | Decision |
|-----------|----------|
| Shared email, different ID No. | Block every time (no ID No. exception) |
| Public form reveals an email has an account | Risk accepted |
| TL member lost the registration email | TL resends the registration notice (exists) |
| `offline-sync` still creates guest rows | Leave for now |
| Existing duplicate rows | Leave for now (admin cancels / DB reset) |

## Steps

### Backend ✅ Done (2026-11-11)
- [x] Revert the earlier linking edits (`UserAccountHelper`, `AuthController`, `RegistrationsController`).
- [x] `GuestRegister`: after email validation, `409` with "This email already has an account. Please
      sign in to register. If your Team Lead added you, complete your registration first using the
      link they emailed you."

### Frontend ✅ Done (2026-11-11) — existing toast already shows the server message
- [x] Guest form shows the server message.

### Tests ✅ Done (2026-11-11)
- [x] Guest form with a TL member's email → 409, no row.
- [x] Guest form with an unknown email → 200.
- [x] Full suite green.

## Change log

| Date | Change |
|------|--------|
| 2026-11-11 | Plan created after diagnosis (linking approach). |
| 2026-11-11 | Switched to blocking guest registration for account emails; trade-offs decided. |
| 2026-11-11 | Implemented: guest form returns 409 for account emails; tests added; fixed fixture missing birthdate (suite 44/44). |
