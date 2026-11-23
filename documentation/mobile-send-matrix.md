# Mobile (Viber / SMS) send matrix

Every message the API sends by Viber or SMS, and how it falls back to email.
Checked against the code on 2026-11-23 (branch `DEV_1783`), after
`plans/completed/20261123_uniform-message-delivery.md`.

## One flow for every message

Each message is defined once and sent the same way:

1. **One template, three renderings.** `MessageTemplates` holds the SMS and Viber wording; the
   email HTML stays in `EmailTemplates`.
   - **SMS**: text only, with a pass page link in place of the QR (when `Sms:PublicApiBaseUrl` is
     set) and a numbered meal stub list.
   - **Viber**: text plus images (QR pass, meal stubs). With no public HTTPS base
     (`Viber:PublicApiBaseUrl`), the text-only version is sent (meal stubs as a numbered list).
   - **Email**: subject, HTML, inline images, attachments (`invite.ics`).
2. **One send, one group.** A message's mobile parts go out in order as one group
   (`IMobileSender.SendAsync`). A rejected part stops the group and fails the whole message.
   **Viber hard rule** (Sinch bills each message): one Viber text per message.
   - Text only: one text message.
   - Text + QR (one image): **one** card message (the image with the text).
   - Text + several images: one card with the text and the QR, then one image message per other
     image (meal stubs). A pass with 2 meal stubs = 3 Viber messages.
   SMS still sends the pass, join link and meal stub list as separate texts.
3. **One dispatcher** (`MessageDispatcher.SendAsync`) applies the channel preference:

| Preference | Sends | On failure |
|---|---|---|
| **Mobile** (Viber/SMS) | Active provider only | Email, when `Viber:FallbackToEmail` / `Sms:FallbackToEmail` is on (also for a later Viber failure) |
| **Both** | Viber/SMS **and** email | No fallback: the email already went out with its own retry |
| **Email** | Email only | Background retry (5×) |

- **Country not listed** for the active provider (Phone country table) **or no phone**: mobile is
  skipped and email is sent, whatever `FallbackToEmail` says.
- **Failed email**: queued for the background retry (5×, `EmailRetryDispatcher`). It never falls
  back to mobile. Registrant emails are rebuilt from the registration; other messages retry their
  own email (EmailSender, or the mail plan's own retry).

**What counts as a failure.** A send is "failed" only when the provider **rejects the request**
(error response or exception). A message the provider accepted but could not deliver later (for
example, a number with no Viber account) is first counted as **sent**. For **Viber**, Sinch then
reports the failure by webhook (`SinchWebhooksController`). Every part of one send shares a
`GroupId`; a `FAILED` report on any part queues the email **once for the group**, when the send
was tagged: mobile preferred, email fallback on, has an email. Messages without a registration save
what to email with the group's first part (`ViberMessages.EmailSnapshotJson`): for a mail plan or
reminder only the receiver's delivery id (the email is rebuilt from the plan, so pasted images are
not copied per receiver); for the broadcast the name of its email, saved as an `.eml` file in
`mail-queue/` (`EmailFileStore`), once per address per broadcast; numbers sharing an address that
both fail later queue that email once. Failed emails waiting for the background retry are saved the same
way; the database never holds an email's content. Reports more than 24 h after the
send are ignored. SMS has no delivery reports.

## Matrix

| # | Message | Trigger | Code | Provider | Email on rejected send | Email on later Viber failure | Notes |
|---|---|---|---|---|---|---|---|
| 1 | Entry pass: text + QR + join link (hybrid) + meal stubs | Admin / "Can confirm" user confirms; participant or admin "Resend pass" | `PassService.SendConfirmationAsync` → `MessageTemplates.Pass` | Active (Viber or SMS) | ✅ Shared rule; any refused part (QR, stub image) fails the message | ✅ Once per group | Online-only events: confirmation text with the join link, same flow. Viber may have delivered some parts before the refused one, so the registrant can get those plus the email |
| 2 | Entry pass resend (Attendance) | Admin "Resend pass" on Attendance | `SendConfirmationAsync(emailOnly: true)` | — | n/a (email only) | n/a | No mobile message |
| 3 | Event details changed | Admin edits an event (time, title, venue, link) | `PassService.SendMeetingUpdateAsync` → `MessageTemplates.MeetingUpdate` | Active | ✅ Shared rule | ✅ | The email always exists when the registrant has an address (the calendar invite falls back to the event page link) |
| 4 | Registration / event cancelled | Admin cancels a registration, participant cancels, or event deleted | `PassService.SendMeetingCancelAsync` → `MessageTemplates.MeetingCancel` | Active | n/a: mobile-preferred is sent as **Both** | ❌ (the email always goes) | Exception (plan decision 5): the CANCEL calendar email always removes the entry. Only for registrations that received a pass |
| 5 | Kit claimed | Admin / self kit claim | `PassService.SendKitClaimedAsync` → `MessageTemplates.KitClaimed` | Active | ✅ Shared rule, when the registrant has an email | ✅ | |
| 6 | Edit-your-response link | Public page "lost my link" recovery | `PassService.SendEditLinkAsync` → `MessageTemplates.EditLink` | Active | ✅ Shared rule, when the registrant has an email | ✅ | |
| 7 | Mail plan message | Mail plan send (channel Viber or Both) | `MailPlanDispatcher.DispatchAsync` → `MessageTemplates.MailPlan` | Active | Viber plan: ✅ shared rule, not when already emailed. Both plan: no fallback (everyone is in the BCC batches) | ✅ Viber plan only | Email-only receivers keep the BCC batches. A failed fallback email is retried with the plan. Link addresses kept in the Viber/SMS text ("See the list (https://…)") |
| 8 | Mail plan reminder | Receiver's "remind me" time is reached | `MailPlanDispatcher.RemindAsync` → `MessageTemplates.MailReminder` | Active | ✅ Shared rule (receiver's channel) | ✅ | Email retried by `EmailSender` |
| 9 | Viber broadcast | Organizer types a message in the Viber broadcast dialog | `EventsController.ViberBroadcast` → `MessageTemplates.Broadcast` | **Viber only**, even when SMS is the active channel (by design) | ⚠️ Only when the dialog's **Fallback to email** box is ticked | ✅ Only when the box is ticked | Ignores `Viber:FallbackToEmail`. An unlisted country counts as failed (then email, if ticked) |
| 10 | Email retry | Background retry of a failed email | `EmailRetryDispatcher` | — | n/a (email only) | n/a | Never sends Viber/SMS |

## Gaps

- **SMS** has no delivery reports, so a later SMS failure never sends the email.
- The actual Sinch reason code for "number has no Viber" is still to be recorded from a live test.
- **#9:** the fallback is the organizer's choice per broadcast, not config (kept by design).
