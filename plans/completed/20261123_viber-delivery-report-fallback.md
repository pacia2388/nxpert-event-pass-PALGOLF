# Plan: Viber delivery reports → email fallback

| | |
|---|---|
| **Status** | ✅ Done (2026-11-23) |
| **Last updated** | 2026-11-23 |
| **Related** | `SinchViberService`, `IMobileSender`, `MobileMessenger`, `PassService.DeliverAsync`, `EmailRetry` / `EmailRetryDispatcher`, `.claude/rules/registration-flow.md` |

## Goal

When a Viber message that Sinch accepted later fails (for example, the number has no Viber
account), the registrant still gets the message by email, following the same rules as a failed
send today (`Viber:FallbackToEmail`, mobile-preferred registrants only).

## Problem today

- `SinchViberService.SendAsync` returns `Sent` as soon as Sinch accepts the request
  (`202` + `message_id`). Sinch checks the recipient later, so a number without Viber is counted as
  sent and no email goes out.
- Sinch reports what happens next by webhook. This app does not receive it (see the class comment,
  `SinchViberService.cs:41-42`).

## Sinch support (checked 2026-11-20)

Source: <https://developers.sinch.com/docs/conversation/callbacks/>, send-message and get-message
API reference. The Viber BM channel page returned 404, so nothing below is Viber-specific.

| Need | Supported? | Detail |
|---|---|---|
| Delivery callback | ✅ Yes | Webhook trigger `MESSAGE_DELIVERY`; payload `message_delivery_report` with `message_id`, `status`, `channel_identity`, `reason {code, description, sub_code}`, `metadata` |
| Status values | ✅ | `QUEUED_ON_CHANNEL`, `DELIVERED`, `READ`, `FAILED`, `SWITCHING_CHANNEL`. `READ` and `FAILED` are terminal |
| Number has no Viber | ⚠️ Not documented | Closest code is `RECIPIENT_NOT_REACHABLE` (or `RECIPIENT_NOT_OPTED_IN`). **Confirm with a real test send** |
| Our data on the report | ✅ | Send field `message_metadata` (≤ 1024 chars) comes back as `metadata`; `correlation_id` (≤ 128) is a top-level callback field |
| Register webhook | ✅ | Dashboard (Conversation app → Webhooks) or `POST /v1/projects/{project}/webhooks` (`target`, `target_type: HTTP`, `triggers`). Per-message `callback_url` override also exists |
| Signed callbacks | ✅ | Headers `x-sinch-webhook-signature`, `-nonce`, `-timestamp`, `-algorithm` (`HmacSHA256`); HMAC-SHA256 base64 over `rawBody + "." + nonce + "." + timestamp`, using the webhook's secret |
| Retries | ✅ | 5xx / 429 / timeouts retried with backoff; other 4xx not retried; duplicates possible, order not guaranteed. **Test hosts (ngrok, webhook.site) are not retried** |
| Poll instead of webhook | ❌ | `GET /messages/{id}` has no status field |
| .NET SDK (installed 1.5.1) | ✅ | `Sinch.Conversation.Hooks.MessageDeliveryReceiptEvent`, `CallbackEventConverter`, `SendMessageRequest.MessageMetadata` / `CorrelationId` / `CallbackUrl`, `Conversation.Webhooks.ValidateAuthenticationHeader(headers, JsonNode, secret)` |

## Design

1. **Tag the primary message.** Only the message that decides delivery (pass text, confirmation
   text, kit-claimed, edit link, meeting update) carries a tag: `MessageMetadata =
   "reg:{RegistrationId};kind:{EmailRetryKind}"`. Follow-ups (QR image, join link, meal stubs) and
   untagged sends (mail plans, broadcasts) still get status recorded, but never trigger email.
2. **Record what was accepted.** New table `ViberMessage` (`MessageId` PK, `RegistrationId?`,
   `Kind?`, `Phone`, `Status`, `ReasonCode?`, `AcceptedAt`, `UpdatedAt`, `EmailQueuedAt?`). It is
   written when Sinch accepts the message, gives the webhook idempotency, and keeps an audit trail.
   Only send attempts that would fall back to email are tagged, i.e. mobile-preferred registrants
   with `FallbackToEmail` on and an email address; `Both` and email-preferred are never tagged.
3. **Webhook endpoint.** `POST /api/webhooks/sinch/conversation`, anonymous, body ≤ 256 KB:
   - verify the HMAC on the **raw body** against `Viber:WebhookSecret`, plus a timestamp skew
     check (≤ 5 min); otherwise return `401`;
   - parse only `message_delivery_report`, then update `ViberMessage.Status` / `ReasonCode`;
   - on `FAILED` for a tagged row with `EmailQueuedAt` null: set `EmailQueuedAt` (an atomic
     `ExecuteUpdate … where EmailQueuedAt == null`, so duplicate callbacks queue once), then add an
     `EmailRetry` (reg + kind, `NextAttemptAt = now`). The existing `EmailRetryDispatcher` rebuilds
     and sends the email, with its 5 tries;
   - return `200` for unknown ids, other statuses and duplicates; `5xx` only on our own DB errors
     (so Sinch retries).
4. **Setup.** One webhook on the Conversation app: target `{PublicApiBaseUrl}/api/webhooks/sinch/conversation`,
   trigger `MESSAGE_DELIVERY`, with a secret. The secret goes in local / production config only
   (`Viber:WebhookSecret`), never committed.
5. **No re-send on Viber.** Viber is still tried once; a failure report only ever leads to email
   (rule unchanged).

6. **Fail closed.** When `Viber:WebhookSecret` is empty (or still the `required` placeholder),
   the endpoint rejects every callback with `401` and logs a warning.

## Logging (for live-server testing)

The feature can only be tested on a server whose API is publicly reachable over HTTPS, because
Sinch must call it. The logs must show the whole path without a debugger.

**Where logs go on IIS:** there is no file logger today, only console output. To read the logs,
turn on `stdoutLogEnabled="true"` in the API site's `web.config` and read `logs\` (see
`documentation/deployment.md`). The default level is `Information`, so every line below must be
`Information` or higher (the current "Sinch accepted" line is `Debug` and is never shown in
production). Add `"EventRegistration.Api.Controllers.SinchWebhooksController": "Debug"` under
`Logging:LogLevel` only while testing.

Every line carries `MessageId`, so one `findstr <message_id> logs\*.log` shows a message's whole
path. Never log the secret, signature, full message text or email address. Phone numbers are
masked to the last 4 digits (`+63•••••1234`).

| # | Where | Level | Message (structured fields) | Proves |
|---|---|---|---|---|
| L1 | `SinchViberService.SendAsync` after accept | Information | `Sinch accepted Viber message {MessageId} for {PhoneMasked} (registration {RegistrationId}, {Kind}, tagged {Tagged})` | The send reached Sinch, and which messages expect a fallback |
| L2 | Webhook entry | Information | `Sinch webhook received: {Trigger} {MessageId} {Status} {ReasonCode} (event time {EventTime}, body {Bytes} bytes)` | Sinch reaches the server (firewall, HTTPS, URL correct) |
| L3 | Signature rejected | Warning | `Sinch webhook rejected: {Why}` with `Why` = missing headers / bad signature / timestamp skew {Seconds}s / secret not configured | Secret mismatch between the dashboard and config, or server clock drift |
| L4 | Unknown message | Information | `Sinch webhook: message {MessageId} not ours; ignored` | Another environment shares the Conversation app |
| L5 | Status updated | Information | `Viber message {MessageId}: {OldStatus} → {NewStatus}` | Reports are parsed and saved |
| L6 | Failure, email queued | Warning | `Viber message {MessageId} failed ({ReasonCode}: {ReasonDescription}); email fallback queued (retry {EmailRetryId}, registration {RegistrationId}, {Kind})` | The fallback ran; records the real reason code for "no Viber" |
| L7 | Failure, no email | Information | `Viber message {MessageId} failed ({ReasonCode}); no email because {Why}` with `Why` = untagged / already queued / older than cutoff / no email address | Why a failure did not send an email |
| L8 | Duplicate report | Debug | `Sinch webhook: duplicate {Status} for {MessageId}; ignored` | Idempotency (debug only: Sinch may send many) |
| L9 | Unparseable body / other trigger | Warning | `Sinch webhook: could not read {Trigger} body ({Error})` | Payload shape differs from the docs |
| L10 | DB error | Error | `Sinch webhook: could not save report for {MessageId}` (with exception); returns 500 so Sinch retries | Our own failure |
| L11 | Startup | Information | `Viber delivery reports: {Enabled}` (on = secret set) | Config loaded on the server |

The existing `EmailRetryDispatcher` logs already cover the email side after L6.

**Database check (no logs needed):** the `ViberMessage` table holds every accepted message with
its last status, reason and `EmailQueuedAt`. After a test send, run `SELECT TOP 20 * FROM
ViberMessages ORDER BY AcceptedAt DESC` to see the result.

**Live test script (verification step):**
1. Turn on the stdout log; set the controller to `Debug`.
2. Confirm L11 shows `on` after the app pool restarts.
3. Confirm a registration whose phone is on Viber: expect L1, then L2/L5 `DELIVERED` (and `READ`).
4. Confirm a registration whose phone has no Viber (mobile-preferred, `FallbackToEmail` on, has
   an email): expect L1, then L2, then L6 with the real `ReasonCode`, then one email, then the
   `EmailRetry` row `Sent`.
5. Change the dashboard secret to a wrong value and resend: expect L3 `bad signature`, then
   restore it.
6. Write the observed reason code into this plan and `documentation/mobile-send-matrix.md`.
7. Turn the stdout log and `Debug` level back off.

## Open decisions

| # | Question | Recommendation |
|---|---|---|
| 1 | Pass context into `SinchViberService`: optional `MobileTag? tag` parameter on `IMobileSender` methods (SMS ignores it) vs. an ambient `AsyncLocal` | Optional parameter: explicit, small; touches `MobileMessenger` + `PassService` callers |
| 2 | Which `FAILED` reason codes trigger email? | All of them (a terminal failure means not delivered) |
| 3 | Ignore a `FAILED` report that arrives long after the send (e.g. > 24 h)? | Yes, 24 h: a late email for a past event is noise |
| 4 | Admin feedback: the Attendance row says "Pass sent via Viber" even when it later fails | Phase 1: log only. Later: show "Viber failed → email sent" on the row (separate change) |
| 5 | Mail plans / Viber broadcast: fall back on late failure too? | Not in this plan; record status only |
| 6 | Sinch SDK `ValidateAuthenticationHeader` takes a parsed `JsonNode`; the signature is over the raw body | Verify by hand on raw bytes (≈ 10 lines, `CryptographicOperations.FixedTimeEquals`); use the SDK only to parse |

## Steps

### Backend ✅ Done (2026-11-23)
- [x] `ViberSettings.WebhookSecret` (+ `appsettings.json` placeholder `required`)
- [x] `ViberMessage` model + `AppDbContext` set + EF migration
- [x] `MobileTag` record; optional param on `IMobileSender` / `MobileMessenger`; `SmsService` ignores it
- [x] `PassService.DeliverAsync`: pass the tag only when the mobile chain is followed by an email that `FallbackToEmail` allows
- [x] `SinchViberService.SendAsync`: set `MessageMetadata`; save a `ViberMessage` row for every accepted message
- [x] `SinchWebhooksController`: raw-body HMAC + timestamp check, parse, update status, queue `EmailRetry` once
- [x] Fail closed when `Viber:WebhookSecret` is not set (401 + L3)
- [x] Logging L1–L11 (see Logging); promote the "Sinch accepted" line from `Debug` to `Information`; phone mask helper
- [ ] ~~Rate-limit the endpoint (existing limiter policy)~~ Deferred: the API has no rate limiter yet (part of the `/api/auth/*` throttling work in the security rules); every call must pass the HMAC check first
- [x] Update the `SinchViberService` class comment (lines 41-42)

### Tests ✅ Done (2026-11-23)
- [x] Invalid or missing signature → 401, nothing changed
- [x] `FAILED` on a tagged pass → one `EmailRetry` (Pass); a duplicate `FAILED` → still one
- [x] `DELIVERED` / `READ` → status updated, no email
- [x] Untagged message (Both channel, `FallbackToEmail` off, follow-up image) `FAILED` → no email
- [x] Unknown `message_id` → 200, no row
- [x] `FAILED` older than the cutoff (decision 3) → no email

### Docs ✅ Done (2026-11-23)
- [x] `.claude/rules/registration-flow.md`: Viber failure can also be reported later; email fallback then follows the same rule
- [x] `.claude/skills/admin-guide/reference.md`: late Viber failure → email
- [x] `documentation/notification-test-matrix.md`: new rows
- [x] `deploy-iis` skill / `deployment.md`: register the Sinch webhook, `Viber:WebhookSecret`, public HTTPS needed

### Verification ✅ Done (2026-11-23)
- [x] Live test script (Logging section) on the server with the public API (not on demo day): record the actual `reason.code`, confirm that one email arrives (marked complete by the owner on 2026-11-23; the observed reason code is not recorded here)

## Change log

| Date | Change |
|---|---|
| 2026-11-20 | Plan created; Sinch support checked against public docs |
| 2026-11-20 | Added fail-closed secret rule, logging plan (L1–L11) and live-server test script |
| 2026-11-23 | Implemented in worktree branch `viber-delivery-report-fallback`: `ViberMessage` table + migration `ViberMessages`, `MobileTag` (decision 1: optional parameter), `SinchWebhooksController` (HMAC by hand on raw bytes, decision 6; body parsed with `System.Text.Json` rather than the SDK converter), 24 h cutoff (decision 3), all FAILED codes trigger email (decision 2). L7 "no email address" dropped: only registrants with an email are tagged, and the retry dispatcher drops a message it cannot send. Rate limiting deferred (no limiter exists). Tests `SinchWebhookTests` (9), full suite 266/266 |
| 2026-11-23 | Webhook lines also written to `logs/viber-hook/viber-hook-yyyyMMdd.log` (`ViberHookFileLoggerProvider`, test `ViberHookFileLoggerTests`). Plan marked complete by the owner; moved to `plans/completed/` |
