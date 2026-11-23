# Mobile (Viber / SMS) send matrix

Every place the API sends a Viber or SMS message, and whether the code falls back to email.
Checked against the code on 2026-11-23 (branch `DEV_1783`).

> **Planned change.** `plans/uniform-message-delivery.md` replaces these separate paths with one
> flow: **one template per message (SMS, Viber, email) → send on the preferred channel → fall
> back.** Each row below is followed by a **(new)** row with the planned behaviour; see
> [After the uniform flow](#after-the-uniform-flow). Until that work lands, the rows without
> "(new)" describe the code as it is.

**What counts as a failure.** A send is "failed" only when the provider **rejects the request**
(error response or exception). A message the provider accepted but could not deliver later (for
example, a number with no Viber account) is first counted as **sent**. For **Viber**, Sinch then
reports the failure by webhook (`SinchWebhooksController`); on the rows marked ✅ in the "later
delivery failure" column the email is queued then (once, through the background email retry),
when the registrant prefers mobile, `Viber:FallbackToEmail` is on and they have an email. Reports
more than 24 h after the send are ignored. SMS has no delivery reports. See
`plans/completed/20261123_viber-delivery-report-fallback.md`.

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
| 1 | Entry pass (text) | Admin / "Can confirm" user confirms a registration | `AttendanceController.cs:608` → `PassService.SendConfirmationAsync` (`PassService.cs:124`) | Active (Viber or SMS) | ✅ Yes, *DeliverAsync* | ✅ Fallback for Viber channel only (late failure report) | Online-only events send a confirmation text instead of the pass (`PassService.cs:125`), same fallback |
| 1 (new) | Entry pass (text + QR + join link + meal stubs) | Same | `MessageTemplates.Pass` → `MessageDispatcher` (shared send → fallback) | Active (Viber or SMS) | ✅ Yes, shared rule | ✅ Fallback for Viber channel only, once per Viber group | One message replaces #1, #2, #5 and #6. Online-only events: confirmation text with the join link, same flow |
| 2 | Entry pass QR image | Same send as #1 | `SinchViberService.cs:110-112` | Viber only (SMS has no images; it gets a link) | ⚠️ Yes, but the text is already sent | ❌ No (the image is sent untagged) | Text goes first, image second, in one try. If only the image is rejected, the whole send counts as failed and the email goes out, so the registrant gets both |
| 2 (new) | Entry pass QR image | Same send as #1 | Part of the Pass Viber group | Viber (SMS: pass page link in the text) | ✅ Yes, with the pass: any rejected part = failed → email (decision 1) | ✅ Yes: a late failure on any part queues the email once for the group | Registrant may still get the Viber text plus the email when only the image fails |
| 3 | Entry pass resend | Participant or admin, "Resend pass" on My Events / registration | `RegistrationsController.cs:505` → `SendConfirmationAsync` | Active | ✅ Yes, *DeliverAsync* | ✅ Fallback for Viber channel only (late failure report) | Same path as #1 |
| 3 (new) | Entry pass resend | Same | `MessageTemplates.Pass` → `MessageDispatcher` (shared send → fallback) | Active | ✅ Yes, shared rule | ✅ Fallback for Viber channel only, once per group | Same as #1 (new) |
| 4 | Entry pass resend (Attendance) | Admin "Resend pass" on Attendance | `AttendanceController.cs:102` (`emailOnly: true`) | — | n/a (email only) | n/a | No mobile message |
| 4 (new) | Entry pass resend (Attendance) | Same | `MessageTemplates.Pass`, email part only | — | n/a (email only) | n/a | Unchanged |
| 5 | Online join link (hybrid) | Follow-up to #1 when the pass was sent by mobile | `PassService.cs:132` | Active | ❌ No | ❌ No | Best effort; result ignored. Sent only after #1 succeeded on mobile, so no email carries the link |
| 5 (new) | Online join link (hybrid) | — | In the Pass message text (SMS and Viber) | Active | ✅ Yes, with the pass | ✅ Fallback for Viber channel only, with the pass group | **No longer a separate send** |
| 6 | Meal stubs | Follow-up to #1 when the pass was sent by mobile | `PassService.cs:134` → `SendMealStubsAsync` (`SinchViberService.cs:182`) | Active (Viber: images; SMS: text) | ❌ No | ❌ No | Result ignored. Viber without a public HTTPS base writes a summary to `outbox/` only |
| 6 (new) | Meal stubs | — | In the Pass message: Viber images in the group, SMS list in the text | Active (Viber: images; SMS: text) | ✅ Yes, with the pass | ✅ Fallback for Viber channel only, with the pass group | **No longer a separate send.** Longer SMS (more segments) |
| 7 | Event details changed | Admin edits an event (time, title, venue, link) | `EventsController.cs:607` → `SendMeetingUpdateAsync` (`PassService.cs:202`) | Active | ✅ Yes, *DeliverAsync*, only when a calendar invite exists | ✅ Fallback for Viber channel only (late failure report) | With no invite, the email part is null, so there is no fallback |
| 7 (new) | Event details changed | Same | `MessageTemplates.MeetingUpdate` → `MessageDispatcher` (shared send → fallback) | Active | ✅ Yes, shared rule, always (decision 6) | ✅ Fallback for Viber channel only | Email template always exists ("details changed", `.ics` when there is an invite) |
| 8 | Registration / event cancelled | Admin cancels a registration, participant cancels, or event deleted | `AttendanceController.cs:66`, `RegistrationsController.cs:540`, `EventsController.cs:652` → `SendMeetingCancelAsync` (`PassService.cs:234`) | Active | n/a: sent as **Both**, so email (CANCEL invite) always goes too when there is one | ❌ No | Only for registrations that received a pass |
| 8 (new) | Registration / event cancelled | Same | `MessageTemplates.MeetingCancel` → `MessageDispatcher`, mobile-preferred sent as **Both** | Active | n/a: sent as **Both**, so the CANCEL email always goes too when there is an invite | ❌ No (the email always goes) | Exception kept (decision 5, decided 2026-11-23): removes the calendar entry; email-preferred get the email only |
| 9 | Kit claimed | Admin / self kit claim | `AttendanceController.cs:187` → `SendKitClaimedAsync` (`PassService.cs:270`) | Active | ✅ Yes, *DeliverAsync*, when the registrant has an email | ✅ Fallback for Viber channel only (late failure report) | |
| 9 (new) | Kit claimed | Same | `MessageTemplates.KitClaimed` → `MessageDispatcher` (shared send → fallback) | Active | ✅ Yes, shared rule, when the registrant has an email | ✅ Fallback for Viber channel only | Same behaviour; text from the shared template |
| 10 | Edit-your-response link | Public page "lost my link" recovery | `RegistrationsController.cs:216` → `SendEditLinkAsync` (`PassService.cs:288`) | Active | ✅ Yes, *DeliverAsync*, when the registrant has an email | ✅ Fallback for Viber channel only (late failure report) | |
| 10 (new) | Edit-your-response link | Same | `MessageTemplates.EditLink` → `MessageDispatcher` (shared send → fallback) | Active | ✅ Yes, shared rule, when the registrant has an email | ✅ Fallback for Viber channel only | Same behaviour; text from the shared template |
| 11 | Mail plan message | Mail plan send (channel not Email-only) | `MailPlanDispatcher.cs:101` | Active | ✅ Yes, when `FallbackToEmail` is on, the receiver has an email and was not already emailed | ❌ No | Unlisted country: skipped, then email. A failed fallback email is retried with the plan. Viber/SMS text keeps link addresses after the link text ("See the list (https://…)") |
| 11 (new) | Mail plan message | Same | `MessageTemplates.MailPlan` → `MessageDispatcher` (shared send → fallback) for mobile-preferred receivers | Active | ✅ Yes, shared rule (not when already emailed) | ✅ Fallback for Viber channel only, if decision 4 is accepted | Email-only receivers keep the BCC batches (decision 2). Link addresses kept in the Viber/SMS text, as today |
| 12 | Mail plan reminder | Receiver's "remind me" time is reached | `MailPlanDispatcher.cs:178` | Active | ❌ No | ❌ No | Result ignored. Email is sent only when the receiver's channel includes email or there is no phone |
| 12 (new) | Mail plan reminder | Same | `MessageTemplates.MailReminder` → `MessageDispatcher` (shared send → fallback) | Active | ✅ Yes, shared rule (decision 8) | ✅ Fallback for Viber channel only, if decision 4 is accepted | No longer best effort |
| 13 | Viber broadcast | Organizer types a message in the Viber broadcast dialog | `EventsController.cs:698` | **Viber only**, even when SMS is the active channel | ⚠️ Only when the dialog's **Fallback to email** box is ticked | ❌ No | Ignores `Viber:FallbackToEmail`. An unlisted country counts as failed (then email, if ticked) |
| 13 (new) | Viber broadcast | Same | `MessageTemplates.Broadcast` → `MessageDispatcher` (shared send → fallback), Viber forced | **Viber only** (kept by design, decision 3) | ⚠️ Only when the dialog box is ticked (unchanged) | ✅ If decision 4 is accepted, only when the box is ticked | Name and dialog unchanged; ignores `Viber:FallbackToEmail` as today |
| 14 | Email retry | Background retry of a failed registrant email | `EmailRetryDispatcher.cs:85-90` (`isRetry: true`) | — | n/a (email only) | n/a | Never sends Viber/SMS |
| 14 (new) | Email retry | Same | `EmailRetryDispatcher` rebuilds the email part of the same template | — | n/a (email only) | n/a | Never sends Viber/SMS |

## After the uniform flow

What `plans/uniform-message-delivery.md` changes, in short:

1. **One template per message.** Each message (pass, meeting update, meeting cancel, kit claimed,
   edit link, mail plan, reminder, broadcast) is defined once with three parts:
   - **SMS**: text, with links in place of images (pass page, join link, meal stub list);
   - **Viber**: text plus images (QR pass, meal stubs);
   - **Email**: subject, HTML, inline images, attachments (`invite.ics`).
2. **One flow: send → fall back.** Every message goes through one dispatcher with the shared rule
   above (preferred channel, then email when `FallbackToEmail` is on; unlisted country or no phone
   → email; Both → both; failed email → background retry). Exceptions are named in the plan, not
   hidden in separate code: the cancellation email (#8) and the Viber-only broadcast (#13).
3. **Follow-ups join the pass.** Rows #2, #5 and #6 stop being separate sends, so they can no
   longer fail without a fallback.
4. **Late Viber failures per group.** All parts of one Viber send share a group; a `FAILED`
   report on any part queues the email once for the whole group.
5. **Wording is locked.** `MessageTemplateSnapshotTests` (snapshots in
   `backend/EventRegistration.Api.Tests/Snapshots/Messages/`) record today's text on every
   channel. A change to them must be intended and reviewed.

**What a registrant notices:** a mobile registrant whose pass follow-ups fail (join link, meal
stubs) now gets the full email instead of missing them. The pass text on SMS also lists the join
link and meal stubs (longer message, more SMS segments).

## Gaps (today)

- **Later delivery failures:** Viber falls back on rows 1, 3, 7, 9 and 10 only (status is
  recorded for every Viber message). Follow-ups (#2, #5, #6), mail plans and broadcasts never fall
  back on a late failure. SMS: no delivery reports; not planned yet. The actual Sinch reason code
  for "number has no Viber" is still to be recorded from a live test.
- **#5, #6, #12:** mobile-only follow-ups with no fallback.
- **#2:** a rejected QR image triggers the email even though the text already arrived (the
  registrant gets the details twice).
- **#13:** the fallback is the organizer's choice per broadcast, not config (kept by design).

All but the last one close with the uniform flow; #13 stays as decided.
