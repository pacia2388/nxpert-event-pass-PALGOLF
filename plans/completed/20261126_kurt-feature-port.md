# Port features from NPAX-KURT — implementation plan

Status: ✅ Completed (2026-11-26). Implemented 2026-11-25; review fixes applied (see kurt-feature-port-fixes.md). All five phases green (backend 379 tests, web 330, builds clean). The manual `verify` skill run-through is the only open item. Every feature below was reviewed one by one with the
product owner. Implement **only ✅ rows**. Work the phases in order; each phase ends green
(build + tests) before the next starts.

## 0. Read this first (for the implementing agent)

### Background
- PALGOLF (this repo, `C:\wt\nxpert-PALGOLF`) forked from the KURT line at commit `3efc007`
  (migration #39, `20261030053545_MailPlannerCc`). KURT (`C:\wt\nxpert-NPAX-KURT`, being retired)
  then added features in commits `4e0562d` and `6a2a8d6`. None of KURT's 15 later migrations exist
  here.
- KURT is a **reference only**. Read its files with
  `git -C C:\wt\nxpert-NPAX-KURT show HEAD:<path>`. **Port by hand, never by patch or file copy.**
  PALGOLF has since rewritten `EmailService` (returns `DeliveryResult`), added `EmailSender`,
  `EmailFileStore`, `EmailRetryDispatcher` and `MessageDispatcher`, and redesigned the invitation
  composer and mail planner. KURT diffs also contain whitespace/CRLF noise and features declined
  below.
- **Never copy KURT migrations.** Create fresh ones in PALGOLF:
  `dotnet ef migrations add <Name>` in `backend/EventRegistration.Api`.

### House rules (all in `.claude/rules/*.md` and memory; follow them)
- Use cmd or bash, not PowerShell scripts.
- Ask the user before `dotnet ef database drop`, `dotnet publish` or `iisreset`.
- Server is authoritative: add DTO annotations, business checks and EF `[MaxLength]`, and return
  `ProblemDetails` (`.claude/rules/validation.md`).
- Every registrant/receiver message goes through `MessageDispatcher` with a `MessageTemplates`
  template (`.claude/rules/registration-flow.md`). Viber send rule: never split one message's
  text.
- Destructive actions need a confirmation dialog with the safe option pre-selected.
- New Angular components get separate `.html` and `.scss` files. Never move an existing inline
  template.
- User-facing text uses plain words: no provider names (Gmail, Sinch, SMTP) and no config terms.
- Keep **theme tokens**. Never hard-code colours (`documentation/theming.md`).
- Docs live in `documentation/`. Behaviour changes also update
  `.claude/skills/admin-guide/reference.md`, `.claude/skills/participant-guide/reference.md`,
  `.claude/rules/validation.md` and `.claude/rules/registration-flow.md` in the same change.
- Tick each step `- [x]` as it lands, mark finished phase headings `✅ Done (date)`, and add a
  change-log row.
- Do **not** commit or push unless the user says so. Never add `Co-Authored-By` or Claude
  attribution.

### Build and test commands
- API build + tests: `dotnet test backend/EventRegistration.Api.Tests` (xUnit, `ApiFixture` =
  `WebApplicationFactory`; helpers `AdminAsync()`, `SignInAsync()`, `CreateUserAsync()`,
  `CreateTeamAsync()`, `ChannelsAsync()`, `StoredEmail()`).
- Web: `cd frontend/web && npm test` (specs next to files, e.g. `core/*.spec.ts`) and
  `npx ng build`.
- Runtime check: `verify` skill (API `http://localhost:5088`, web `http://localhost:4200`).

## 1. Feature matrix

| ID | Feature | Decision | Phase |
|----|---------|----------|-------|
| F1 | Secrets out of `appsettings.json`, `*.Local.json` not published, remove committed `publish/` | ❌ Declined | — |
| F2 | Deactivate users, no shared default passwords, set-password link, forgot password, invite link no longer signs in | ✅ Accepted (changed) | 1 |
| F3 | Separate display photo and face-recognition photo | ✅ Accepted | 2 |
| F4 | App update banner + no-cache for app shell and runtime JSON | ✅ Accepted | 1 |
| F5 | Meal scanner camera fix | ✅ Accepted | 1 |
| F6 | TV screen-display dialog flexible height | ✅ Accepted | 1 |
| F7 | Calendar memoization | ❌ Declined | — |
| F8 | `GET api/registrations/mine/event-ids` | ❌ Declined | — |
| F9 | Mail attachments, stored in the `.eml` only | ✅ Accepted (changed) | 3 |
| F10 | Batched delivery (100/message), retry skips delivered, **live SignalR progress** | ✅ Accepted (changed) | 3 |
| F11 | Mail recycle bin | ❌ Declined — app sends mail, it is not a mailbox | — |
| F12 | Invitation drafts + To exclusions | ❌ Declined | — |
| F13 | `*@domain` entry, exclusions on app groups, Mail Planner "send immediately" | ✅ Accepted (changed) | 3 |
| F14 | Automatic post-event feedback | ✅ Accepted (changed) | 4 |
| F15 | Rich-text image fixes + **configurable** image limits | ✅ Accepted (changed) | 1 |
| F16 | Department / Job Role, **switchable per installation** | ✅ Accepted (changed) | 5 |
| — | In-app reply / forward / reactions, reply delivery details (`MailConversations*`, Reply-To, Message-ID threading) | ❌ Declined — app accepts no email responses | — |
| — | Optional email for admin-created users | ❌ Declined | — |
| — | TV banners always whole (drop Fill/Fit/Stretch) | ❌ Declined (re-confirmed with illustration) | — |
| — | User delete keeps registrations as guests | ❌ Declined — replaced by F2 Deactivate | — |
| — | KURT's bigger rich-text limits hard-coded | ❌ Declined — F15 makes limits configurable | — |
| — | Invite-only removal, PassService Viber→email fallback, build-time API URLs / `api-base.util.ts` / env files, N-PAX branding (`index.html`, manifest, icons, `styles.scss` colours), attendee-pass restyle dropping `autoPrint`, hard-coded `@n-pax.com` group, `serve-lighthouse.mjs`, `login-deployment.md`, `facerog-engine` (already identical) | ❌ Declined / not needed | — |

Do not implement anything from a ❌ row, even if it appears in a KURT file you read.

---

## Phase 1 — Access, update banner, small fixes ✅ Done (2026-11-25)

One migration: `UserDeactivationAndPasswordSetup`.

### 1A. F2 Deactivate instead of Delete

KURT reference: `Services/JwtService.cs` (`ValidatePasswordAccessAsync`) and `Program.cs`
(`OnTokenValidated`). PALGOLF uses a **middleware** instead of the token event, at the user's
request.

- [x] `User` (`Models/Entities.cs`): add `DateTime? DeactivatedAt`.
- [x] `UsersController.Delete` (`Controllers/UsersController.cs:419`): replace it with
  `POST api/users/{id:int}/deactivate` and `POST api/users/{id:int}/reactivate` (Admin roles).
  - Refuse to deactivate yourself.
  - Keep registrations, teams and team memberships.
  - A Captain can be deactivated; their team stays.
  - Remove the `HttpDelete` route.
- [x] New `Middleware/ActiveUserMiddleware.cs`, registered in `Program.cs` right after
  `app.UseAuthentication()` (line ~146), before `UseAuthorization`:
  - Runs only when `context.User.Identity.IsAuthenticated`.
  - Reads the user id from `ClaimTypes.NameIdentifier` and makes **one** query:
    `Users.Where(Id).Select(Id, DeactivatedAt, PasswordHash != "")`.
  - Returns **401** (empty `ProblemDetails`) when the user is missing, deactivated or has no
    password.
  - Otherwise puts the projection in `HttpContext.Items["ActiveUser"]` so controllers can reuse
    it.
  - SignalR `/hubs/notifications` negotiate/connect passes through the same check.
  - Anonymous requests: no query.
- [x] `AuthController.Login`: deactivated, or no password → the same generic failure message as a
  wrong password.
- [x] `JwtService.CreateToken`: throw if `PasswordHash == ""`. Callers must never issue a token
  for a passwordless user.
- [x] Users list UI (`pages/admin/users/*`):
  - Delete becomes Deactivate (confirmation dialog, Cancel pre-selected) and Reactivate.
  - Show a "Deactivated" badge and a filter.
  - Add `UserDto.DeactivatedAt` / `isActive` in `models.ts`.
- [x] Every audience that sends to users skips deactivated users:
  - `MailPlanAudience.GroupsAsync/ResolveAsync`
  - invitation builder recipient lookups
  - captain member lookups (`TeamsController` candidate search)
  - Do not change existing registrations of deactivated users; passes stay valid.

### 1B. F2 No shared default passwords + set-password link

- [x] Remove `DefaultImportPassword = "12345"` (`UsersController.cs:176`). Admin create (when no
  password is typed) and CSV import save `PasswordHash = ""`.
- [x] Remove `DefaultMemberPassword = "char"` (`TeamsController.cs:22`):
  - New captain-added users get `PasswordHash = ""`.
  - The "unregistered" check at `TeamsController.cs:410` becomes `user.PasswordHash == ""`.
  - The team registration link flow (`AuthController.cs:122-170`) stays as it is; it already sets
    the password.
- [x] `User`: add `Guid? PasswordSetupToken` and `DateTime? PasswordSetupTokenExpiresAt` (unique
  filtered index on the token). Validity comes from config `App:PasswordSetupLinkHours` (default
  72).
- [x] New service `PasswordSetupService`:
  - `IssueAsync(user)` mints a new token, which replaces any earlier one.
  - It sends the message through `MessageDispatcher` with a new
    `MessageTemplates.PasswordSetup(name, url)`: SMS/Viber text plus an email built in
    `EmailTemplates`, themed and HTML-encoded.
  - Kind: a new `EmailRetryKinds` value.
  - URL: `{App:FrontendBaseUrl}/set-password/{token}`, built server-side.
- [x] Send the link automatically when:
  - an admin creates a user without a password;
  - CSV import creates a user. Throttle it the way invitations are, and report "links sent"
    counts in the import result.
- [x] Endpoints (`AuthController`):
  - `GET api/auth/set-password/{token:guid}` → `{ valid, name, email }`. An expired or unknown
    token returns `valid=false`.
  - `POST api/auth/set-password/{token:guid}` with `{ password }`:
    - Runs the server password policy (`PasswordRules`, NFC).
    - Sets the hash and marks `EmailVerified = true`.
    - Clears the token (single-use), then returns `AuthResponse` (sign-in).
    - Refuses deactivated users.
  - `POST api/auth/forgot-password` with `{ email }`:
    - **Always 200** with the same message, whether or not the account exists.
    - Issues a link only for an active user with that email.
  - `POST api/users/{id:int}/password-link` (Admin): resend.
- [x] Throttling: add the built-in ASP.NET Core rate limiter (`AddRateLimiter`, fixed window per
  IP) on `forgot-password` and `set-password` (POST). Keep the policy reusable for other
  `/api/auth/*` routes, but do not change other routes in this phase.
- [x] Web:
  - New page `pages/set-password/` (separate .html/.scss), route `set-password/:token`, public.
  - "Forgot password?" link and dialog on the login page.
  - "Send password link" action in the users list / user form.
  - Use `app-phone-field` patterns where relevant.

### 1C. F2 Event-invite link no longer signs in

- [x] `AuthController.ResolveInvite` (`:262`): never return `AuthFor(user)`. Return
  `{ registered: bool, email, slug }` only.
- [x] `public-event.ts:204-221`:
  - Registered invitee → show "You already have an account — sign in" with the email filled in.
    After sign-in, return to `/e/{slug}`.
  - A registered invitee with no password → the message offers "Send me a password link" (calls
    `forgot-password`).
  - **Guest invitee: unchanged.** The guest form is pre-filled with the invited email.
  - Composer emails (shared `/e/{slug}` link, `InvitationMessageBuilder.cs:73`): unchanged.

### 1D. F4 App update banner + caching

KURT reference: `frontend/web/src/app/core/app-update.service.ts`, `app.html`,
`public/web.config`.

- [x] `core/app-update.service.ts`:
  - Uses `SwUpdate.versionUpdates`, filtered to `VERSION_READY`.
  - Exposes a `ready` signal.
  - `reload()` calls `activateUpdate()` and then `location.reload()`.
  - Checks for an update on start and every 30 min while the page is visible.
  - Does nothing when `SwUpdate.isEnabled` is false.
- [x] `app.html`/`app.ts`: a banner with the text "A new version is ready." and a "Reload"
  button, styled with theme tokens.
- [x] `public/web.config`: add `<location>` blocks setting `Cache-Control: no-cache, no-store,
  must-revalidate` for `index.html`, `ngsw.json`, `ngsw-worker.js`, `appSettings.json`,
  `menu.json`, `theme.palgolf.json` and `theme.nxpass.json`. Keep the existing rewrite rules and
  headers.
- [x] `ngsw-config.json`: make sure the runtime JSON files are not cached by the service worker
  (exclude them, or put them under `dataGroups` with a `freshness` strategy).

### 1E. F5 Meal scanner camera fix

KURT reference: `pages/merchant/meal-scanner.ts` and `.html`.

- [x] The camera starts only from a "Start camera" button.
- [x] `html5-qrcode` is loaded with a dynamic `import()` when the camera is started.
- [x] A start-generation counter means a stale start stops its own stream.
- [x] The camera is stopped on destroy and on route leave.
- [x] Use `core/camera-failure.ts` messages (they already exist).
- [x] Check `pages/scanner/scanner.ts` for the same double-start/leak race and fix it the same
  way. Keep the scanner's current auto-start if the admin guide documents it.

### 1F. F6 Screen-display dialog height

KURT reference: `pages/scanner/screen-display-dialog.ts`.

- [x] Port **only the layout**: a flexible stage height (`min()`/`dvh` bounded by the header) so
  short or rotated windows don't scroll.
- [x] Keep PALGOLF's colour tokens (`--pg-*`). Do not take KURT's `--icon-tile-*`.

### 1G. F15 Rich-text image fixes + configurable limits

KURT reference: `shared/rich-text/rich-text.ts` (the `available` budget and rescale loop, around
line 138).

- [x] API config section `Mail:InlineImages` in `appsettings.json` (and an options class):
  `MaxSourceBytes` 10485760, `MaxImageBytes` 2097152, `MaxTotalBytes` 5242880, `MaxCount` 5.
  Defaults equal today's values.
- [x] Server:
  - `InvitationMessageBuilder.MaxImageBytes` (`Services/InvitationMessageBuilder.cs:18`) and
    every total/count check read the options.
  - Replace the fixed `[RequestSizeLimit(10_000_000)]` (`InvitationsController.cs:40,118`) and
    `[RequestSizeLimit(40_000_000)]` (`MailPlansController.cs:139,153`) with a limit worked out
    from the options: `ceil(MaxTotalBytes * 4/3) + 1 MB` for text. Set it per request through
    `IHttpMaxRequestBodySizeFeature` in a small endpoint filter or attribute that reads
    `IOptions`.
- [x] Expose the limits to admins through `GET api/settings/mail-limits` (Admin).
- [x] Web `rich-text.ts`:
  - Turn the constants (`MAX_SOURCE_BYTES`, `MAX_IMAGES`, `MAX_IMAGE_BYTES`,
    `MAX_TOTAL_IMAGE_BYTES`) into values loaded once from that endpoint, falling back to today's
    defaults.
  - The help text is built from the loaded numbers.
  - An image that is too large after the 1600 px resize is **re-encoded smaller** (JPEG quality
    steps, then scaled down) until it fits `min(MaxImageBytes, remaining total)`, instead of
    being refused.
  - Fix the base64 size maths: decoded bytes = `len * 3/4 - padding`.
  - `MAX_IMAGE_EDGE` stays 1600.
- [x] Docs: `documentation/configuration-and-publishing.md` gets a `Mail:InlineImages` section
  noting that the whole message must stay under 25 MB, and that base64 adds about 33%.

### Phase 1 tests

- [x] API (`EventRegistration.Api.Tests`):
  - A deactivated user's existing token gets 401 on `/api/auth/me`.
  - A deactivated user's login fails with the generic message.
  - Reactivate restores access.
  - You can't deactivate yourself.
  - CSV-imported and captain-added users have an empty password, and login with `12345` / `char`
    fails.
  - The set-password link works once, an expired link fails, the password policy is enforced, and
    it signs the user in.
  - forgot-password returns the same response for known and unknown emails.
  - The rate limiter returns 429 after the limit.
  - The invite resolve never returns a token.
  - Mail limits come from config: a lowered `MaxImageBytes` is refused by the server.
- [x] Web specs: `app-update.service` (no-op when SW is disabled); `rich-text` size maths and
  rescale-to-budget; public-event invite handling (registered → sign-in prompt, guest →
  prefill).
- [x] Update existing tests that call `DELETE api/users/{id}` or rely on `12345` / `char`.

### Phase 1 docs

- [x] admin-guide `reference.md`: deactivate/reactivate, password links after create/import, resend
  link.
- [x] participant-guide `reference.md`: set-password link, forgot password, invite link asks you to
  sign in.
- [x] `.claude/rules/security.md`: one middleware checks each signed-in request; invite links never
  sign in.
- [x] `.claude/rules/registration-flow.md`: roles line, deactivated users.
- [x] `.claude/rules/validation.md`: password set via link.
- [x] `documentation/roles.md`.

---

## Phase 2 — F3 Display photo vs face-recognition photo ✅ Done (2026-11-25)

One migration: `SeparateRecognitionAndDisplayPhotos`. KURT reference: commit `6a2a8d6`, files
`Models/Entities.cs`, `Models/Dtos.cs`, `Controllers/{Auth,Users,Registrations,FaceEngine,Events}
Controller.cs`, web `account.ts`, `user-form-panel.ts`, `public-event.ts`, `offline.service.ts`,
`face-tv.ts` (`toItem`), and `documentation/display-photo-and-face-enrollment.md`.

- [x] `User.FacePhoto` and `Registration.FacePhoto` (nullable string, same storage format as
  `PhotoUrl` / `GuestPhoto`).
- [x] DTOs get `FacePhoto` with `[StringLength(2_000_000)]`: the profile update, admin upsert,
  guest registration and offline sync requests.
- [x] Saving:
  - A face capture writes **only** `FacePhoto`.
  - An uploaded display photo writes only `PhotoUrl` / `GuestPhoto`.
  - Fix `public-event.ts:437`, which today does `(faceConsent ? capturedFacePhoto : null) ??
    displayPicture()`. The account and admin face dialogs must stop writing into `PhotoUrl`.
- [x] `FaceEngineController` gallery: `FacePhoto ?? PhotoUrl` (users) and `FacePhoto ??
  GuestPhoto` (guests). Withdrawing face consent clears `FacePhoto`.
- [x] The offline bundle (`EventsController`, around line 385) includes the guest display photo.
- [x] TV / check-in result / pass show the **display** photo, or initials when there is none.
  Never show the face capture (`face-tv.ts` `toItem` uses the API `photoUrl`).
- [x] Tests: rewrite KURT `tests/DisplayPhotos/Program.cs` assertions as xUnit tests:
  - a face capture doesn't change the display photo;
  - the gallery prefers `FacePhoto`;
  - the guest offline bundle carries the display photo.
  - Skip KURT's `.mjs` checks.
- [x] Docs: participant and admin guides (the uploaded photo is used on the welcome screen; the
  face capture is used only for recognition). Add
  `documentation/display-photo-and-face-enrollment.md`, rewritten for PALGOLF.

---

## Phase 3 — Mail sending: F9, F10, F13 ✅ Done (2026-11-25)

One migration: `MailAttachmentsBatchesExclusions`. KURT reference: `Services/EmailService.cs`,
`InvitationEmailDispatcher.cs`, `MailPlanDispatcher.cs`, `MailAttachments.cs`,
`Controllers/{Invitations,MailPlans}Controller.cs`, web `shared/invitation-composer/*`,
`pages/mail-planner/*` and `shared/mail-attachments/mail-attachments.ts`. Ignore everything
about conversations, replies, `ReplyToAddress`, `SmtpMessageId`, recycle and drafts (❌).

### 3A. F9 Attachments in the `.eml` only

- [x] **No attachment tables, no upload endpoint, no 7-day cleanup.** Files come as multipart
  parts of the compose / update request:
  - `POST/PUT api/events/{id}/invitations/compose|emails/{id}`
  - Mail Planner create/update, which turn to `multipart/form-data` with a JSON `payload` part
    plus `files[]`.
- [x] On save, build the full `MimeMessage` (body + inline images + attachments) and store it
  with `EmailFileStore.SaveAsync`.
  - `InvitationEmail` and `MailPlanSend` get `EmailFile` (bare file name, `[MaxLength(100)]`).
    `PathOf` already refuses anything else.
  - Use a separate folder `App:MailOutgoingDir` (default `mail-outgoing/`). It is **not** cleaned
    after 26 h.
  - `EmailFileStore` today has one folder, read from `App:MailQueueDir` in its constructor. Give
    it the folder through the constructor (config key plus default name), and register a second,
    keyed instance for outgoing mail. Keep the `PathOf` guard and the 26 h `DeleteOld` on the
    `mail-queue/` instance only.
  - Delete the file when the email is fully delivered or the row is removed.
- [x] Editing a scheduled email:
  - `GET` returns the attachment list (name, size) read from the `.eml` with MimeKit.
  - `PUT` sends `keepAttachments: string[]` plus new files, and the `.eml` is rebuilt.
- [x] Sending:
  - Dispatchers send the stored `MimeMessage`, setting To/Cc/Bcc per batch (see F10).
  - `EmailSender` / `EmailRetryDispatcher` retries reuse a copy in `mail-queue/` (existing
    path).
  - The Viber email fallback and late emails (`MailPlanDispatcher.SendLateEmailAsync`) use the
    same `.eml`.
- [x] Limits: 25 MB total per email including inline images (base64 counted), Admin only. A plan
  that is Viber-only refuses attachments (400).
- [x] Web: a new `shared/mail-attachments/` component (separate .html/.scss) with a list of files
  (name, size, remove) and a total-size bar. Use it in the invitation composer and the
  mail-plan dialog.
- [x] Deployment rule: `mail-outgoing/` holds personal data. Never serve it, never delete it on
  deploy. Update `.claude/rules/deployment.md`, `.claude/rules/security.md` and
  `documentation/deployment.md`.

### 3B. F10 Batches + live progress over SignalR

- [x] At most **100 recipients per SMTP message**. CC addresses count toward the first batch.
  Invitations still send one calendar invite per batch.
- [x] `InvitationEmail.DeliveredRecipientsJson` and `MailPlanSend.DeliveredRecipientsJson`
  (`string?`) record each address once its batch is accepted. A retry sends only to addresses
  not yet in it. A failure message reads "Sent to X of Y — the rest will be retried".
- [x] Progress over SignalR: follow the existing `NotificationService.ViberBroadcastProgressAsync`
  (`Services/NotificationService.cs:93`) and `realtime.service.ts` `viberBroadcastProgress$`
  pattern.
  - Add `MailSendProgressAsync(int userId, MailSendProgressNotice { source: 'invitation'|'plan',
    id, sent, total, failed, done })`, sent to `NotificationHub.UserGroup(senderUserId)` after
    each batch.
  - Scheduled sends, which have no user waiting, go to `NotificationHub.AdminGroup` (`"admins"`,
    `Hubs/NotificationHub.cs:18`).
- [x] Web: the composer and planner replace the spinner with a progress bar showing "Sending… X of
  Y", driven by `mailSendProgress$`. Fall back to the HTTP result when SignalR is disconnected.

### 3C. F13 `*@domain`, exclusions, send immediately

- [x] Domain entry: an address field token matching `^\*@[a-z0-9.-]+\.[a-z]{2,}$` (trimmed,
  lower-case) expands server-side to every **active** user whose email ends with `@domain`.
  - New `GET api/mail-plans/expand?pattern=*@company.com` (Admin) returns the list for the UI.
  - The same helper is used by the invitation composer (To and CC) and the Mail Planner.
  - The UI shows the expanded people as removable chips under the field.
  - The saved request holds the **expanded addresses**, not the pattern. The pattern is never
    sent to the mail server.
  - External distribution lists are not expanded (no directory access).
- [x] Exclusions on app groups: `MailPlan.ExcludedEmailsJson`, and
  `MailPlanAudience.ResolveAsync(groupIds, emails, excluded)` removes them. The plan dialog lists
  group members with tick boxes.
- [x] Send immediately: `MailPlan.SendImmediately` (bool) and a nullable `SendAt`. On save the
  plan is queued for the next scheduler tick (`MailPlanScheduler`), and the UI shows live
  progress (F10).
  - Keep PALGOLF channel rules: CC is email-only; mobile is tried once, with email fallback per
    settings.
  - Viber text is 1–1000 characters.

### Phase 3 tests and docs

- [x] API tests:
  - Attachments survive edit and retry (`.eml` read back).
  - A Viber-only plan with attachments returns 400.
  - Over 25 MB is refused.
  - 230 recipients go out in 3 messages; after a failed batch, the retry skips delivered
    addresses.
  - A progress notice is sent for each batch (use a test hub client or a fake `IHubContext`).
  - `*@domain` expands only active users of that domain.
  - Exclusions are removed from the resolved recipients.
  - Send-immediately goes out on the next tick.
- [x] Web specs: domain-pattern parsing, the attachments component size total, and progress
  reduction.
- [x] Docs: admin-guide (attachments, progress, `*@domain`, exclusions, send now),
  `documentation/configuration-and-publishing.md` (`App:MailOutgoingDir`).

---

## Phase 4 — F14 Automatic post-event feedback ✅ Done (2026-11-25)

One migration: `EventFeedbackAutomation`. KURT reference: `Models/EventFeedbackCampaign.cs`,
`Services/EventFeedbackService.cs`, `Controllers/{Feedback,PublicFeedback}Controller.cs`,
`pages/event-feedback/*`, `my-events/feedback-dialog*`, event-detail Feedback tab,
`documentation/event-feedback-automation.md` and `tests/EventFeedback/Program.cs`. **Do not**
copy KURT's inline HTML email, its own retry loop or its invitee audience.

- [x] Entities:
  - `EventFeedbackCampaign` (EventId PK, Enabled, PreparedAt).
  - `EventFeedbackRecipient` (Id, EventId, RegistrationId, UserId?, Name, Email?, Phone?, Token
    Guid unique, Status Pending/Sent/Failed, SentAt, SubmittedAt; unique (EventId,
    RegistrationId)).
  - `Feedback` gets `FeedbackRecipientId?`, `IsAnonymous`, `DisplayName?` (≤200),
    `OrganizationRating?` (1–5), `WentWell?` and `Improve?` (≤1000 each), and `UserId`
    becomes nullable.
  - Replace the unique index `(EventId, UserId)` (`AppDbContext.cs:156`) with filtered unique
    indexes on `(EventId, UserId) WHERE UserId IS NOT NULL` and `(EventId, FeedbackRecipientId)
    WHERE FeedbackRecipientId IS NOT NULL`.
- [x] Audience: **confirmed** registrations of the event, including team members registered by a
  captain, excluding cancelled or soft-deleted ones and deactivated users. Freeze the audience on
  first prepare.
- [x] `EventFeedbackScheduler` (hosted service, every 60 s, injected `TimeProvider`, registered as
  `TimeProvider.System`):
  - Prepares enabled campaigns whose event has ended (server local time; server is on Philippine
    time) and is not Draft.
  - Sends each recipient through **`MessageDispatcher`** with a new
    `MessageTemplates.FeedbackRequest(eventTitle, name, url)` (SMS, Viber and themed email).
  - The URL is `{App:FrontendBaseUrl}/feedback/{token}`.
  - Retries are the dispatcher's own; no separate loop.
- [x] Endpoints:
  - Admin: `PUT api/events/{id}/feedback/automation` `{ enabled }`.
  - Admin: `POST api/events/{id}/feedback/automation/retry` (failed only).
  - The existing `GET api/events/{id}/feedback` adds the campaign state and counts.
  - Public `PublicFeedbackController`, `AllowAnonymous`, rate-limited (Phase 1 limiter):
    - `GET api/feedback/{token:guid}` returns the event title, dates and whether it was already
      submitted.
    - `POST api/feedback/{token:guid}`: ratings 1–5; comment, went-well and improve ≤1000 each;
      `isAnonymous`. Returns 409 before the event ends. A second POST updates the answer.
  - The signed-in `FeedbackController` POST gains `isAnonymous` and the structured fields.
- [x] Anonymous answers: organizer views show "Anonymous" and never the name, email or phone.
  The recipient link is still stored so the person can't answer twice.
- [x] Web:
  - Page `pages/event-feedback/` (separate .html/.scss), public route `feedback/:token`.
  - Event-detail Feedback tab: a "Send feedback form after the event" switch, status counts
    (pending / sent / failed / answered) and a Retry failed button.
  - "Submit anonymously" tick box in `my-events/feedback-dialog`.
- [x] Tests:
  - Port KURT `tests/EventFeedback` checks that still apply:
    - the audience is only confirmed registrants (captain-registered members included);
    - nothing is sent before the event ends;
    - a token works only for its event;
    - anonymous answers are hidden in the admin view;
    - a second submit updates the answer;
    - 409 before the end;
    - throttling.
  - Add: a phone-only registrant gets the link by mobile (`ChannelsAsync`).
- [x] Docs:
  - `.claude/rules/validation.md`: the feedback rule becomes "registered attendee or feedback-link
    holder, one per person/event"; add rows for the new fields.
  - `.claude/rules/registration-flow.md`: the feedback link message goes through
    MessageDispatcher.
  - Admin and participant guides; `documentation/event-feedback-automation.md` (PALGOLF
    version).

---

## Phase 5 — F16 Department / Job Role, switchable per installation ✅ Done (2026-11-25)

One migration: `UserDepartmentAndJobRole`. KURT reference: commit `6a2a8d6` user files. PALGOLF
differs: both fields are **optional and off by default**, and sign-up is unchanged.

- [x] `User.Department` (`[MaxLength(100)]`) and `User.JobRole` (`[MaxLength(200)]`), both
  nullable and trimmed.
- [x] New file `backend/EventRegistration.Api/user-fields.json`, handled exactly like
  `categories.json`:
  - loaded with `AddJsonFile(..., optional: true, reloadOnChange: true)` in `Program.cs`;
  - read through `IOptionsMonitor<UserFieldOptions>`;
  - excluded from publish in the `.csproj`;
  - shaped as `{ "UserFields": { "Department": { "Enabled": false }, "JobRole": { "Enabled":
    false } } }`, with a `"// ..."` help key like `categories.json`.
- [x] `UserFieldCatalog` service and public `GET api/settings/user-fields` (AllowAnonymous, like
  `api/settings/categories`).
- [x] Server: a disabled field is ignored on input, so existing values are never overwritten or
  cleared, and it is left out of `UserDto` / `ProfileDto` output.
- [x] Web: a `user-fields.service.ts` (pattern: `core/category.service.ts`). The fields show only
  when enabled, on the admin user form, users list column, My Account and the CSV import
  template/columns.
- [x] Tests:
  - Disabled (default): the fields are ignored and not returned.
  - Enabled: saved and returned; length limits enforced.
  - Editing the file applies without a restart (use the pattern in `CategoryCatalogTests`).
- [x] Docs:
  - `.claude/rules/deployment.md` and `.claude/rules/demo-and-config.md`: `user-fields.json` is
    not published, so copy it once and never delete it.
  - `documentation/configuration-and-publishing.md`.
  - The validation.md field table.
  - Admin guide.

---

## Final verification (after all phases)

- [x] `dotnet test backend/EventRegistration.Api.Tests` green (379 tests).
- [x] `npm test` and `npx ng build` green in `frontend/web` (330 tests).
- [x] `dotnet ef migrations list` shows the 5 new migrations after `20261123074103_EmailRetryEmailFile`,
  and none of KURT's migration names.
- [ ] Run the `verify` skill for:
  - deactivate (an open session is logged out on the next call);
  - set-password link;
  - forgot password;
  - invite link for an account holder vs a guest;
  - update banner;
  - meal scanner start;
  - photo separation on TV;
  - an attachment email in `outbox/`;
  - live progress;
  - `*@domain`;
  - the feedback link end to end;
  - user fields on and off.
- [x] Grep that no declined feature slipped in: `MailConversation`, `RecycledAt`, `ReplyToAddress`,
  `SmtpMessageId`, `api-base.util`, `@n-pax.com` (no hits).

## Change log

| Date | Change |
|------|--------|
| 2026-11-25 | Plan created from the KURT vs PALGOLF fork-point analysis |
| 2026-11-25 | Features reviewed one by one with the product owner; matrix recorded; rewritten as an executable implementation plan |
| 2026-11-25 | Phase 1 implemented: deactivation + ActiveUserMiddleware, passwordless accounts with set-password links, forgot password + rate limiting, invite link no longer signs in, update banner + no-cache, meal scanner camera fix, screen-display height, configurable inline-image limits |
| 2026-11-25 | Phase 2 implemented: separate FacePhoto on users/registrations, gallery prefers it, display photo everywhere else, offline bundle carries guest display photo, docs added |
| 2026-11-25 | Phase 3 implemented: attachments stored in the outgoing .eml (multipart compose/save), 100-recipient batches with delivered-address tracking and SignalR progress, *@domain expansion, plan exclusions, send-immediately |
| 2026-11-25 | Phase 4 implemented: feedback campaign/recipient tables, scheduler + MessageDispatcher links, public rate-limited form, anonymous answers, admin switch/counts/retry, public page |
| 2026-11-25 | Phase 5 implemented: Department / Job role with user-fields.json (off by default), public catalog endpoint, admin/account/CSV UI, docs |
| 2026-11-25 | Final: 5 migrations, 379 backend + 330 web tests green, web build green, declined-feature grep clean; manual `verify` run-through still to do |
| 2026-11-25 | Review fixes applied (H1–H4, M1–M8, L1–L13; see kurt-feature-port-fixes.md): 391 backend + 330 web tests green |
| 2026-11-26 | Closed and moved to plans/completed; the manual `verify` run-through was not recorded here |
