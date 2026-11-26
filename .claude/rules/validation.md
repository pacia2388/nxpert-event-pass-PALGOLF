# Validation rules

Source: `validation-spec` skill. The server is authoritative; the client is UX only.

## Where to enforce

- Every request DTO gets L2 annotations (`[ApiController]` auto-400); keep L3 business checks;
  match L4 EF `[MaxLength]` to the limits below.
- Return failures as RFC 7807 `ProblemDetails` with an `errors` map. Never leak stack traces.
- Trim all strings, reject control chars, cap request body (~256 KB), HTML-encode on display.

## Canonical field rules

| Field | Rule |
|-------|------|
| Name | Required, trimmed, 2–200 chars, not whitespace-only |
| Email | Trimmed, lower-cased, RFC 5322 format, ≤ 256 |
| Password | 8–128 chars, ≥ 1 letter, ≥ 1 digit and ≥ 1 symbol (not a letter, digit or space), not equal to email; checked and hashed in Unicode NFC |
| Set-password link | Single-use, expires after `App:PasswordSetupLinkHours` (default 72); the password it sets follows the Password rule; the token is cleared on use |
| Passwordless account | An admin-created, imported or captain-added account has no password (`PasswordHash = ""`); it can only be used after the link sets one |
| Phone | Optional, 7–40 chars, `^[0-9+()\-\s]+$`; stored `+<code><number>` (8–15 digits; `+63` = `+639` + 9 digits). Every form (`app-phone-field`): code must be in the Phone country table. CSV: any valid number; no `+` = PH |
| Birthdate | Required in UI/requests (DB nullable for legacy rows); `yyyy-MM-dd`, 1900-01-01 .. today; CSV also accepts `M/d/yyyy` |
| Organization | Optional, ≤ 200 |
| Department | Optional (only while enabled in `user-fields.json`), trimmed, ≤ 100 |
| Job role | Optional (only while enabled in `user-fields.json`), trimmed, ≤ 200 |
| Event title | Required, trimmed, 3–300 (an edit that keeps an older, shorter title is allowed) |
| Description | Optional, ≤ 4000 |
| Venue | Required, trimmed, ≤ 300 |
| Start / End | Both required; **End > Start**; new events Start ≥ today (warn, don't block, when editing past events) |
| Capacity | Blank = unlimited, else integer ≥ 1; on edit ≥ current registration count |
| Status | `Draft` \| `Published` \| `Closed` |
| Event category | Optional: a name from `categories.json` (Senior / Regular here) \| blank = none; trimmed, matched ignoring case, saved as listed; an edit may keep a name taken off the list. No categories configured = blank only |
| Team category | A name from `categories.json`, required while any are listed; blank when none are configured; an edit may keep a name taken off the list. Cap = its `TeamMaxMembers` (else `DefaultTeamMaxMembers`) |
| Rating | Integer 1–5 |
| Organization rating | Optional integer 1–5 |
| Feedback comment / What went well / What to improve | Optional, ≤ 1000 each |
| Anonymous feedback | Boolean; when true the organizer view shows "Anonymous" and never the person's name, email or phone |
| Token / slug | Tokens parse as GUID; slug `^[a-z0-9-]{1,100}$` |

## Per-request rules

- `GuestRegisterRequest`: name required; email format if present; phone pattern if present; **email
  or phone required** (pending product sign-off).
- `InviteRequest`: non-empty, each a valid email, ≤ 200 items, de-duplicated server-side.
- `CheckInRequest`: token must parse as GUID; invalid scans return `invalid`, never crash.
- Route params constrained (`{id:int}`), tokens GUID-parsed.

## Business rules that must hold

- Closed or full events refuse registration; capacity check must be atomic (transaction/guard).
- Invite-only events accept only the invited email.
- Duplicate email/phone registration reuses the existing pass.
- Cancel is blocked after check-in **on the server**, not just the UI.
- Feedback is accepted only from a registered attendee or the holder of that person's one-time feedback link, and only after the event ended; one answer per person/event (submitting again updates it).
- Login failure message is generic (no user enumeration).
- Verification and invite tokens expire and are single-use.
