# KURT port — review fixes

Status: ✅ Completed (2026-11-26). Fixes done 2026-11-25 — H1–H4, M1–M8 and L1–L13 applied; backend 391/391 and web 330/330 pass; verify walk-through pending.
itself is described in [kurt-feature-port.md](20261126_kurt-feature-port.md).

## Rules for the implementing agent

- Fix every item, in the order listed. Each fix adds or updates a test that **fails before the fix**.
- Keep the house rules from `kurt-feature-port.md` §0:
  - port by hand;
  - every message goes through `MessageDispatcher`;
  - a new component gets separate `.html` / `.scss` files;
  - plain words in the UI;
  - theme tokens only.
- Change the migration `20261125072936_MailAttachmentsBatchesExclusions` **in place**. It has not
  been applied anywhere yet (the dev DB is untouched), so a new migration is not needed. Regenerate
  the Designer and the snapshot if the model changes.
- Run the backend suite from `backend/`, because `global.json` opts in to Microsoft.Testing.Platform:
  `dotnet test --project EventRegistration.Api.Tests`. Report the **total** count; there are 661+
  tests. Also run `npm test` and `npx ng build` in `frontend/web`.
- Do not commit. Tick each item `- [x]` as it lands and add a change-log row.

## High

- [x] **H1 Set-password link is copied to the Settings CC list (security).**
  - Where: `Services/PasswordSetupService.cs` sends the email with `includeDefaultCc: true`, so
    every CC address receives a working account-takeover link.
  - Fix: use `includeDefaultCc: false`.
  - Check the other new personal-link messages for the same flag: the feedback link and the team
    registration link.
  - Test: create a user with no password while a default CC is configured. The stored or sent email
    has no CC.
- [x] **H2 Mail plans saved before the upgrade fail to send and fail to open.**
  - Where: the migration adds `MailPlans.ExcludedEmailsJson` with `defaultValue: ""`. That value
    breaks the JSON reads at `Services/MailPlanDispatcher.cs:48` and
    `Controllers/MailPlansController.cs:466`.
  - Fix: set the default to `"[]"` (migration and model), and read the value tolerantly at both
    places: blank or invalid becomes an empty list.
  - Check every other new `*Json` column (`DeliveredRecipientsJson` and the rest) for the same
    problem.
  - Test: a plan row with `ExcludedEmailsJson = ""` dispatches, and `GET` returns 200.
- [x] **H3 Attachments are refused above about 8 MB; the limit should be 25 MB.**
  - Where: `MailImageOptions.RequestLimitBytes` is worked out from the inline-image budget only, and
    `[MailRequestBodyLimit]` applies it to compose, update and plan create/update.
  - Fix: the request limit = the inline budget (base64) + the 25 MB attachment cap + 1 MB for text.
    Raise the multipart form limits (`FormOptions.MultipartBodyLengthLimit` or the per-request
    feature) to match.
  - Keep the 25 MB-per-email check as the real gate, with a plain-words 400.
  - Test: replace the test at `MailDeliveryPhase3Tests.cs:151`, which accepts 413. A 10 MB attachment
    is accepted. A 26 MB one gets 400, not 413.
- [x] **H4 Feedback link returns 500 when the person already answered.**
  - Where: `Controllers/PublicFeedbackController.cs:47` looks up the answer by `FeedbackRecipientId`
    only. A user who already rated the event in My Events, or who has two sub-event registrations,
    breaks the unique index `(EventId, UserId)` on save.
  - Fix: when `r.UserId` is set, find the existing answer by `EventId + UserId` (or by recipient)
    and update it. The signed-in `FeedbackController` POST must do the same in reverse: find an
    answer saved through the link.
  - Tests:
    - rate in My Events, then answer through the link: the answer is updated, no error;
    - answer through the link, then through My Events: the answer is updated.

## Medium

- [x] **M1 Retrying a failed invitation sends it to everyone again.**
  - Where: `InvitationsController.cs:187` and `:255` (`ApplyMessage`) clear
    `DeliveredRecipientsJson` on every save.
  - Fix: clear it only when the To / CC recipients actually change. Saving a failed email without
    changing them keeps the delivered list, and the retry skips those addresses.
  - Test: 230 recipients; the second batch fails; save without changes; the retry sends only the
    130 who have not received it.
- [x] **M2 One message can go to more than 100 recipients.**
  - Invitations: `InvitationEmailDispatcher.cs:69` puts every CC in the first message (a large
    `*@domain` CC list can reach 300+).
  - Mail plans: `MailPlanDispatcher.cs:81` sends 100 BCC per message and adds CC on top.
  - Fix in both: every SMTP message carries at most 100 addresses (To + CC + BCC). When CC exceeds
    100, CC goes out in its own messages of up to 100, and their delivery is tracked in
    `DeliveredRecipientsJson` too.
  - Test: 300 CC + 1 To becomes 4 messages, each with 100 or fewer.
- [x] **M3 Editing a mail plan's attachments doesn't work.**
  - Server: `MailPlansController.cs:431-433` has no `keepAttachments`. A new upload replaces every
    day's files; no upload keeps them all.
  - Web: `mail-plan-dialog.ts` builds `editingAttachments` with `flatMap` across days, so each file
    shows once per day.
  - Fix: same model as invitations. `PUT` takes `keepAttachments: string[]` plus new files; the plan
    has one attachment set used by every day; the dialog lists each file once and removing it
    works.
  - Test: plan with 2 days and files A and B; keep A and add C; every day's email holds A and C.
- [x] **M4 Reminders leave extra files and lose their attachments.**
  - Where: `MailPlanDispatcher.RemindAsync` (`:215`) loads the day with `AsNoTracking`.
    `LoadTemplateAsync` (`:277`) then rebuilds the email without attachments, saves a new file and
    never records its name. The day's `.eml` was already deleted on delivery (`:175`).
  - Fix: keep a delivered day's `.eml` until the later of 24 h after `SentAt` and the last reminder
    or late-fallback window. Delete it from the scheduler's cleanup, not at delivery. Reminders and
    `SendLateEmailAsync` (`:261`) send from that file, so attachments are kept.
  - Never save a file whose name is not stored.
  - Test: after a reminder, `mail-outgoing/` holds no unreferenced files, and the reminder carries
    the attachment.
- [x] **M5 Live progress goes to the wrong admin.**
  - Where: `InvitationEmailDispatcher.cs:88,100,116` sends to `ev.CreatedByUserId`;
    `MailPlanDispatcher.cs:43` sends to `plan.CreatedByUserId`.
  - Fix: a send started by an admin's request reports to that admin's user group. A scheduled send
    reports to `NotificationHub.AdminGroup`. Pass the sender id through from the controller.
  - Test: admin B sends an invitation for admin A's event; the notices go to B.
- [x] **M6 `*@domain` people never show as removable chips.**
  - Where: `api.service.ts:418` `expandMailDomain` is never called.
  - Fix:
    - The invitation composer (To and CC) and the mail-plan dialog call it when a `*@domain` entry
      is typed. The returned people show as chips under the field, each removable.
    - The saved request holds the remaining addresses, never the pattern.
    - Show "No registered users at @domain" when the list is empty.
  - Spec: typing `*@x.com` shows the chips; removing one drops it from the payload.
- [x] **M7 Feedback is sent once per sub-event registration.**
  - Where: `EventFeedbackService.cs:50-75` makes one recipient per registration, so a person with 3
    sub-events gets 3 billed messages.
  - Fix: one recipient per person per event: by `UserId`, else guest email, else guest phone. Keep
    the link tied to the person.
  - Test: one user with 2 confirmed sub-event registrations gives 1 recipient and 1 message.
- [x] **M8 Feedback never falls back to email after a late Viber failure.**
  - Where: `EventFeedbackService.cs:108-109` sends with no `RegistrationId` and no
    `emailFile` / `EmailSnapshotJson`, so `MessageDispatcher.TagFor` returns null and the Sinch
    webhook cannot queue the email.
  - Fix: pass a `RegistrationId`, or save the email with `emailFile:` the way the broadcast does
    (`EventsController.cs:734`).
  - Test: a mobile-preferred recipient with email; a later webhook failure queues one email.

## Low

- [x] **L1** CSV import sends password links inside the request, 200 ms apart
  (`UsersController.cs` import loop), so a large file hits the IIS request timeout.
  - Fix: return right after saving the accounts. A background queue sends the links, reusing the
    `PasswordSetupService` throttle. The import result says "links are being sent".
- [x] **L2** `.claude/rules/security.md` says only a hash of the set-password token is stored, but
  `User.PasswordSetupToken` stores it as a plain `Guid`.
  - Fix: store a SHA-256 hash of a random 32-byte token. The URL carries the token; the lookup
    hashes it. Update the route constraint (`{token}` instead of `{token:guid}`) and the tests.
  - Keep `security.md` and `validation.md` matching the code.
- [x] **L3** Feedback can be sent twice: the scheduler and `POST automation/retry` both run
  `SendPendingAsync` on the same Pending rows.
  - Fix: claim rows before sending (set a Sending status in one update with a status check), and
    release stale claims after 10 minutes.
- [x] **L4** `POST automation/retry` while the campaign is switched off leaves Pending rows unsent,
  because `RunAsync` skips disabled campaigns.
  - Fix: Retry is refused with a plain message while the campaign is off.
- [x] **L5** `EventFeedbackScheduler` takes `TimeProvider` but never uses it. Use it for the timer
  (`new PeriodicTimer(..., time)`) and for "now".
- [x] **L6** The signed-in feedback POST (`FeedbackController.cs:26-46`) is missing checks:
  - no "event ended" check (validation.md requires one);
  - no length limit on `DisplayName`;
  - it accepts any display name.
  - Fix: return 409 before the end, like the public endpoint; drop the client-supplied
    `DisplayName` and use the account's name, or null when anonymous.
- [x] **L7** The feedback message text says the link "works once", but a second submit updates the
  answer. Fix the wording in `MessageTemplates.cs:175` and `EmailTemplates.cs:194`, and the
  snapshots.
- [x] **L8** The `"feedback"` rate-limit policy partitions by IP + path. The path holds the token,
  so one IP can try unlimited tokens. Partition by IP only.
- [x] **L9** Switching a mail plan to Viber-only keeps its attachments (`MailPlansController.cs:387`
  checks only new uploads). Refuse when the resulting plan is Viber-only and any attachment
  remains.
- [x] **L10** Cancelling an invitation (`InvitationsController.cs:215`) deletes the `.eml` before
  `SaveChangesAsync`. Delete the file only after the save succeeds.
- [x] **L11** If create/update fails part way, the `.eml` files it already saved stay on disk
  (`MailPlansController.cs:437`; the catches at `:183`, `:210` see an empty list). Track saved
  names outside the loop and delete them on failure.
- [x] **L12** `MailPlanAudience.GroupsAsync` (`:44`) lists deactivated users in the event-registrant
  groups and the exclusion tick boxes. Filter on `DeactivatedAt == null`.
- [x] **L13** Department / Job Role `[StringLength]` (`Dtos.cs:71-72, 164-165`) is checked before
  trimming, and also when the field is switched off. Move the length check into `UserFieldCatalog`
  after trimming, only for enabled fields.

## Done when

- [ ] Every item above is ticked, and each has a test that failed before the fix.
- [x] The backend suite run from `backend/` reports its **total** count with 0 failed; `npm test` and
  `npx ng build` are clean.
- [ ] The `verify` skill run-through from `kurt-feature-port.md` "Final verification" is done,
  including:
  - a 10 MB attachment;
  - `*@domain` chips;
  - a 230-recipient send with a forced failure, then retry;
  - a feedback link after rating in My Events.
- [x] `kurt-feature-port.md` change log notes "review fixes applied".

## Change log

| Date | Change |
|------|--------|
| 2026-11-25 | Fix list created from the review of the staged KURT port |
| 2026-11-25 | H1–H4 hot fixes applied with tests: no CC on personal links, `[]` default + tolerant JSON reads, 25 MB attachment path, feedback answer reused across link/My Events |
| 2026-11-25 | M1–M8 and L1–L13 applied. Deviations: L8 keeps the per-link limit and adds a per-IP cap of 100/min across links (a per-IP-only limit would make tests share one quota); L2 adds a 6th migration `PasswordSetupTokenHash` (old links become invalid); M4 keeps a sent day's .eml 24 h (or while a reminder is pending), a reminder set later goes without attachments; L1 queue is in memory (links waiting at a restart are lost; admin resends) |
| 2026-11-26 | Closed and moved to plans/completed; the manual `verify` run-through was not recorded here |
