# Registration & check-in flow rules

Source: `admin-guide` and `participant-guide` skills. Preserve these behaviors when changing code.

- New registrations are **Pending**; **no pass is sent** until an admin (or a user with **Can
  confirm registrations**) confirms.
- A pass (and other registrant messages) goes Viber → email or SMS → email only (never Viber ↔
  SMS); the email fallback follows `Viber:FallbackToEmail` / `Sms:FallbackToEmail`. A phone whose
  country code is not in the Phone country table for the active channel skips mobile and always
  gets email. Viber/SMS are tried once and never retried (mail plans included); failed registrant
  emails retry 5× in the background. An email failure never falls back to Viber/SMS.
  A Viber failure Sinch reports later (delivery webhook, within 24 h) on the message that decides
  delivery (pass / confirmation, meeting update, kit claimed, edit link) also queues the email
  once, under the same rule (mobile preferred, `Viber:FallbackToEmail` on, has an email).
  The mobile channel is the admin's Settings choice only when both Viber and SMS are set up;
  otherwise the one that is set up is used, and the choice is not offered.
  Exception: the organizer **Viber broadcast** is Viber only (optional fallback); it reports failed numbers, never counts them as sent.
- Every phone entry (user master, My Account, sign-up, guest registration and edit) is a
  country-code dropdown + number (`app-phone-field`) and accepts only codes in the Phone country
  table (server too); CSV import accepts any valid number. Unsupported numbers are flagged on the user list and My Account.
- Confirmation sends the QR pass (and meal stubs, and for online/hybrid the meeting link +
  `invite.ics`) via the registrant's chosen channel (Email / Viber / Both; SMS is text-only).
- **Only confirmed passes can be checked in**; the scanner returns "Not confirmed" otherwise.
- Admins can cancel **pending** registrations only; confirmed or checked-in ones are kept.
  Participants cannot cancel after check-in.
- Each sub-event is its own registration/pass/confirmation/check-in; a sub-event with
  registrations cannot be removed; check-in opens only on its own dates.
- Categories come from the API's `categories.json` (Senior / Regular here; each installation sets
  its own list, or none, which hides every category choice, pill and warning).
- Event **category** only **warns** a signed-in player whose **team category**
  differs before registering (Cancel pre-selected); it never blocks, and guests, admins and
  players not on a team get no warning. The message names both categories, not ages. It shows
  on the event page's Register and on Browse Events' Register (card and Details); accepting it on
  Browse Events means the event page doesn't ask again.
- **Online** events: no QR pass, no meal stubs. **Hybrid**: pass + link. The meeting link is never
  shown on public pages — confirmed registrants only. Meeting changes/cancellations must update or
  remove calendar entries automatically.
- Visible in *Browse Events* only when **Published** or **Closed**, and public link **on**. Closed
  events sit under the **Closed** tab and refuse registration.
- Check-in scanner is admin-only and opens only between event start and end.
- Meal stubs: redeemable once, on valid dates only; Merchant role sees only the Meal Stub Scanner.
- Roles: Admin (all) · Participant · Participant + Can confirm (Attendance actions only) ·
  Merchant. Grant changes apply at next sign-in.
- Destructive actions (delete user/event, cancel registrations, discard changes) require a
  confirmation dialog with the safe option pre-selected.
- Offline check-ins queue on the device and sync when online; meal-stub scanning needs internet.
- Face check-in requires clear, close, front-facing, multi-frame confirmation; face data is
  optional and consent-based.
