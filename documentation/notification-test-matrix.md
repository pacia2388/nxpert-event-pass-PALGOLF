# Notification test matrix

Automated tests for every message the app sends by **email**, **Viber** and **SMS**, including
fallback, failed, turned-off and retry cases. Code: `backend/EventRegistration.Api.Tests/NotificationMatrixTests.cs`
(case ids below match the test names / data). Last run: 2026-11-13, **140 / 140 passed** (whole suite).

## How the tests send

No real provider is called. In tests each channel has a **test send folder** (config
`Email:TestSendDir`, `Viber:TestSendDir`, `Sms:TestSendDir`; ignored in Production):

| Folder (under `EventRegistration.Api.Tests/bin/Debug/net10.0/`) | Holds |
|---|---|
| `test/sent/email/` | One `.eml` per email (full message: To / CC / BCC, HTML, QR and meal-stub images, `invite.ics`) |
| `test/sent/viber/` | One `.txt` per Viber message (+ `.png` for the QR picture) |
| `test/sent/sms/` | One `.txt` per SMS |

File name: `<time>_<recipient>_<subject>`, so the folder a message landed in shows the channel that
delivered it. Folders are emptied at the start of each run and kept afterwards for inspection.

Each test sets every channel to one of:

| Mode | Meaning |
|---|---|
| **Send** | Writes to its folder; counts as delivered |
| **Fail** | Every send fails (provider error) — fallback / retry kicks in |
| **Off** | Not set up — skipped, as in Production (`NotConfigured`) |

Plus the switches `Viber:FallbackToEmail`, `Sms:FallbackToEmail`, the active mobile provider
(Settings → Viber or SMS) and, for "unlisted", the `+63` row removed from the Phone country table
for that channel.

## P — Pass on confirm (registrant delivery chain)

Guest registers with email + `+639…` phone, admin confirms. ✉ = email folder, V = viber, S = sms.

| Id | Provider | Registrant channel | Email | Viber | SMS | Email fallback | Phone country | Result | Retry row | Admin count |
|---|---|---|---|---|---|---|---|---|---|---|
| P01 | Viber | Email | Send | – | – | – | – | ✉ | – | sent |
| P02 | Viber | Email | **Fail** | – | – | – | – | nothing | ✅ Pass | retrying |
| P03 | Viber | Email | **Off** | – | – | – | – | nothing | – | not sent |
| P04 | Viber | Mobile | Send | Send | – | on | listed | V | – | sent |
| P05 | Viber | Mobile | Send | **Fail** | Send | on | listed | ✉ (no SMS) | – | sent (fell back) |
| P06 | Viber | Mobile | Send | **Fail** | Send | **off** | listed | nothing | – | not sent |
| P07 | Viber | Mobile | Send | **Off** | Send | off | listed | ✉ (skipped, switch ignored) | – | sent |
| P08 | SMS | Mobile | Send | Send | Send | on | listed | S | – | sent |
| P09 | SMS | Mobile | Send | Send | **Fail** | on | listed | ✉ (no Viber) | – | sent (fell back) |
| P10 | SMS | Mobile | Send | Send | **Fail** | **off** | listed | nothing | – | not sent |
| P11 | SMS | Mobile | Send | Send | **Off** | off | listed | ✉ | – | sent |
| P12 | Viber | Mobile | **Fail** | **Fail** | Send | on | listed | nothing | ✅ Pass | retrying |
| P13 | Viber | Both | Send | Send | Send | on | listed | ✉ + V | – | sent |
| P14 | Viber | Both | Send | **Fail** | Send | on | listed | ✉ only (no SMS) | – | sent |
| P15 | Viber | Both | **Fail** | Send | Send | on | listed | V | ✅ Pass | sent |
| P16 | SMS | Both | Send | Send | Send | on | listed | ✉ + S | – | sent |
| P17 | Viber | Mobile | Send | Send | Send | **off** | **unlisted for Viber** | ✉ (skipped, switch ignored) | – | sent |
| P18 | SMS | Mobile | Send | Send | Send | **off** | **unlisted for SMS** | ✉ | – | sent |
| P19 | Viber | Both | Send | Send | Send | off | **unlisted for Viber** | ✉ only | – | sent |

Every P case also checks `PassEmailSentAt`: stamped when something was delivered, empty otherwise.

## M — Every message type

| Id | Message | Trigger in test | Setup | Expected |
|---|---|---|---|---|
| M01 | Pass email | Confirm | Email, hybrid event | ✉ with inline QR (`Content-Id`) and `invite.ics` (`method=REQUEST`) |
| M02 | Viber pass + meal stubs | Confirm | Mobile, event with a meal | V: pass text + QR `.png`, and a `meal-stubs` message |
| M03 | Resend pass | `POST …/attendance/{id}/resend-pass` | Mobile registrant | ✉ only; Viber not sent again |
| M04 | Kit claimed | `POST …/attendance/claim-kit` | Both | ✉ + V "Event kit claimed" |
| M05 | Edit link | `POST /api/registrations/public/{slug}/edit-link` | Both | ✉ + V "Edit your registration" |
| M06 | Meeting update | `PassService.SendMeetingUpdateAsync` | Both, online event | ✉ calendar update (`method=REQUEST`) + V "Updated" |
| M07 | Meeting cancel | `PassService.SendMeetingCancelAsync` | Mobile, online event | ✉ calendar cancel (`method=CANCEL`) + V "Cancelled" |
| M08 | Meeting cancel, email fails | same | Email **Fail** | `EmailRetry` row (MeetingCancel) with snapshot |
| M09 | Team "Complete your registration" | `POST /api/teams/mine/registration-notice` | Email Send, Settings CC set | ✉ with the Settings CC copy; counted sent |
| M10 | Team notice, email fails | same | Email **Fail** | counted sent; `EmailRetry` row (Message) |
| M11 | Team notice, email off | same | Email **Off** | counted failed; no retry row |
| M12 | Direct event invite | `POST /api/events/{id}/invitations` | Email Send | ✉ "You're invited"; sent 1 |
| M13 | Direct invite, email fails | same | Email **Fail** | sent 1 (queued); `EmailRetry` row (Message) |
| M14 | Direct invite, email off | same | Email **Off** | failed 1; no retry row |
| M15 | Calendar update to invitees | `CalendarInvitationNotifier.NotifyAsync` | Email Send | ✉ "Updated" |
| M16 | Calendar update, email fails | same | Email **Fail** | `EmailRetry` row (Message) |
| M17 | Composed invitation | `POST /api/events/{id}/invitations/compose` | Email Send | ✉ "Join us"; status Sent |
| M18 | Composed invitation, email fails | same | Email **Fail** | status Queued (its own retry) |
| M19 | Mail plan, Email channel | `MailPlanDispatcher.DispatchAsync` | — | ✉ (BCC) |
| M20 | Mail plan, Viber channel | same | Viber Send | V only |
| M21 | Mail plan, Viber fails | same | Viber **Fail**, fallback on | ✉ fallback (single message) |
| M22 | Mail plan, Viber fails, fallback off | same | Viber **Fail**, fallback **off** | nothing; plan still Sent |
| M23 | Mail plan, phone country unlisted | same | unlisted for Viber, fallback off | ✉ (skipped, switch ignored) |
| M24 | Mail-plan reminder | `MailPlanDispatcher.RemindAsync` | Email Send | ✉ "Reminder" |
| M25 | Reminder, email fails | same | Email **Fail** | `EmailRetry` row (Message) |
| M26 | Account verification | `POST /api/auth/resend-verification` | Email Send | ✉ "Confirm your email" |
| M27 | Verification, email fails | same | Email **Fail** | nothing; **no** retry row (user can press Resend) |

## R — Background email retry

| Id | Case | Expected |
|---|---|---|
| R01 | Both registrant: pass email failed at confirm, retry runs with email back | ✉ sent by the retry; row Sent; Viber pass **not** sent again |
| R02 | Email keeps failing | 5 attempts, row Failed, no 6th try, nothing in ✉ |
| R03 | Direct invite queued (Message), retry runs | ✉ sent, `invite.ics` kept |
| R04 | Meeting cancel queued, registration then deleted, retry runs | ✉ "Registration cancelled" sent from the snapshot |

## Also covered (other test classes)

| Area | Test class |
|---|---|
| Chain order, stop at first delivery, `AfterFailure` (fallback switch), off = skipped | `NotificationChainTests` |
| Outbox rule per channel: Dev off / Dev failed / Prod off / Prod failed / delivered | `OutboxRuleTests` |
| Every channel off end to end, phone-country API, retry dispatcher edge cases | `NotificationFallbackApiTests` |

## Running the tests

```bash
dotnet test backend/EventRegistration.Api.Tests
```

If `dotnet test` stops with **"Access is denied"** (the test helper process is not allowed to watch
its parent process on some locked-down machines), run the same tests in one process with the xunit
console runner (package `xunit.runner.console` 2.9.3 in the NuGet cache):

```bash
cd backend/EventRegistration.Api.Tests/bin/Debug/net10.0 && dotnet exec --runtimeconfig EventRegistration.Api.Tests.runtimeconfig.json --depsfile EventRegistration.Api.Tests.deps.json ~/.nuget/packages/xunit.runner.console/2.9.3/tools/net6.0/xunit.console.dll EventRegistration.Api.Tests.dll
```

Build first (`dotnet build backend/EventRegistration.Api.Tests`). Add `-class EventRegistration.Api.Tests.NotificationMatrixTests` to run only this matrix.
