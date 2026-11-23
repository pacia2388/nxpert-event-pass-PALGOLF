# Plan: One template set and one send → fallback flow for every message

| | |
|---|---|
| **Status** | ✅ Done (2026-11-23); the live Viber check is left to live testing after publish |
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

> **As built (2026-11-23).** Two changes from the sketch below, both to keep the wording locked
> (snapshots unchanged) and the email cheap:
> - **Viber hard rule (decided 2026-11-23, cost):** one Viber text per message. Text only = one
>   text; text + QR = one card (image with the text); text + several images = one card with the
>   text and the QR, then one image message per other image (meal stubs). The Viber pass is one
>   part: details, join link and stub list in one text.
> - The mobile renderings are **lists of parts** (`MobilePart`: subject, text, images, text without
>   images), sent in order as one group. The pass is three parts (pass, join link, meal stubs),
>   so each part keeps today's text; SMS sends them as separate texts (no longer SMS).
> - The email is a **send function** built only when it is sent (the pass bumps the calendar
>   sequence and renders stub images); `EmailSnapshotJson` keeps a copy when a late Viber failure
>   or a cancel retry needs it. `MessageTemplates` holds the SMS/Viber texts; HTML stays in
>   `EmailTemplates`.

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
| 1 | Viber group: text accepted, an image rejected. Fall back to email (registrant gets the text and the full email) or count as sent? | ✅ Decided 2026-11-23: fall back. Duplicate details are better than a missing QR or meal stub |
| 2 | Mail plans (#11): the email part today goes out as BCC batches to email-preferred receivers. Keep the batches and send only mobile-preferred receivers through the dispatcher? | ✅ Decided 2026-11-23: yes. Batches keep SMTP load low; the per-receiver dispatcher is only for mobile-preferred receivers. A **Both** plan (channel set per plan): every receiver is in the BCC batches and the mobile send never falls back, as today (no second email) |
| 3 | Viber broadcast (#13): today Viber only, even when SMS is active, with a per-broadcast "Fallback to email" tick box | ✅ Decided 2026-11-23: stays **Viber only** by design (not renamed, never SMS). It uses the shared template and dispatcher with the channel forced to Viber; the dialog's tick box, not `Viber:FallbackToEmail`, decides the email fallback |
| 4 | Late Viber failure for mail plans and broadcasts | ✅ Decided 2026-11-23: include it. Tag with the plan delivery / broadcast, queue a `Message` retry with the email snapshot. Viber-only plans only: a Both plan has already emailed everyone |
| 5 | Cancellation (#8): sent as "Both" so the calendar entry is always removed. Keep? | ✅ Decided 2026-11-23: **keep the exception.** A mobile-preferred registrant's cancellation goes as **Both** (text + CANCEL email, no fallback) so the calendar entry is always removed; email-preferred stays email only. A documented exception, not a separate path |
| 6 | Meeting update (#7) without a calendar invite | ✅ Decided 2026-11-23: always build an email. Already true in code: `CalendarInvite.For` returns null only when there is no email address (the URL falls back to the event page), so no change is needed |
| 7 | `GroupId` column: new migration, or fold it into `20261122155946_ViberMessages`? | ✅ Decided 2026-11-23: **fold it in** (not deployed to any server). The migration is already pushed (`e518c3c` on `origin/DEV_1783`), so every database that applied it, the local dev DB included, must be dropped and re-created after the fold (`dotnet ef database drop --force`; ask before running) |
| 8 | Reminder (#12): today Viber/SMS by preference, result ignored | ✅ Decided 2026-11-23: normal message, preference → fallback |

## Steps

### Safety net (before any refactor) ✅ Done (2026-11-23)
- [x] `MessageTemplateSnapshotTests`: every message on every channel (pass in-person / hybrid / online, meeting update online / in-person, meeting cancel × 3 reasons, kit claimed by other / self, edit link, broadcast Viber / email fallback, mail plan email / Viber / SMS, mail plan reminder), sent through the real code and compared with `Snapshots/Messages/<case>.txt`
- [x] Generate the snapshots (28 files in `Snapshots/Messages/`, reviewed; 17/17 tests pass twice in a row, 2026-11-23)
- [x] The snapshots must stay unchanged through every step below. A wording change that is meant (for example the pass now carrying the join link and meal stubs) is accepted only with `UPDATE_SNAPSHOTS=1` and a reviewed diff. Only change: the Viber pass as one message (`pass-inperson` / `pass-hybrid` email-viber, edited and reviewed); all snapshots pass

### Model and templates ✅ Done (2026-11-23)
- [x] `OutgoingMessage`, `MobilePart`, `MobileImage` records (as built, see Design)
- [x] `MessageTemplates`: Pass (with join link + meal stubs), MeetingUpdate, MeetingCancel, KitClaimed, EditLink, MailPlan, MailReminder, Broadcast
- [x] Move the SMS/Viber texts out of `SmsService` / `SinchViberService` into the templates (wording unchanged except where merged)

### Providers and dispatcher ✅ Done (2026-11-23)
- [x] `IMobileSender.SendAsync(phone, message, tag)`; remove the three old methods from `SinchViberService`, `SmsService`, `MobileMessenger`
- [x] Viber group send: shared `GroupId`, all parts tagged, any rejected part = failed (decision 1)
- [x] `MessageDispatcher` (rules from `PassService.DeliverAsync`); `PassService` methods build a message and call it
- [x] `EmailRetryDispatcher`: rebuilds through the `PassService` methods, which now use `MessageTemplates` (no change needed)
- [x] Delete `ViberService.cs` (commented out)

### Late failures ✅ Done (2026-11-23)
- [x] `ViberMessages.GroupId` (+ `EmailSnapshotJson`), folded into migration `20261122155946_ViberMessages` by hand (decision 7); `dotnet ef migrations has-pending-model-changes` reports none
- [x] Reset every database that applied the old migration: no server has it (the folded migration ships in one publish); local dev databases are dropped before running the app (`dotnet ef database drop --force`)
- [x] Webhook: claim by group; queue a `Message` retry with a snapshot for messages without a registration (decision 4)

### Mail plans, reminders, broadcast ✅ Done (2026-11-23)
- [x] `MailPlanDispatcher`: mobile receivers through the dispatcher; keep the BCC batches (decision 2)
- [x] `RemindAsync` through the dispatcher (decision 8)
- [x] Broadcast through the dispatcher, channel forced to Viber, fallback from the dialog tick box (decision 3); dialog and wording unchanged

### Tests ✅ Done (2026-11-23)
- [x] Pass renders SMS parts without images and Viber parts with image paths, join link and meal stubs in one message (`MessageDispatcherTests`); email HTML is covered by the snapshots
- [x] Dispatcher matrix: preference × provider rejected/off × country listed × `FallbackToEmail`, tagging, broadcast tick box (`MessageDispatcherTests`, 16 pass, no database)
- [x] Webhook: two parts of one group `FAILED` → one `EmailRetry`; mail-plan message → one `Message` retry (`SinchWebhookTests`, written, not run here)
- [x] A rejected stub image → email fallback (`ViberGroupSendTests`, no database: a stand-in for Sinch through the overridable `SinchViberService.PostAsync`; also checks text + QR = one card, then one message per stub)
- [x] Mail plan / broadcast: rejected mobile send → email (existing M21, M31, M33)
- [x] Existing `NotificationMatrixTests`, `NotificationChainTests`, `SinchWebhookTests`, `MessageScheduleTests` and the snapshots still pass: full suite 335/335 passed on the developer machine, 2026-11-23 (after the Viber group tests)

### Live check (after publish) — handed to live testing
- [ ] On a published server with real Viber: a pass with several meal stubs arrives as one card (text + QR) plus one picture per stub; the card's text is not cut off; a card without a title looks right

### Docs ✅ Done (2026-11-23)
- [x] `documentation/mobile-send-matrix.md`: rewrite as one flow plus a per-message template table
- [x] `documentation/notification-test-matrix.md`: new rows
- [x] `.claude/rules/registration-flow.md`: follow-ups part of the pass; cancellation exception (the Viber-only broadcast rule stays as written)
- [x] `.claude/skills/admin-guide/reference.md` and `participant-guide/reference.md`: what registrants now get

## Risks

- **Bigger Viber group:** a pass with many meal stubs becomes one group of many Sinch messages;
  any rejection sends the full email (decision 1).
- **SMS count:** unchanged. The pass, join link and meal stubs stay separate SMS texts (one group);
  a refused one stops the rest and sends the email.
- **Viber wording:** with images, the meal stub intro now lists the stubs before the pictures (the
  test text already did). With no public HTTPS base, meal stubs go as a numbered text list instead
  of failing quietly.
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
| 2026-11-23 | Decisions 1, 2, 4, 6, 8 accepted as recommended; decision 6 needs no code (an invite exists whenever there is an email) |
| 2026-11-23 | Implemented: `OutgoingMessage`/`MobilePart`, `MessageTemplates`, `IMobileSender.SendAsync` (Viber group send), `MessageDispatcher`, `PassService`, mail plans, reminders, broadcast, webhook group claim, `GroupId` + `EmailSnapshotJson` folded into the migration, `ViberService.cs` deleted; docs updated. Database tests and snapshots still to run |
| 2026-11-23 | Viber hard rule: one text per message; text + QR as one card; other images (meal stubs) one message each. Viber pass merged into one part (was pass + join link + stubs = up to 2 + 1 + 1 + N sends; now 1 + N). `pass-inperson` / `pass-hybrid` email-viber snapshots and M02 updated by hand (not run). Card text length on Viber still to check in a live test |
| 2026-11-23 | Database space: mail plans and reminders no longer copy the email (with pasted images, base64) into `ViberMessages.EmailSnapshotJson` per receiver; they keep `{ DeliveryId }` and the retry rebuilds the email from the plan (`EmailRetryKinds.MailPlan` / `MailReminder`, `MailPlanDispatcher.SendLateEmailAsync`; skips a receiver already emailed). Only the broadcast keeps a copy (text only, ≤ 1000 characters) |
| 2026-11-23 | Readability pass: no chained or nested ternaries; `MessageTemplates.Pass` takes a `PassDetails` record and is split into SMS / Viber builders; email send functions, tags and fallbacks built in named steps. No behaviour change |
| 2026-11-23 | Emails kept for later are saved as `.eml` files (`EmailFileStore`, folder `mail-queue/` or `App:MailQueueDir`); the database stores only the file name: `EmailRetry.EmailFile` (new migration `EmailRetryEmailFile`) and `ViberMessages.EmailFile` (folded into the `ViberMessages` migration). Retry sends the saved file and deletes it when finished; files older than 26 h that no pending retry uses are deleted hourly. Older `Message` rows with JSON still work |
| 2026-11-23 | Review fixes: broadcast saves one `.eml` per address (not per number), none for an address already emailed, and deletes at the end the ones Viber never took; webhook queues a saved email once (`IX_EmailRetries_EmailFile`, added to the `EmailRetryEmailFile` migration); broadcast summary no longer says "sent to 0 Viber numbers" and the "faillback" typo is fixed (M29/M31/M33 wording); test runs also empty `test/mail-queue` |
| 2026-11-23 | Full test suite on the developer machine: 333/333 passed. Open: a test for a rejected stub image (needs a Sinch stand-in); a live Viber check of the card (text length, no title) |
| 2026-11-23 | `ViberGroupSendTests` added (card + one message per stub; refused stub → email). `SinchViberService.PostAsync` is the one overridable Sinch call (Sinch's interfaces have internal members, so they cannot be faked). Only the live Viber check after publish is left |
| 2026-11-23 | Plan closed: full suite 335/335; the live Viber card check moves to live testing after publish |
