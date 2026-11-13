# Plan: Notification channel fallback and mobile hotfixes

| | |
|---|---|
| **Status** | 🚧 In progress — steps 1–4, 3b, 7 implemented 2026-11-12; steps 8–9 (phone country table, strict mobile → email fallback) implemented 2026-11-13. Tests: 141/141 pass (2026-11-13) with `dotnet exec EventRegistration.Api.Tests.dll` (xUnit v3); see `documentation/notification-test-matrix.md` |
| **Last updated** | 2026-11-13 |
| **Related** | `PassService`, `MobileMessenger`, `ViberService`, `SmsService`, `EmailService`, `MailPlanDispatcher`, `AttendanceController`, `PhPhone`, `shared/ph-phone.ts` |

## Goal

A notification is reported as failed only after **every** channel has been tried. When a channel
fails, the next channel is tried right away. Viber and SMS cost money per message, so the
fallback chain tries each **once**. Two hotfixes close gaps in the mobile (SMS / Viber) flow.
Existing retries (mail plans, invitation emails) keep their schedule; they change only to treat a
turned-off email as "not sent" (step 1) and, for mail plans, to fall back to email when the single
mobile attempt fails (decision 10). Viber/SMS stay un-retried, as today.

## Problem today

- `ViberService` and `SmsService` catch every error and write to `outbox/`; callers never see the
  failure. `PassService` reports "Pass sent via Viber" and stamps `PassEmailSentAt` anyway.
- `EmailService.SendAsync` returns nothing; on SMTP failure it writes to `outbox/` silently.
- `MailPlanDispatcher` counts `viberFailed`, but the senders never throw, so it is always 0 and
  `ViberSentAt` is stamped for failed messages.
- No sender falls back to another channel.
- Passes, kit-claim, edit-link and meeting messages have **no automatic retry**; the only resend is
  the admin "Resend pass" button (`AttendanceController.cs:102`), which is email only.

## Before / after

| Area | Before (today) | After (this plan) |
|---|---|---|
| Viber / SMS send fails | Error hidden; written to `outbox/`; counted as sent | Reported as failed; email tried at once **only when** that provider's `FallbackToEmail` is on (decision 14) |
| Viber fails, SMS configured (or SMS fails, Viber configured) | Not tried | Other mobile provider **never** tried (Infobip fails over itself); email per `Viber:FallbackToEmail` / `Sms:FallbackToEmail` (decision 14) |
| `FallbackToEmail` off, mobile send fails | — (no setting) | No email; result `NotSent`, "Pass could not be sent by any channel." (decision 14) |
| Phone country not listed for the active channel | Sent to any number | Mobile **skipped** (not a failure, no provider call); email sent **whatever** `FallbackToEmail` says (decision 14) |
| Email send fails (pass, kit-claim, edit link, meeting) | Written to `outbox/`; counted as sent | Reported as failed; **no fallback** to Viber/SMS (decision 11); queued for background email retry, 5×, every 5 min (decision 12) |
| Fallback order | None (only email when Viber chosen but no phone) | Mobile preferred: active provider only, then email (decision 14 rules). Email preferred: email only. Both: email and mobile each sent, neither falls back (decisions 4, 11, 14) |
| All tried channels fail | Admin sees "Pass sent via …"; `PassEmailSentAt` stamped | `NotSent`; one outbox entry per tried channel; `PassEmailSentAt` empty; admin message per decision 6 |
| All channels turned off | Outbox; counted as sent; "Pass sent" | Development: same, admin sees "Pass sent (channels are disabled)." Production: `NotSent`, "Pass could not be sent by any channel." |
| A channel turned off, another on (Production) | Turned-off channel writes its message to `outbox/`, counted as sent | Off channel skipped (like an unlisted country), email used, no outbox (decisions 9, 14) |
| Outbox, channel off (Production) | Written | **Not written** (decision 5) |
| Admin message after confirm | "Pass sent via Viber / email / email and Viber" | Real channel used, e.g. "Viber failed; pass sent by email." |
| Mail-plan receiver whose mobile send fails | Marked as reached anyway; never retried | Not marked as reached; never retried on mobile; email at once (single message) when `FallbackToEmail` is on, unless already emailed (decisions 10, 14) |
| Mail-plan receiver whose phone country is not listed | Sent to any number | Mobile skipped; email at once unless already emailed (decision 14) |
| Kit-claimed / edit-link for "Both" users | Email only | Email **and** mobile (#4) |
| Meeting cancelled, mobile-only registrant | No notice | Mobile text sent (#5) |
| Settings page "Mobile notification channel" note | Said QR not in SMS, "Both use Infobip" | ✅ Done: plain wording, full message list |
| Settings page "Phone countries" | — | Admin lists, adds and removes country codes and ticks Viber / SMS per code; remove asks first (decision 13) |
| Channel label when SMS is active | "Viber" | "Mobile (Viber)" / "Mobile (SMS)" (M1) |
| Phone control (user master, My Account, invite sign-up) — now every form | Free text (My Account) / PH mask `+63 9XX XXX XXXX` (user master) | Country-code dropdown (distinct codes in the table) + number; PH keeps `9XX XXX XXXX` (decision 13) |
| Phone stored format | `+63` without leading 9 accepted | `+<code><number>`, 8–15 digits; `+63` must be `+639` + 9 digits (M2, decision 13) |
| Phone outside the Philippines — user master / My Account / invite sign-up | Rejected (user master) / not checked (My Account) | Accepted only when its country code is in the Phone country table; otherwise "Mobile numbers from this country are not supported." (decision 13) |
| Phone outside the Philippines — CSV import | Rejected | Any valid number accepted, listed or not; no `+` = read as PH (decision 13). Template header and import dialog explain the format (with `+` and country code; set the Excel column to Text) |
| Phone entry on sign-up, guest registration, registration edit | Free text (sign-up) / PH mask `+63 9XX XXX XXXX` (guest, edit) | Same country-code dropdown + number as the user forms; server rejects unlisted codes (decision 13) |
| Admin removes a country code later | — | Users keep their number; user list shows a warning icon, user form and My Account show "Mobile messages can't be sent to this number"; mobile skipped, email used (decisions 13, 14) |
| Database | — | New `EmailRetry` table (decision 12); new `MailPlanDelivery.MobileAttemptedAt` (decision 10); new `PhoneCountries` table, seeded `63`/Viber, `63`/Sms (decision 13); optional `Registration.PassDeliveryFailedAt` |

## Scope

| # | Item | Decision |
|---|---|---|
| 1 | Failed sends | **Fall back** to the next channel at once. No retries inside a request (decision 2). |
| 4 | "Both" users miss kit-claimed / edit-link mobile messages | **Hotfix.** |
| 5 | Mobile-only users get no meeting-cancel notice | **Hotfix.** |
| M1 | "Viber" label shown when the provider is SMS | Recommendation below. |
| M2 | Admin user phone accepts `+63` without a leading 9 | Recommendation below. |
| G1 | Accept phone numbers from countries the admin allows (Phone country table) | Decision 13, step 8. |
| G2 | Viber → email and SMS → email only, switchable; no duplicate with provider failover | Decision 14, step 9. |

## Decisions

| # | Decision |
|---|---|
| 1 | Every sender returns a result instead of hiding failures: `DeliveryResult { Sent, Failed, NotConfigured }`. `Failed` covers timeouts, provider errors and provider rejections. |
| 2 | **No in-process retries.** One attempt per channel; on `Failed` the next channel is tried at once. Existing background retries stay as they are: mail plans (email only; Viber/SMS are never retried today and stay that way, decision 10), invitation emails (email). |
| 3 | Channels in the chain: **Email**, **Viber**, **SMS**. A channel is used only when the registrant has the contact (email / phone). A turned-off channel is handled by decision 9, not by fallback. |
| 4 | Fallback order, preferred channel first (changed 2026-11-13 by decision 14): <br>• Mobile preferred: the **active** mobile provider only, then Email when allowed (decision 14). The other mobile provider is **never** tried. <br>• Email preferred: **Email only — no fallback to Viber/SMS** (decision 11). <br>• Both: Email and the active mobile provider are each sent; neither falls back. |
| 5 | **Outbox rules** (Email, Viber and SMS alike; each channel writes its own message): <br>• Development, channel **off**: write to `outbox/`. <br>• Development, channel **on but failed**: write to `outbox/`. <br>• Production, channel **off**: write **nothing**. <br>• Production, channel **on but failed**: write to `outbox/`. <br>A delivered message is never written (as today). The senders keep their own outbox writes; only the "off in Production" case changes. |
| 6 | `PassDelivery` reports the channel(s) that actually delivered and whether a fallback happened, e.g. `{ Channels: [Email], FellBackFrom: Viber }`. `NotSent` = at least one channel was enabled and tried, and every tried channel failed. `PassEmailSentAt` is stamped only when at least one channel delivered (or decision 9 applies). Admin message when nothing delivered: if an email retry was queued (decision 12), "Email could not be sent; retrying in 5 minutes."; otherwise "Pass could not be sent by any channel." |
| 7 | One helper, `NotificationDelivery` (new service), owns the chain. `PassService` and `MailPlanDispatcher` give it one send delegate per channel (the pass message differs per channel: email HTML + QR, Viber text + image, SMS text + link). No new abstraction beyond that. |
| 8 | Each attempt is logged with channel and result (`Viber failed: 503, trying SMS`). No phone number or email in log messages beyond what is logged today. |
| 9 | **Turned-off channel depends on the environment** (`IWebHostEnvironment.IsDevelopment()`), for Email, Viber and SMS: <br>• **Development:** an off channel (`Enabled` false or no credentials) writes **its own message** (email `.eml`, Viber text + QR, SMS text) to `outbox/` so developers can check it, and **counts as sent** — no fallback, `PassEmailSentAt` stamped. When every channel used was off, the admin sees **"Pass sent (channels are disabled)."** <br>• **Production (released):** an off channel is **skipped** — nothing written to `outbox/`, not a failure, the next channel in the chain is used. If no channel is on, the result is `NotSent`. <br>An **enabled** channel always sends for real, in both environments (needed to test real Viber/SMS/Gmail the day before a demo). Fallback (decision 4) reacts only to a real failure of an enabled channel. |
| 10 | **Viber / SMS are tried once by the fallback chain.** Passes, kit-claim, edit-link and meeting messages have no mobile retry; "Resend pass" stays **email only** (`emailOnly: true`). **Mail plans keep today's behaviour: Viber/SMS never retried.** Each receiver's mobile message is tried once (active provider only, decision 14); a new nullable `MailPlanDelivery.MobileAttemptedAt` is stamped on every try, and later plan attempts skip receivers that have it. `ViberSentAt` is stamped only on a real `Sent`. A mobile failure does not re-queue the plan (only email does). A receiver whose mobile send failed falls back to email at once (single message) — **skipped** when the receiver already got this plan's email (`EmailSentAt` set, e.g. "Both") or has no email; that fallback email uses the plan's existing email retry. |
| 11 | **Email failure never falls back to Viber/SMS.** A failed email is not a reason to pay for a mobile message. Mail-plan and invitation emails already retry; pass and other registrant emails get a background retry (decision 12). Applies when email is the preferred channel, and to the email part of "Both". Mobile → email fallback (decision 4) still applies. |
| 12 | **Background email retry for registrant emails** (pass, kit-claim, edit link, meeting update, meeting cancel). A failed email (`Failed` only; a turned-off email is `NotConfigured` and is skipped per decision 9, not queued) is saved as an `EmailRetry` row and retried **5×, every 5 min**, copying the `InvitationEmailDispatcher` pattern. Pass, kit-claim, edit-link and meeting-update rows store only `RegistrationId` + message kind; the email (QR, `invite.ics`) is rebuilt from the registration at each try, so no pass or body is stored. **Meeting-cancel rows** store a small snapshot instead (attendee email, display name, invite UID, title, start, reason; no QR), because the registration is already deleted (registration cancel deletes the row; event delete cascades) — `RegistrationId` is nullable with no FK cascade. **Email only:** Viber/SMS are never re-sent; for a "Both" registrant whose Viber already delivered, the email is still retried. A pass email that succeeds on retry stamps `PassEmailSentAt`. |
| 13 | **Phone country table.** New admin-managed table `PhoneCountry` (`Id`, `CountryCode` digits 1–4 without `+`, unique; `Viber` and `Sms` bit flags — changed 2026-11-13 from one row per code + channel). Seeded with `63` (Viber ✓, SMS ✓). A row means "users may save numbers with this code"; each flag means "this channel may send to them". A new code starts with both flags off. A future channel is a new bit column (it needs its own sender code anyway). Phones are stored `+<code><number>` (8–15 digits). <br>• **User master (admin create/edit) and user profile (My Account):** the phone control is a country-code dropdown (the **distinct** codes in the table) + number. A number whose code is not in the table is **rejected** (server too). `+63` keeps the PH mobile rule (`+639` + 9 digits). <br>• **CSV import:** any valid number is accepted, in the table or not. Numbers without `+` are read as PH (`09…`, `639…`, `9…`). <br>• Codes are matched by prefix (country codes never prefix each other; `+1` is shared by NANP). <br>• **Out of sync** (admin removes a code later): users keep their number; the user list and My Account show "Mobile messages can't be sent to this number" so it can be updated. <br>• **Every phone entry** (user master, My Account, sign-up, guest registration, registration edit) uses the same control — shared `app-phone-field` — and the server rejects unlisted codes on each (changed 2026-11-13). CSV template header says: `Phone (optional; with country code e.g. +639171234567)` (old header still accepted). |
| 14 | **Strict mobile → email fallback** (supersedes the Viber ↔ SMS fallback of decision 4). Config switches next to `Enabled`, default `true`: `Viber:FallbackToEmail`, `Sms:FallbackToEmail`. <br>• The active provider is tried once. On `Failed`, email is sent only when that provider's `FallbackToEmail` is on; otherwise the message ends `NotSent`. <br>• **Skipped, not failed:** before sending, the phone's country code is checked against the table for the active channel. No row = the mobile channel is **skipped** and email is used **regardless of** `FallbackToEmail` (nothing was sent, so no duplicate is possible). A turned-off provider in Production (`NotConfigured`, decision 9) is skipped the same way. <br>• Mail plans: same rule per receiver (skip = email; failure = email only when `FallbackToEmail`). <br>• "Both": email is its own channel, always sent; switches do not apply. Email failure never falls back to mobile (decision 11). |
| 15 | **Shared email sender with retry** (`EmailSender`, for every new email). `SendAsync` tries once now; a real failure (`Failed`) saves the whole message (to, cc, subject, HTML, attachments, inline images) as an `EmailRetry` row, `Kind = Message`, in `SnapshotJson`, and `EmailRetryDispatcher` retries it 5×, every 5 min. A turned-off email (`NotConfigured`) is not queued (decision 12). `SendNowAsync` = one try, no retry. `includeDefaultCc: true` also copies the Settings CC addresses (kept with the queued email); on for the team notice and direct invite, as before. Used now by: team "Complete your registration" notice, direct event invite, calendar update/cancel to invitees, mail-plan reminder. **Not** used by account verification (the user can press "Resend"). Callers count a queued email as accepted (it will arrive), and a turned-off or rejected one as failed. |

## Open questions

- **Delivery log.** `outbox/` holds only undelivered messages and nothing in the app reads it; the
  record of sent messages is the DB stamps (`PassEmailSentAt`, `MailPlanDelivery.*SentAt`,
  `InvitationEmail` status). Optional: a `NotificationLog` table (registrant, channel, message
  type, time, result; no body or QR) shown as "Delivery history" on the Attendance row.
- **Outbox in Production** still holds failed messages with unencrypted passes. Security rule:
  keep it protected (outside the web root, server-admin access only).
- Should a pass that still fails after the 5 email retries (decision 12) be visible to the admin
  (a "Delivery failed" badge on the Attendance row, new nullable
  `Registration.PassDeliveryFailedAt`)? Optional now; without it the only trace is the log and
  the `EmailRetry` row. "Resend" stays email only (decision 10).
- ✅ Closed (2026-11-13): the non-active mobile provider is never used (decision 14).
- ✅ Closed: the decision-9 result is `DeliveryResult.Disabled`.
- SMS pass with no pass link (`Sms:PublicApiBaseUrl` empty) leaves a mobile-only registrant without
  a usable QR. Recommended: such an SMS pass does not count as delivered, so the chain moves on to
  email; log a startup warning when SMS is on and the link base is empty.
- M1 label "Mobile (Viber)" shows the Viber name on screen. Viber is the app registrants use, not a
  backend provider (Infobip etc.), so it is likely fine under the plain-words rule — confirm.
- SMS characters: `—`, `•`, `₱` in SMS texts switch the message to Unicode (70 chars per part, more
  cost). Recommended: plain `-`, `*`, `PHP` in SMS texts only.
- Provider: **Infobip** for Viber and SMS (2026-11-13), numbers from any country (decision 13). The
  chain still works with any provider; a new SMS provider needs a small adapter in `SmsService`.
  UniSMS sends to PH numbers only — a foreign number there is rejected (`Failed`).
- **Provider-side failover set-up (decision 14).** Infobip's Viber → SMS failover is set up on the
  Infobip side (account / sender set-up, or a `failover` block in the send request). Recommended:
  set it up in Infobip; the app never sends SMS after Viber (decision 14) and does not add the `failover`
  block unless asked. Note: Infobip accepts the message at once and fails over later, so the app
  sees `Sent` and cannot tell which channel delivered — admin messages say "Pass sent via Viber".
- With provider failover on, should `Viber:FallbackToEmail` stay true? A Viber message Infobip
  rejects at once (e.g. bad number) is `Failed` and goes to email; one that fails later (SMS
  failover also failed) is not seen by the app, so no email. Recommended: keep true.
- International SMS costs more per message, and Viber/SMS reach depends on the Infobip account's
  country coverage. Confirm the countries expected (registrants abroad) with the client.

## Steps

### 1. Senders report results ✅ Done (2026-11-12)

- [x] Add `DeliveryResult` enum in `Services/` (`Sent`, `Disabled`, `NotConfigured`, `Failed`);
      it replaces `EmailDeliveryStatus`. `Disabled` = off in Development, written to `outbox/`, counts as sent.
- [x] `ViberService` / `SmsService`: every send returns `DeliveryResult`; keep the internal outbox
      writes (decision 5); map provider rejection (Infobip groupId 2/4/5, Movider bad number,
      UniSMS `fail_reason`) and HTTP/timeout errors to `Failed`, not configured to `NotConfigured`.
- [x] Outbox gate (decision 5): in `ViberService`, `SmsService` and `EmailService.TransmitAsync`,
      when the channel is off and `!env.IsDevelopment()`, return `NotConfigured` without writing
      to `outbox/`. `IWebHostEnvironment` is already injected in all three constructors.
- [x] Email off in Production: `MailPlanDispatcher` / `InvitationEmailDispatcher` must treat
      `NotConfigured` as "not sent" (retry, then Failed), not as `Sent`.
- [x] `IMobileSender` methods return `Task<DeliveryResult>`; expose `bool Configured`.
- [x] `MobileMessenger`: `ChainAsync()` returns the active sender, then the other one only when it
      is configured.
- [x] `EmailService.SendAsync` returns `DeliveryResult` (keeps its own outbox write, decision 5).
      Check every caller of `SendAsync` (grep) — callers that ignore the result keep working.

### 2. Fallback helper ✅ Done (2026-11-12)

- [x] New `Services/NotificationDelivery.cs`: `DeliverAsync(channelOrder, sendDelegates)` — one
      attempt per channel, fallback per decision 4. No outbox writes of its own (senders do it).
- [x] Register in `Program.cs`.
- [x] `PassService.SendConfirmationAsync`, `SendMeetingUpdateAsync`, `SendMeetingCancelAsync`,
      `SendKitClaimedAsync`, `SendEditLinkAsync` use the helper. `emailOnly` resend bypasses the
      chain (decision 10).
- [x] `PassDelivery` carries actual channels + fallback info (decision 6).
- [x] `RegistrationsController` (line ~512), `AttendanceController` (lines ~185, ~606) show the real
      result, e.g. "Viber failed; pass sent by email." / "Pass could not be sent by any channel." /
      "Pass sent (channels are disabled)."

### 3. Broadcasts (mail plans) ✅ Done (2026-11-12)

- [x] `MailPlanDispatcher`: mobile send per receiver goes through the helper; a receiver whose
      mobile send fails falls back to email (single message, not the BCC batch).
- [x] `ViberSentAt` / `EmailSentAt` stamped only on a real `Sent`.
- [x] New nullable `MailPlanDelivery.MobileAttemptedAt` (migration), stamped on every mobile try;
      the mobile loop selects `MobileAttemptedAt is null` (not `ViberSentAt is null`), so a failed
      receiver is never re-sent (decision 10).
- [x] Mobile failure does not count toward re-queuing the plan (drop `viberFailed` from the
      `Queued` condition; keep it in `LastError` as information). Fall back to email at once for that
      receiver (single message) unless `EmailSentAt` is set or the receiver has no email.

### 3b. Email retry for registrant emails ✅ Done (2026-11-12)

- [x] New `Models/EmailRetry.cs`: `Id`, `RegistrationId` (nullable, no FK cascade), `Kind` (Pass /
      KitClaimed / EditLink / MeetingUpdate / MeetingCancel), `Status`, `Attempts`,
      `NextAttemptAt`, `LastAttemptAt`, `LastError`, `CreatedAt`, `SentAt`, plus nullable
      meeting-cancel snapshot as one `SnapshotJson` column (calendar entry, name, reason; no QR).
      `DbSet` in `AppDbContext`, migration `NotificationFallbackEmailRetry` (also adds
      `MailPlanDelivery.MobileAttemptedAt`).
- [x] `PassService`: when the email part of any registrant message fails, add an `EmailRetry`
      row (one per registration + kind; skip if one is already pending).
- [x] New `Services/EmailRetryDispatcher.cs` (hosted service, copy of `InvitationEmailDispatcher`):
      picks due rows, rebuilds the email through `PassService` (email only), max 5 attempts,
      5 min apart, then `Failed`. A Pass row that succeeds stamps `PassEmailSentAt`.
- [x] Non-cancel rows: skip if the registration was cancelled or deleted (mark `Failed`, reason
      logged). MeetingCancel rows are sent from the snapshot (`CalendarInvite.Cancel`), never skipped
      for a missing registration.
- [x] Register in `Program.cs`.

### 4. Hotfixes ✅ Done (2026-11-12)

- [x] **#4 Both channel:** `SendKitClaimedAsync` and `SendEditLinkAsync` use
      `NotificationChannels.UsesViber` / `UsesEmail` (as `SendConfirmationAsync` does), so "Both"
      sends both.
- [x] **#5 Meeting cancel:** `SendMeetingCancelAsync` also sends a mobile text ("…meeting has been
      cancelled / removed") when `UsesViber(channel)` and a phone is on file. Move the
      `invite is null` early return so it only skips the email part (invite is null when the
      registrant has no email, but the mobile text must still go). Keep the
      `PassEmailSentAt is null` early return: a pass that was `NotSent` (decision 6) gets no cancel
      notice on any channel — intended, nothing was received.

### 5. Minor recommendations 🚧 In progress

- [x] **Settings wording:** "Mobile notification channel" note on the admin Settings page rewritten
      in plain words (full message list, SMS pass link, Viber picture; no provider or config terms).
- [x] **M1 Provider label:** keep the stored channel value `Viber` (no migration). Show the label
      from the active provider: "Mobile (Viber)" or "Mobile (SMS)" in the registration form,
      account page and user form; server messages name the channel that actually delivered
      (`RegistrationsController`, `AuthController:210`, `UsersController:61`). Plain words only on
      screens — no provider names. `GET /api/settings` is admin-only, so a new public
      `GET /api/settings/mobile-channel` feeds `MobileChannelService` in the frontend. Bulk confirm
      reports `fellBack` / `retrying` / `notSent` counts.
- [ ] 🚧 **M2 Phone rule:** merge the three PH normalizers (`RegistrationsController.NormalizePhMobile`
      `:617`, `ViberService.NormalizePhone`, `SmsService.NormalizePhone`) into one shared
      `PhPhone.Normalize` helper; use it there and in `UsersController` (create/edit) and CSV import so
      every stored phone is `+639XXXXXXXXX`. Change `user-form-panel` pattern to
      `\+63 9\d{2} \d{3} \d{4}` and reuse `shared/ph-phone.ts`.
      Done: `PhPhone.Normalize` used by registrations, user create/edit and CSV import; user form
      pattern and `isCompletePhMobile`. Left: `ViberService` / `SmsService.NormalizePhone` only strip
      formatting for any number (not PH validation), so they were not merged — **superseded by
      step 8** (one helper for all numbers, PH rule kept for `+63`).

### 6. Tests ✅ Superseded by step 12 (2026-11-13) — the open items below are covered by the notification test matrix

- [x] Fake senders in `EventRegistration.Api.Tests` (Sent / Failed / NotConfigured).
- [x] Viber fails once = SMS tried at once (if configured), then Email; no second Viber call;
      result names the channel used.
- [ ] All fail = `NotSent`, one outbox entry per tried channel, `PassEmailSentAt` null.
- [ ] All channels off (local demo) = outbox entry, `PassEmailSentAt` stamped, admin message
      "Pass sent (channels are disabled)." (decision 9).
- [ ] Development, mobile preferred, Viber off, email on = Viber message written to `outbox/`,
      counts as sent, no fallback (decision 9).
- [ ] Production, mobile preferred, Viber off, SMS off, email on = nothing in `outbox/`, email sent.
- [x] Production, all channels off = `NotSent`, nothing in `outbox/`.
- [x] Outbox matrix (decision 5), each for Email, Viber and SMS: Dev+off = written; Dev+failed =
      written; Prod+off = not written; Prod+failed = written; any+delivered = not written.
- [ ] Development, Viber enabled = real send (fake HTTP), not outbox.
- [ ] Both, Viber off, email on (Development) = Viber message in `outbox/` and email sent.
- [ ] Mail plan: mobile fails = one Viber/SMS call per receiver, email fallback sent at once; a
      later plan attempt (email queued) makes no second Viber/SMS call; `ViberSentAt` empty,
      `MobileAttemptedAt` set; mobile-only failures leave the plan `Sent`, not `Queued`
      (decision 10).
- [ ] Email preferred, email fails = no Viber/SMS call; result `NotSent`; one `EmailRetry` row;
      admin message "Email could not be sent; retrying in 5 minutes." (decisions 6, 11, 12).
- [x] Production, email off = no `EmailRetry` row (decision 12).
- [x] Meeting cancel email fails after registration delete / event delete = retried from the
      snapshot and sent.
- [ ] Mail plan, "Both" receiver, mobile fails, email already sent = no fallback email.
- [ ] Email retry: succeeds on a later try = `PassEmailSentAt` stamped; 5 failures = `Failed`, no
      6th try; no Viber/SMS call at any try.
- [ ] Both, Viber sent, email fails = email retried; Viber not sent again.
- [x] Email retry (non-cancel kind) for a cancelled registration = not sent.
- [ ] Both, email fails, mobile sent = mobile only; no extra mobile message for the email failure.
- [ ] Both: kit-claimed and edit-link reach email and mobile (#4).
- [ ] Meeting cancel reaches a mobile-only registrant (#5).
- [ ] Viber pass rejected = `Failed`, one Viber call, falls back to the next channel.
- [x] Step 8: `PhoneNumber.Normalize` — `09171234567` / `+63 917 123 4567` = `+639171234567`;
      `+1 (415) 555-0100` = `+14155550100`; `+63 817 …`, `+0…`, `+123`, 16 digits, `12345678` = null.
- [x] Step 8: admin create with `+1` number = 400 when `1` is not in the table, 200 after adding it;
      CSV import with `+1` number = imported (CSV part not written yet).
- [x] Step 9: Viber fails, `FallbackToEmail` false = no email, `NotSent`; true = email sent.
- [ ] Step 9: Viber active, SMS configured, Viber fails = no SMS call.
- [ ] Step 9: phone code not in table for the active channel = no mobile call, email sent even with
      `FallbackToEmail` false.
- [ ] Step 9: mail plan, receiver code not in table = email sent; Viber failure with switch off =
      no email.

### 8. Phone country table and phone control (decision 13) ✅ Done (2026-11-13)

- [x] Model `PhoneCountry` (`Id`, `CountryCode` `[MaxLength(4)]`, `Channel` `[MaxLength(10)]`), unique
      index (code, channel), `DbSet`, migration `PhoneCountries` with seed rows `63`/Viber, `63`/Sms.
- [x] `PhoneCountriesController`: `GET /api/phone-countries` (anonymous: forms need the codes; no
      personal data), `POST` and `DELETE {id:int}` (admin). Code: digits only, 1–4, no leading 0;
      channel `Viber` or `Sms`; duplicate = 409.
- [x] `PhPhone` → `PhoneNumber`: `Normalize(raw)` accepts `+<digits>` (8–15, first digit 1–9; `+63`
      must pass the PH rule) and PH local input; `Supported(phone, codes)` prefix match.
- [x] `UsersController` create/edit and `AuthController` profile: reject a phone whose code is not
      in the table ("This country code is not supported."). CSV import: format check only.
- [x] Frontend `PhoneCountryService` (loads the table once); `PhoneInputComponent`
      replaced by helpers in `shared/phone.ts` + a code select and number field in the admin user
      form and My Account (no new component). PH keeps the `9XX XXX XXXX` mask.
- [x] Settings page: "Phone countries" card — one row per code: Code, Country, Viber ☐, SMS ☐,
      delete (confirm dialog, safe option first); add picks a code, both channels start unticked.
- [x] Indicator: users list row and My Account show "Mobile messages can't be sent to this number"
      when the phone's code is not in the table.
- [x] Country names: `frontend/web/public/country-codes.json` (`[{ "code": "63", "country":
      "Philippines" }]`, read at runtime, devs add entries there). Settings adds codes from a
      dropdown of these entries and shows the country per row; the phone-field dropdown shows
      "+63 Philippines" (closed field keeps "+63"). (2026-11-13)

### 9. Strict mobile → email fallback (decision 14) ✅ Done (2026-11-13)

- [x] `ViberSettings.FallbackToEmail`, `SmsSettings.FallbackToEmail` (default `true`); documented in
      `deployment.md` (not added to `appsettings.json`; the default applies).
- [x] `IMobileSender`: `FallbackToEmail` (replaces `Configured` on the interface).
- [x] `MobileMessenger.ChainAsync()` → `RouteAsync()`: active sender, its `FallbackToEmail`, and the
      table codes for its channel; `Supports(phone)`. No backup provider.
- [x] `ChannelAttempt` gets `AfterFailure` (default `true`): when false the chain stops after a
      `Failed` previous channel but still continues after a skip/off.
- [x] `PassService.DeliverAsync`: unsupported country = no mobile attempt (email used);
      email attempt marked `AfterFailure: FallbackToEmail`.
- [x] `MailPlanDispatcher`: same per receiver.
- [x] Docs: `deployment.md` (switches), `events.md`, admin-guide, `registration-flow.md`,
      `database-table.md` (`PhoneCountry`), `validation.md` (Phone row).

### 10. Shared email sender with retry (decision 15) ✅ Done (2026-11-13)

- [x] `Services/EmailSender.cs`: `SendAsync(to, subject, html, cc?, attachments?, inlineImages?)`
      → `EmailSendResult(Result, RetryQueued)`; `SendNowAsync(...)` (no retry). Queue saved in its
      own scope so the caller's unsaved changes are not committed with it.
- [x] `EmailRetryKinds.Message`; `EmailRetryDispatcher` sends a `Message` row from its snapshot
      (no registration lookup). No migration (`SnapshotJson` is `nvarchar(max)`).
- [x] Use it: `TeamsController` team notice (fixes "failed counted as sent"),
      `InvitationsController.Send` (same), `CalendarInvitationNotifier`, `MailPlanDispatcher.RemindAsync`.
- [x] Tests: queued on `Failed`, not queued on `NotConfigured`; dispatcher sends a `Message` row
      (written, not run).
- [x] Docs: `events.md`, `database-table.md` (`EmailRetries.Kind = Message`).

### 11. Explicit phone entry everywhere (decision 13) ✅ Done (2026-11-13)

- [x] Shared `shared/phone-field` component (country-code dropdown + number, separate `.html` /
      `.scss`); two-way binds `"+<code><number>"`. Replaces the inline controls in the user form and
      My Account, and the PH-only fields in sign-up, guest registration and registration edit.
      `shared/ph-phone.ts` removed.
- [x] Server: `PhoneNumber.ValidateAsync` (renamed from `ForUserAsync`) on sign-up, guest
      registration and guest registration edit too.
- [x] CSV template header `Phone (optional; with country code e.g. +639171234567)`; old header
      `Phone (optional)` still accepted; import dialog explains the format and the Excel Text tip.

### 12. Channel test folders and notification test matrix ✅ Done (2026-11-13)

- [x] `Email:/Viber:/Sms:TestSendDir` + `TestFail` (tests only, ignored in Production): a channel
      writes each message to `test/sent/{email,viber,sms}` or fails every send. `TestSendFolder` helper.
- [x] `ApiFixture`: all channels write to the test folders by default; `ChannelsAsync(...)` sets
      Send / Fail / Off per channel, the email fallback switches and the provider for one test.
- [x] `NotificationMatrixTests`: P01–P19 (pass chain), M01–M27 (every message type), R01–R04
      (retry). Matrix in `documentation/notification-test-matrix.md`.
- [x] "Access is denied": `dotnet test`'s helper process may not watch its parent on this machine;
      tests run in one process with the xunit console runner (command in the matrix doc).
- [x] Whole suite: 141 / 141 pass.

### 7. Docs ✅ Done (2026-11-12)

- [x] `documentation/events.md` and `.claude/skills/admin-guide`: fallback behaviour, "tried once
      by the chain; never retried, mail plans included" rule for Viber/SMS, new admin messages.
- [x] `.claude/rules/registration-flow.md`: add "a pass falls back to other channels before it is
      reported as not sent; Viber/SMS are never retried; failed registrant emails retry 5× in the
      background".
- [x] `documentation/database-table.md`: `EmailRetry` table, `MailPlanDelivery.MobileAttemptedAt`.

## Risks

- Fallback to SMS adds per-message cost when Viber is down (one extra message at most).
- A mail-plan receiver whose mobile send fails gets no second mobile try; a short provider
  hiccup means email only (or nothing, if the receiver has no email).
- A registrant may receive a message on a channel they did not choose (by design — better than
  no pass).
- No retry on a channel: a short provider hiccup sends the message by the next channel instead
  of the preferred one.
- A channel left off by mistake in production is skipped quietly (decision 9). Mitigation: log a
  startup warning in Production for each channel that is off.
- A deployed demo/staging server must run as `Development` to keep the outbox behaviour; with
  `ASPNETCORE_ENVIRONMENT=Production` and all channels off, passes show as not sent.
- A pass email that fails all 5 retries is not tried again; without the optional "Delivery failed"
  badge the admin may not notice.
- The email is rebuilt at retry time: if the event changed in between (venue, meeting link), the
  registrant gets the current details, not the original ones (intended).
- A fallback switch turned off while the provider has **no** failover set up means a failed Viber
  message reaches nobody by mobile. Mitigation: deployment doc pairs each switch with the matching
  Infobip set-up.
- International numbers raise SMS cost and may fail on providers or senders limited to PH; such
  failures fall back per decision 4/14.
- CSV numbers without `+` are read as PH: a foreign number without `+` is rejected (with a
  clear message) rather than guessed.

## Change log

| Date | Change |
|---|---|
| 2026-11-12 | Plan created from the SMS/Viber flow review. |
| 2026-11-12 | Decision 9: all channels off keeps today's outbox behaviour (counts as sent); turned-off channel is skipped, not a failure. |
| 2026-11-12 | Decision 9: admin message for the all-off case is "Pass sent (channels are disabled)." |
| 2026-11-12 | Settings page "Mobile notification channel" wording corrected in plain words — no provider names or config terms on user-facing pages. |
| 2026-11-12 | Removed the 3 in-process retries: failed channel falls back to the next one at once. `Rejected` result dropped (same as `Failed`). Retry config and latency question removed. |
| 2026-11-12 | Decision 10: Viber/SMS tried once per message to cap cost; mail-plan retries and Resend pass are email only; new `MailPlanDelivery.MobileAttemptedAt`. |
| 2026-11-12 | Stale-info pass: decisions renumbered in order, "Queued" removed from decision 9 (naming moved to open questions), problem list and risks updated, open questions added (SMS pass link, SMS characters, provider choice), Before / after matrix added. |
| 2026-11-12 | Decision 11: email failure never falls back to Viber/SMS. Open question added: passes have no email retry today. |
| 2026-11-12 | Decision 9 widened: a turned-off channel keeps today's behaviour — writes its own message to `outbox/` for developers, counts as sent, no fallback. Decisions 3 and 5, matrix, tests and risks updated. |
| 2026-11-12 | Decision 9 split by environment: Development writes off-channel Viber/SMS to `outbox/` (counts as sent); Production skips off channels (no outbox, next channel used). Enabled channels always send for real. Open question added: same rule for email. |
| 2026-11-12 | Decision 5 rewritten as the outbox matrix (Dev: off/failed written; Prod: off not written, failed written), for Email, Viber and SMS. Senders keep their outbox writes; only Prod+off changes. Email-in-Production question closed; delivery-log and outbox-protection notes added. |
| 2026-11-12 | Decision 10 narrowed: mail-plan retries keep today's mobile retry for failed receivers; `MobileAttemptedAt` dropped; email fallback only after the last attempt. |
| 2026-11-12 | Hotfix #3 dropped: Viber pass stays one message (text + image) to avoid a second charge. |
| 2026-11-12 | Removed "unchanged" items (rows, scope #2/#3, steps, tests) so the plan lists only changes. Scope #1 and decision 11 reworded; email-retry question notes Viber is not re-sent. |
| 2026-11-12 | Decision 12: background email retry (5×, every 5 min) for pass, kit-claim, edit-link and meeting emails; new `EmailRetry` table and `EmailRetryDispatcher`; Viber/SMS never re-sent. "Delivery failed" badge now optional. Step 3b, tests, docs and risks added. |
| 2026-11-12 | Review fixes: meeting-cancel retry rows store a snapshot (registration is deleted before the send); outbox = one entry per tried channel (no-outbox email variant dropped); email off in Production not queued; admin message rule for queued retry; mail-plan email fallback skips receivers already emailed or without email; M2 merges all three phone normalizers; hotfix #5 keeps the `PassEmailSentAt` guard; decisions 11–12 moved to the end; Goal reworded; M1 label question added. |
| 2026-11-12 | Code check: today a failed Viber/SMS is never retried anywhere — senders swallow errors, so mail plans stamp `ViberSentAt` and never re-queue for mobile. Decision 10 now keeps that: mobile tried once, never retried; `MailPlanDelivery.MobileAttemptedAt` re-added to block re-sends; mobile failure no longer re-queues the plan; email fallback at once instead of after the last attempt. Database row in Before / after updated. |
| 2026-11-12 | Implemented steps 1–4, 3b, 7 and M1; M2 mostly. `DeliveryResult.Disabled` names the decision-9 result; `EmailDeliveryStatus` removed; meeting-cancel snapshot stored as `SnapshotJson`; public `GET /api/settings/mobile-channel` added for the M1 label. Tests added in `NotificationFallbackTests.cs` (chain, admin messages, outbox rules, phone rule, retry dispatcher, API confirm with channels off) — not run yet: the test host fails with "Access is denied" on this machine. Fixed three lines merged by earlier edits. |
| 2026-11-13 | Provider set to Infobip (Viber with its own SMS failover). Decision 13 + step 8: phone numbers from any country, stored E.164; PH local input still accepted; one `PhoneNumber` helper (M2 normalizer question superseded). Decision 14 + step 9: per-provider switches `Viber:FallbackToSms` / `FallbackToEmail`, `Sms:FallbackToViber` / `FallbackToEmail` (default true) so provider-side failover does not cause duplicate messages. Before / after, scope, tests, open questions and risks updated. |
| 2026-11-13 | Redesign of the earlier 2026-11-13 entry: fallback is now strictly Viber → email and SMS → email (`Viber:FallbackToEmail`, `Sms:FallbackToEmail`); Viber ↔ SMS dropped (decision 4 changed). Decision 13 replaced: admin-managed **Phone country** table (code + channel) drives the phone control in user master and My Account (outside codes rejected; CSV accepts any). Send-time check: code not in the table for the active channel = mobile skipped, email used regardless of the switch. Indicator for unsupported numbers. Steps 8–9 and tests rewritten. |
| 2026-11-13 | Implemented steps 8–9: `PhoneCountries` table + migration (seed `63`/Viber, `63`/Sms), `PhoneCountriesController`, `PhoneNumber` helper (replaces `PhPhone`), user master / My Account / invite completion reject unlisted codes, CSV accepts any; `MobileMessenger.RouteAsync` (active provider only, country check); `ChannelAttempt.AfterFailure`; `Viber:/Sms:FallbackToEmail`. Frontend: code select + number on user form and My Account, unsupported-number badge on user list and warning on My Account, Settings → Phone countries card. Tests added, not run. |
| 2026-11-13 | Before / after matrix updated for decisions 13–14: strict mobile → email with `FallbackToEmail`, unlisted country = skipped (email always), Phone countries settings, phone control, CSV / guest rules, out-of-sync codes, `PhoneCountries` table. |
| 2026-11-13 | Decision 15 + step 10: shared `EmailSender` (send with background retry, or send now without) on the `EmailRetry` table; applied to the team notice, direct invite, calendar update/cancel and mail-plan reminder. Account verification stays without retry (has Resend). |
| 2026-11-13 | Implemented step 10: `EmailSender` (`SendAsync` with retry, `SendNowAsync` without), `EmailRetryKinds.Message`, dispatcher branch; team notice and direct invite now count only delivered-or-queued as sent (`failed` added to the invite response); calendar notifier and mail-plan reminder retried. Tests written, not run. |
| 2026-11-13 | Step 11: every phone entry uses the shared country-code dropdown + number (`app-phone-field`) and the server checks the Phone country table on sign-up and guest registration too; CSV template header and import dialog describe the phone format. |
| 2026-11-13 | Fix: `EmailSender` gained `includeDefaultCc` (saved in the queued email); team notice and direct invite send the Settings CC copy again, as they did before step 10. |
| 2026-11-13 | Step 12: test send folders per channel, `ChannelsAsync` switch, 50 notification matrix tests (pass chain, every message type, retry); whole suite 140/140 passing via the in-process xunit runner. Step 6 items are covered by the matrix. |
| 2026-11-13 | Country-code JSON (`public/country-codes.json`, code + country): Settings → Phone countries picks codes from it and shows the country; `app-phone-field` options show the country name. |
| 2026-11-13 | `PhoneCountries` reshaped to one row per code with `Viber` / `Sms` flags (migration `PhoneCountryChannelColumns` merges old rows per code). API: `POST {countryCode}` adds with both off, `PUT /{id} {viber, sms}`. Settings: channel toggle removed; table shows Code, Country, Viber, SMS checkboxes, delete. Tests 140/140 (in-process xunit runner), web 67/67. |
| 2026-11-13 | Tests moved to xUnit v3 on Microsoft Testing Platform (`global.json` test runner, VS launch profile) so they run with `dotnet exec` where VSTest gets "Access is denied". Fixture blanks provider credentials; guard test checks own database, `Testing` environment and test-only send folders. 141/141. |
| 2026-11-13 | Hot fix: organizer Viber broadcast counted every number as sent even when Infobip failed (e.g. 401). Now counts only delivered numbers and returns `failed`; event page shows success / warning / error. Decision: the broadcast stays **Viber only** (no email fallback, no phone-country check). Tests M28–M30; 144/144. |
