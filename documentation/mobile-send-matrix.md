# Mobile (Viber / SMS) send matrix

Every place the API sends a Viber or SMS message, and whether the code falls back to email.
Checked against the code on 2026-11-20 (branch `DEV_1783`).

**What counts as a failure.** A send is "failed" only when the provider **rejects the request**
(error response or exception). A message the provider accepted but could not deliver later (for
example, a number with no Viber account) is first counted as **sent**. For **Viber**, Sinch then
reports the failure by webhook (`SinchWebhooksController`); on the rows marked ✅ in the last
column the email is queued then (once, through the background email retry), when the registrant
prefers mobile, `Viber:FallbackToEmail` is on and they have an email. Reports more than 24 h after
the send are ignored. SMS has no delivery reports. See `plans/viber-delivery-report-fallback.md`.

**Shared rule (`PassService.DeliverAsync`).** Used by the rows marked *DeliverAsync*:
- Channel **Viber/SMS preferred**: the active mobile provider only, then email when
  `Viber:FallbackToEmail` / `Sms:FallbackToEmail` is on.
- Phone **country not in the Phone country table** for the active channel, or **no phone**: mobile
  is skipped, and email is sent whatever `FallbackToEmail` says.
- Channel **Both**: mobile and email are each sent; neither falls back.
- A failed **email** never falls back to mobile; it is queued for the background email retry (5×).

## Matrix

| # | Message | Trigger | Code (send) | Provider | Email fallback on rejected send? | Email fallback on later delivery failure? | Notes |
|---|---|---|---|---|---|---|---|
| 1 | Entry pass (text) | Admin / "Can confirm" user confirms a registration | `AttendanceController.cs:608` → `PassService.SendConfirmationAsync` (`PassService.cs:123`) | Active (Viber or SMS) | ✅ Yes, *DeliverAsync* | ✅ Viber only (late failure report) | Online-only events send a confirmation text instead of the pass (`PassService.cs:124`), same fallback |
| 2 | Entry pass QR image | Same send as #1 | `SinchViberService.cs:103` | Viber only (SMS has no images; it gets a link) | ⚠️ Yes, but the text is already sent | ❌ No | Text goes first, image second, in one try. If only the image is rejected, the whole send counts as failed and the email goes out, so the registrant gets both |
| 3 | Entry pass resend | Participant or admin, "Resend pass" on My Events / registration | `RegistrationsController.cs:505` → `SendConfirmationAsync` | Active | ✅ Yes, *DeliverAsync* | ✅ Viber only (late failure report) | Same path as #1 |
| 4 | Entry pass resend (Attendance) | Admin "Resend pass" on Attendance | `AttendanceController.cs:102` (`emailOnly: true`) | — | n/a (email only) | n/a | No mobile message |
| 5 | Online join link (hybrid) | Follow-up to #1 when the pass was sent by mobile | `PassService.cs:131` | Active | ❌ No | ❌ No | Best effort; result ignored. Sent only after #1 succeeded on mobile, so no email carries the link |
| 6 | Meal stubs | Follow-up to #1 when the pass was sent by mobile | `PassService.cs:133` → `SendMealStubsAsync` | Active (Viber: images; SMS: text) | ❌ No | ❌ No | Result ignored. Viber without a public HTTPS base writes a summary to `outbox/` only |
| 7 | Event details changed | Admin edits an event (time, title, venue, link) | `EventsController.cs:599` → `SendMeetingUpdateAsync` (`PassService.cs:201`) | Active | ✅ Yes, *DeliverAsync*, only when a calendar invite exists | ✅ Viber only (late failure report) | With no invite, the email part is null, so there is no fallback |
| 8 | Registration / event cancelled | Admin cancels a registration, participant cancels, or event deleted | `AttendanceController.cs:66`, `RegistrationsController.cs:540`, `EventsController.cs:644` → `SendMeetingCancelAsync` (`PassService.cs:233`) | Active | n/a: sent as **Both**, so email (CANCEL invite) always goes too when there is one | ❌ No | Only for registrations that received a pass |
| 9 | Kit claimed | Admin / self kit claim | `AttendanceController.cs:187` → `SendKitClaimedAsync` (`PassService.cs:269`) | Active | ✅ Yes, *DeliverAsync*, when the registrant has an email | ✅ Viber only (late failure report) | |
| 10 | Edit-your-response link | Public page "lost my link" recovery | `RegistrationsController.cs:216` → `SendEditLinkAsync` (`PassService.cs:287`) | Active | ✅ Yes, *DeliverAsync*, when the registrant has an email | ✅ Viber only (late failure report) | |
| 11 | Mail plan message | Mail plan send (channel not Email-only) | `MailPlanDispatcher.cs:101` | Active | ✅ Yes, when `FallbackToEmail` is on, the receiver has an email and was not already emailed | ❌ No | Unlisted country: skipped, then email. A failed fallback email is retried with the plan |
| 12 | Mail plan reminder | Receiver's "remind me" time is reached | `MailPlanDispatcher.cs:178` | Active | ❌ No | ❌ No | Result ignored. Email is sent only when the receiver's channel includes email or there is no phone |
| 13 | Viber broadcast | Organizer types a message in the Viber broadcast dialog | `EventsController.cs:690` | **Viber only**, even when SMS is the active channel | ⚠️ Only when the dialog's **Fallback to email** box is ticked | ❌ No | Ignores `Viber:FallbackToEmail`. An unlisted country counts as failed (then email, if ticked) |
| 14 | Email retry | Background retry of a failed registrant email | `EmailRetryDispatcher.cs:85-90` (`isRetry: true`) | — | n/a (email only) | n/a | Never sends Viber/SMS |

## Gaps

- **Later delivery failures:** Viber falls back on rows 1, 3, 7, 9 and 10 only (status is
  recorded for every Viber message). Follow-ups (#2, #5, #6), mail plans and broadcasts never fall
  back on a late failure. SMS: no delivery reports; not planned yet. The actual Sinch reason code
  for "number has no Viber" is still to be recorded from a live test.
- **#5, #6, #12:** mobile-only follow-ups with no fallback.
- **#2:** a rejected QR image triggers the email even though the text already arrived (the
  registrant gets the details twice).
- **#13:** the fallback is the organizer's choice per broadcast, not config.
