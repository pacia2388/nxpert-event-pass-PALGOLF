# Plan: One template set and one send → fallback flow for every message

| | |
|---|---|
| **Status** | 📝 Planned: open decisions to settle before work starts |
| **Last updated** | 2026-11-23 |
| **Related** | `PassService` (`DeliverAsync`), `IMobileSender` / `MobileMessenger`, `SinchViberService`, `SmsService`, `NotificationDelivery`, `EmailTemplates`, `MailPlanDispatcher`, `EventsController.ViberBroadcast`, `SinchWebhooksController`, `EmailRetryDispatcher`, `documentation/mobile-send-matrix.md`, `.claude/rules/registration-flow.md` |

## Goal

Every message to a registrant or mail-plan receiver is defined once, with three renderings:

- **SMS**: plain text, with links in place of images (pass page, join link, meal stub list).
- **Viber**: text plus images (QR pass, meal stubs, pasted images), sent as one group.
- **Email**: subject, HTML, inline images and attachments (`invite.ics`).

Every message then goes through the same flow: **send on the preferred channel → fall back**,
with the same rules for rejected sends and for late Viber failures. No message has its own
special path.

## Problem today

`documentation/mobile-send-matrix.md` lists 14 send paths, and they behave differently:

| Gap | Rows |
|---|---|
| Follow-ups are sent separately and their results are ignored (no fallback) | #5 join link, #6 meal stubs |
| The QR image is a second send inside the pass; if the image is rejected the whole pass counts as failed, and the registrant gets the text plus the email | #2 |
| No email template when there's no calendar invite, so no fallback | #7 meeting update |
| Sent as "Both" instead of the preferred channel | #8 cancellation |
| Own fallback rules, no late-failure fallback | #11 mail plan, #12 reminder, #13 broadcast |
| Text built separately in each provider (`SinchViberService.SendPassAsync`, `SmsService.SendPassAsync`, `SendMealStubsAsync`), so SMS and Viber wording drifts | all |

## Design

### 1. One message, three renderings

```csharp
// The three renderings of one message. Null part = that channel has nothing to send.
public sealed record OutgoingMessage(
    string Kind,                // EmailRetryKinds.* (retry rebuild + webhook tag)
    SmsPart? Sms,               // Text
    ViberPart? Viber,           // Text + ImagePaths (relative, e.g. "api/passes/{token}/qr.png")
    EmailPart? Email);          // Subject, Html, InlineImages, Attachments
```

- Templates live in one place, `Services/MessageTemplates.cs`, one method per message:
  `Pass`, `MeetingUpdate`, `MeetingCancel`, `KitClaimed`, `EditLink`, `MailPlan`, `MailReminder`,
  `Broadcast`. The HTML stays in `EmailTemplates`; `MessageTemplates` calls it.
- **The pass absorbs its follow-ups.** One `Pass` message carries the join link (hybrid) and the
  meal stubs in all three renderings. Rows #2, #5 and #6 disappear as separate sends.
- Images are relative paths. Each provider makes them absolute with its own `PublicApiBaseUrl`.
  With no public base, Viber sends the text only (as today), and SMS leaves out the pass link.

### 2. One provider method

`IMobileSender` drops `SendPassAsync`, `SendTextAsync` and `SendMealStubsAsync` for one method:

```csharp
Task<DeliveryResult> SendAsync(string phone, OutgoingMessage message, MobileTag? tag);
```

- SMS sends `message.Sms.Text`.
- Viber sends `message.Viber.Text`, then each image, all tagged with the same `MobileTag`.
- **Rule for a Viber group:** if Sinch rejects any part, the message counts as failed and the
  fallback runs (open decision 1).

### 3. One dispatcher

`MessageDispatcher.SendAsync(Recipient, OutgoingMessage, channel)` (with an optional forced provider and fallback switch, for the Viber-only broadcast) replaces
`PassService.DeliverAsync`, the mail-plan mobile loop and the broadcast loop. The rules stay the
same as `DeliverAsync` today:

The three channel choices (confirmed 2026-11-23):

| Preference | Sends | On failure |
|---|---|---|
| **Mobile** (Viber/SMS) | Active provider only | Email, when `Viber:FallbackToEmail` / `Sms:FallbackToEmail` is on (also for a late Viber failure) |
| **Both** | Viber/SMS **and** email | No fallback: the email already went out with its own retry, so a Viber/SMS failure needs nothing more |
| **Email** | Email only | Background retry (5×) |

- **Country not listed or no phone:** mobile is skipped, email is always sent.
- **Failed email (any preference):** queued for the background retry (5×). It never falls back to mobile.

`Recipient` = phone, email, channel preference, optional `RegistrationId` (for the retry and the
Viber tag).

### 4. Late Viber failures for the whole group

- All parts of one send share a `GroupId` (new column on `ViberMessages`).
- When the webhook gets `FAILED` on any part, it claims the **group**:
  `UPDATE ViberMessages SET EmailQueuedAt = now WHERE GroupId = @g AND EmailQueuedAt IS NULL`.
  Zero rows updated = already queued. This stops one email per image.
- The 24-hour cutoff and the tagging rule (mobile preferred, fallback on, has an email) are
  unchanged.
- Messages without a registration (mail plan, broadcast) need the email part saved for the retry:
  `EmailRetry` kind `Message` with `SnapshotJson`, as `EmailSender` already does (open decision 4).

### 5. Email retry

- No change for registration messages: `EmailRetryDispatcher` rebuilds the email by `Kind` from
  the registration's current data. It calls `MessageTemplates` instead of the `PassService` send
  methods.
- Mail plans keep their own retry (per receiver, `EmailSentAt`).

## What stays out

These are email-only by nature: they go to an address, not to a registrant with a channel
preference.

- Account verification email (`AuthController`)
- Event invitations (`InvitationsController`, `InvitationEmailDispatcher`)
- Team "complete your registration" (`TeamsController`)
- Calendar update/cancel for invitees (`CalendarInvitationNotifier`)

`ViberService.cs` is fully commented out and can be deleted in the same change.

## Open decisions

| # | Question | Recommendation |
|---|---|---|
| 1 | Viber group: text accepted, an image rejected. Fall back to email (registrant gets the text and the full email) or count as sent? | Fall back. Duplicate details are better than a missing QR or meal stub |
| 2 | Mail plans (#11): the email part today goes out as BCC batches to email-preferred receivers. Keep the batches and send only mobile-preferred receivers through the dispatcher? | Yes. Batches keep SMTP load low; the per-receiver dispatcher is only for mobile-preferred receivers. A **Both** plan (channel set per plan): every receiver is in the BCC batches and the mobile send never falls back, as today (no second email) |
| 3 | Viber broadcast (#13): today Viber only, even when SMS is active, with a per-broadcast "Fallback to email" tick box | ✅ Decided 2026-11-23: stays **Viber only** by design (not renamed, never SMS). It uses the shared template and dispatcher with the channel forced to Viber; the dialog's tick box, not `Viber:FallbackToEmail`, decides the email fallback |
| 4 | Late Viber failure for mail plans and broadcasts | Include it: tag with the plan delivery / broadcast, queue a `Message` retry with the email snapshot. Viber-only plans only: a Both plan has already emailed everyone |
| 5 | Cancellation (#8): sent as "Both" so the calendar entry is always removed. Keep? | ✅ Decided 2026-11-23: **keep the exception.** A mobile-preferred registrant's cancellation goes as **Both** (text + CANCEL email, no fallback) so the calendar entry is always removed; email-preferred stays email only. A documented exception, not a separate path |
| 6 | Meeting update (#7) without a calendar invite | Always build an email ("details changed" without `.ics`), so the fallback works |
| 7 | `GroupId` column: new migration, or fold it into `20261122155946_ViberMessages`? | ✅ Decided 2026-11-23: **fold it in** (not deployed to any server). The migration is already pushed (`e518c3c` on `origin/DEV_1783`), so every database that applied it, the local dev DB included, must be dropped and re-created after the fold (`dotnet ef database drop --force`; ask before running) |
| 8 | Reminder (#12): today Viber/SMS by preference, result ignored | Normal message: preference → fallback |

## Steps

### Safety net (before any refactor) 🚧 In progress
- [x] `MessageTemplateSnapshotTests`: every message on every channel (pass in-person / hybrid / online, meeting update online / in-person, meeting cancel × 3 reasons, kit claimed by other / self, edit link, broadcast Viber / email fallback, mail plan email / Viber / SMS, mail plan reminder), sent through the real code and compared with `Snapshots/Messages/<case>.txt`
- [x] Generate the snapshots (28 files in `Snapshots/Messages/`, reviewed; 17/17 tests pass twice in a row, 2026-11-23)
- [ ] The snapshots must stay unchanged through every step below. A wording change that is meant (for example the pass now carrying the join link and meal stubs) is accepted only with `UPDATE_SNAPSHOTS=1` and a reviewed diff

### Model and templates
- [ ] `OutgoingMessage`, `SmsPart`, `ViberPart`, `EmailPart` records
- [ ] `MessageTemplates`: Pass (with join link + meal stubs), MeetingUpdate, MeetingCancel, KitClaimed, EditLink, MailPlan, MailReminder, Broadcast
- [ ] Move the SMS/Viber texts out of `SmsService` / `SinchViberService` into the templates (wording unchanged except where merged)

### Providers and dispatcher
- [ ] `IMobileSender.SendAsync(phone, message, tag)`; remove the three old methods from `SinchViberService`, `SmsService`, `MobileMessenger`
- [ ] Viber group send: shared `GroupId`, all parts tagged, any rejected part = failed (decision 1)
- [ ] `MessageDispatcher` (rules from `PassService.DeliverAsync`); `PassService` methods build a message and call it
- [ ] `EmailRetryDispatcher`: rebuild through `MessageTemplates`
- [ ] Delete `ViberService.cs` (commented out)

### Late failures
- [ ] `ViberMessages.GroupId`, folded into migration `20261122155946_ViberMessages` (decision 7): remove and re-add the migration, then reset every database that applied it
- [ ] Webhook: claim by group; queue a `Message` retry with a snapshot for messages without a registration (decision 4)

### Mail plans, reminders, broadcast
- [ ] `MailPlanDispatcher`: mobile receivers through the dispatcher; keep the BCC batches (decision 2)
- [ ] `RemindAsync` through the dispatcher (decision 8)
- [ ] Broadcast through the dispatcher, channel forced to Viber, fallback from the dialog tick box (decision 3); dialog and wording unchanged

### Tests
- [ ] Each message renders all three parts (SMS has no images, Viber has image paths, email has HTML)
- [ ] Pass: join link and meal stubs travel in the one message; a rejected stub image → email fallback
- [ ] Dispatcher matrix: preference × provider rejected/off × country listed × `FallbackToEmail`
- [ ] Webhook: two parts of one group `FAILED` → one `EmailRetry`
- [ ] Mail plan / broadcast: rejected mobile send → email; late failure → one `Message` retry
- [ ] Existing `NotificationMatrixTests`, `NotificationChainTests`, `SinchWebhookTests`, `MessageScheduleTests` still pass

### Docs
- [ ] `documentation/mobile-send-matrix.md`: rewrite as one flow plus a per-message template table
- [ ] `documentation/notification-test-matrix.md`: new rows
- [ ] `.claude/rules/registration-flow.md`: follow-ups part of the pass; cancellation exception (the Viber-only broadcast rule stays as written)
- [ ] `.claude/skills/admin-guide/reference.md` and `participant-guide/reference.md`: what registrants now get

## Risks

- **Bigger Viber group:** a pass with many meal stubs becomes one group of many Sinch messages;
  any rejection sends the full email (decision 1).
- **SMS length:** a pass with a join link and meal stubs is longer, so more SMS segments and cost.
  Check with the SMS provider's limits.
- **Wide change:** every registrant message path changes. Run the full test suite and the
  notification test matrix before the demo, not on demo day.

## Change log

| Date | Change |
|---|---|
| 2026-11-23 | Plan created from the mobile send matrix review (rows #2, #5, #6) |
| 2026-11-23 | Added the snapshot safety net (`MessageTemplateSnapshotTests`); 28 snapshots generated and reviewed. Noted: mail-plan Viber/SMS text drops link URLs ("See the list.") — current behaviour, locked as is; fixing it is a wording change for a later step |
| 2026-11-23 | Decision 3: Viber broadcast stays Viber only and keeps its name; fallback stays the dialog tick box |
| 2026-11-23 | Decision 7: fold `GroupId` into the existing `ViberMessages` migration; databases that applied it must be reset |
| 2026-11-23 | Mail-plan Viber/SMS text now keeps link addresses (`InvitationMessageBuilder.PlainText`, test `PlainTextLinkTests`); `mail-plan-viber` / `mail-plan-sms` snapshots updated, so the refactor carries the fix |
| 2026-11-23 | Decision 5: cancellation keeps its exception (mobile-preferred sent as Both, so the CANCEL email always removes the calendar entry) |
| 2026-11-23 | Shared rule written as the three channel choices (Mobile → fallback to email; Both → mobile + email with retry; Email → email with retry) |
| 2026-11-23 | Both: no fallback and no late-failure email, since the email channel already went out with its retry (shared rule, decisions 2 and 4) |
