# Automatic post-event feedback

Each event has a switch on its form (create / edit): **Send a feedback form after the event**.
It is saved as the event's campaign row (`EventFeedbackCampaigns`). When on, the feedback
scheduler (every 120 s) waits until the event has ended (server local time — the server is on
Philippine time), then freezes the audience and sends each person one message with a one-time link.

The event's **Feedback** tab has no switch. It shows the counts and two actions: **Send feedback
link** (sends again to everyone in the audience who has not answered; freezes the audience first
when that never happened; up to `MaxPerRun` = 50 per click) and **Retry failed**.

## Audience

- **Confirmed** registrations of the event only, including people a captain registered on a team.
- Pending, cancelled (soft-deleted) and deactivated accounts are left out.
- The audience is frozen the first time it is prepared (`EventFeedbackRecipients`); later
  registrations do not join it.
- A person with no email and no phone is skipped (nothing could reach them).

## Message and link

- Sent through `MessageDispatcher` with `MessageTemplates.FeedbackRequest` (SMS/Viber text) and
  `EmailTemplates.FeedbackRequest` (themed HTML): mobile once, email retried in the background.
- Link: `{App:FrontendBaseUrl}/feedback/{token}`. The token is unique and single-use per person;
  it identifies the frozen recipient, not the signed-in user.
- Opening the link works without signing in. Submitting again **updates** the earlier answer.
- Answers are refused with **409** until the event has ended.

## Data

| Table | Purpose |
|-------|---------|
| `EventFeedbackCampaigns` | one row per event: the switch, `PreparedAt` (audience frozen) |
| `EventFeedbackRecipients` | frozen audience: registration, name/contact, channel, token, Pending/Sent/Failed, submitted |
| `Feedbacks` | the answer: rating 1–5, organization rating 1–5, comment / went well / improve (≤1000 each), `IsAnonymous`, `DisplayName`, optional `User` and `FeedbackRecipient` |

One answer per person per event:
- account answers: unique `(EventId, UserId)` where `UserId` is set;
- link answers: unique `(EventId, FeedbackRecipientId)` where the recipient is set.

An **anonymous** answer still stores the recipient link (so the person cannot answer twice) but
the organizer view shows **"Anonymous"** and never the name, email or phone.

## Admin endpoints

| Endpoint | Use |
|----------|-----|
| `POST api/events/{id}/feedback/automation/send` | send the link again to unanswered recipients; freezes the audience if needed; 409 before the event ends |
| `PUT api/events/{id}/feedback/automation` `{ enabled }` | turn the campaign on/off (the event form does this through `feedbackForm` on create/update) |
| `POST api/events/{id}/feedback/automation/retry` | failed links go back to Pending and are sent now |
| `GET api/events/{id}/feedback` | answers plus the campaign counts (pending / sent / failed / answered) |

Public, rate-limited per IP+token: `GET`/`POST api/feedback/{token}`.
