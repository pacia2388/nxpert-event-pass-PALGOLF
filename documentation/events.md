# Events

How events work in PAL GOLF: what an event holds, its lifecycle, sub-events, formats, registration,
confirmation and check-in. Table columns are in [database-table.md](database-table.md); who can do
what is in [roles.md](roles.md); meal stubs are in [meals-stub.md](meals-stub.md).

Main code:

| Area | Location |
|---|---|
| API | `backend/EventRegistration.Api/Controllers/EventsController.cs` (events), `RegistrationsController.cs` (sign-up), `AttendanceController.cs` (roster actions), `CheckInController.cs` (scanner) |
| Pass / calendar delivery | `Services/PassService.cs`, `Services/CalendarInvite.cs`, `Services/CalendarInvitationNotifier.cs` |
| Admin UI | `frontend/web/src/app/pages/admin/` (event list, event form, event detail) |
| Public page | `/e/{slug}` (`public-event`), pass page `/r/{token}` |

---

## 1. What an event holds

| Group | Fields (column) | Notes |
|---|---|---|
| Basics | Title, Description, Venue, Banner (`BannerImage`) | Description is shown exactly as typed |
| Dates | Start (`StartsAt`), End (`EndsAt`) | Wall-clock server time. A multi-day event spans all days |
| Format | `Format`: **InPerson** · **Online** · **Hybrid** | See [section 4](#4-formats-in-person-online-hybrid) |
| Online | Meeting link (`MeetingUrl`) | Must start with `https://`. Never shown publicly |
| Visibility | Status (`Status`): **Draft** · **Published** · **Closed**; public link (`IsPublicLinkEnabled`) | See [section 3](#3-status-and-visibility) |
| Capacity | `Capacity` | Blank = unlimited. Per sub-event when sub-events exist |
| Registration page extras | Registration fees, Schedule of activities (`ScheduleJson`), custom form (`FormSchema`) | |
| Scanner | TV welcome message, Verify only (`VerifyOnly`) | Verify only: scanners look up passes but record no check-ins |
| Kit | Notify on kit claim (`NotifyOnKitClaim`) | |
| Children | Sub-events (`Sessions`), meal stub definitions (`Meals`), invitations | |
| System | `PublicSlug` (unique, from title + random suffix), `InviteSequence`, `CreatedByUserId`, `CreatedAt` | |

## 2. Lifecycle

```
 Create ──► Draft / Published ──► Closed ──► (Reopen) Published
                │                                    │
                └────────────── Delete (permanent) ◄─┘
```

| Action | Endpoint | Who | Effect |
|---|---|---|---|
| Create | `POST /api/events` | Admin | Status defaults to **Published** if blank. Slug generated from title |
| Edit | `PUT /api/events/{id}` | Admin | Keeps registrations. Sub-events matched by id; meal definitions rebuilt; calendar updates sent when needed |
| Close | `POST /api/events/{id}/close` | Admin | Status → Closed. New registrations refused |
| Reopen | `POST /api/events/{id}/reopen` | Admin | Status → Published |
| Delete | `DELETE /api/events/{id}` | Admin | Removes the event and **all** its sub-events, registrations, meals and invitations (cascade). Confirmed registrants get a calendar cancellation |

## 3. Status and visibility

| Status | Public link on | Public link off |
|---|---|---|
| **Published** | In *Browse Events*; anyone with the link can register | Invite-only: hidden from *Browse Events*; only invited emails can register |
| **Draft** | Not in *Browse Events* | Not in *Browse Events* |
| **Closed** | Not in *Browse Events*; registration refused | Same |

- *Browse Events* shows only `Status = Published` **and** `IsPublicLinkEnabled = true`.
- **Gap:** the registration endpoints refuse only **Closed** events. A **Draft** event can still be
  registered for by anyone who has its `/e/{slug}` link.

Event card status shown to admins: **Open**, **Full** (capacity reached), **Closed**.

## 4. Formats: in person, online, hybrid

| | In person | Online | Hybrid |
|---|---|---|---|
| Venue | Required | None | Required |
| Meeting link | – | Yes | Yes |
| QR entry pass | ✓ | ✗ | ✓ |
| Meal stubs | ✓ | ✗ | ✓ |
| Door check-in | ✓ | ✗ | ✓ |
| Calendar invite (`invite.ics`) | ✓ | ✓ with link | ✓ with link |

The meeting link reaches **confirmed** registrants only (email button, calendar invite, Viber/SMS
text, *My Events* "Join meeting"). A sub-event can override the link (falls back to the event's).

**Automatic calendar updates** (`InviteSequence` is bumped so the new invite replaces the old one):

| Change | Confirmed registrants get |
|---|---|
| Time, title, venue or link changed | Updated invite |
| Format switched | New invite (online → in person removes the meeting) |
| Event deleted | Cancellation |
| Registrant cancels a confirmed registration | Cancellation |

Invite times use the server's time zone; keep the server on Philippine time.

## 5. Sub-events (sessions)

Optional. Use them when people sign up for days or sessions **separately**.

- Each sub-event has its own title, dates, optional venue (falls back to the event's), capacity,
  meeting link and poster.
- People get **one registration, pass, confirmation and check-in per sub-event**.
- A user can register for several sub-events in one go; each becomes its own `Registrations` row.
- Check-in for a sub-event opens only on its own dates.
- A sub-event **with registrations cannot be removed** (edit returns an error).
- Without sub-events, a registration covers the whole event (`SessionId` = NULL).

## 6. Registration

All new registrations start **Pending**. No pass is sent until someone confirms.

| Path | Endpoint | Notes |
|---|---|---|
| Account | `POST /api/registrations` | Signed in. Details come from the user's profile. Blocked if email verification is required and not done |
| Guest | `POST /api/registrations/guest/{slug}` | No account. Name and ID No. required; email or mobile depending on chosen channel (Email / Viber / Both); optional photo and face data |
| Offline guest sync | `POST /api/registrations/offline-sync` | Guest sign-ups captured offline, uploaded later |
| Invite link | `/e/{slug}?invite={token}` | Pre-fills the page; account holders are signed in |

Refused when:

| Condition | Message |
|---|---|
| Event Closed | "Registration for this event is closed." |
| Capacity reached (event or sub-event) | "This event is already full." / "'{sub-event}' is already full." |
| Invite-only and email not invited | "This event is invite-only…" |
| Sub-event not part of the event / removed | "One of the selected sub-events is not part of this event." / "That sub-event no longer exists." |

Duplicates: one active account registration per user per event/sub-event (unique index).
Guests are de-duplicated by ID No. per event/sub-event.

**Gap:** the capacity check counts then inserts without a lock, so two simultaneous sign-ups can
exceed capacity (the validation rules require an atomic check).

### Custom registration form

`FormSchema` holds the questions. Field types: `text`, `textarea`, `number`, `date`, `time`,
`select`, `multiselect`, `radio`, `checkbox`, `image` (max 8 MB). Optional category heading and
"show only when" (depends on a radio answer above). Answers are stored per registration in
`FormResponses` and appear as Attendance columns and in the CSV export. Registrants can edit
answers until they check in or the event ends.

## 7. Confirmation and the pass

Registration states (derived from `Registrations` columns):

```
Pending ──confirm──► Confirmed ──check in──► Checked in
   │                    │
   └─cancel (removed)   └─(admin delete = soft delete)
```

| Action | Endpoint | Who | Effect |
|---|---|---|---|
| Confirm | `POST /api/events/{id}/attendance/confirm` | Admin, Can confirm | Sets `ConfirmedAt`; sends pass (+ meal stubs, + link and invite) by the registrant's channel |
| Check in (manual) | `POST .../attendance/checkin` | Admin, Can confirm | `CheckInMethod = Manual`; pending rows are confirmed first |
| Cancel | `POST .../attendance/cancel` | Admin, Can confirm | Pending rows only |
| Remove | `DELETE .../attendance/{registrationId}` | Admin | Soft delete (`DeletedAt`); pass stops working |
| Resend pass | `POST .../attendance/{registrationId}/resend-pass` | Admin, Can confirm | Confirmed only |
| Participant cancel | `DELETE /api/registrations/{id}` | Owner, Admin | Pending only |

The pass is a QR code with payload `REG:{token}`, shown at `/r/{token}` and attached to the email.
Delivery channel: Email, Viber or Both (SMS when Settings → mobile channel = SMS; text only).

## 8. Check-in

| Rule | Detail |
|---|---|
| Who | Admin / SuperAdmin (scanner and face TV) |
| When | Today must be within the event's (or sub-event's) start–end **dates**, inclusive. Otherwise "Not open yet" / "Event ended" |
| What | Only **confirmed** registrations. Otherwise "Not confirmed" |
| Repeat | "Already checked in" |
| Verify only | Pass is looked up and shown, no check-in recorded |
| Methods | `Scan` (QR / barcode), `Face` (browser face match or face engine TV), `Manual` (Attendance tab) |
| Offline | Scanner queues check-ins on the device and syncs via `POST /api/checkin/sync`; or load the event's **Offline bundle** |

Face TV: the browser sends webcam frames to the face engine, which matches against the event's
registrant photos (gallery from `GET /api/face-engine/events/{id}/gallery`). Recognized people
are checked in through the normal check-in endpoints, so the same rules apply.

## 9. Event detail page (admin)

| Tab / button | Purpose |
|---|---|
| Invite | Type or pick saved addresses, use `@n-pax` for all users, or click **Invite teams** in the To field to tick team members (one by one, or a whole team; the Team Lead is not listed). Untick a member already in To to remove them. Send now or schedule (≤ 500 recipient entries per send) |
| Attendance | Live roster; confirm, check in, cancel, export CSV |
| Kit Claiming | Mark kits claimed (self or on behalf), clear claims |
| Meal Stubs | Issued stubs; manual redeem / clear (see [meals-stub.md](meals-stub.md)) |
| Feedback | Average rating and comments |
| Viber blast | Message (≤ 1000 chars) to registrants who use Viber |
| Edit / Close / Reopen / Delete | Lifecycle (section 2) |
| Offline bundle | Data file for offline check-in |

## 10. Known gaps

| Gap | Where |
|---|---|
| Draft events accept registrations through their direct link | `RegistrationsController.RegistrationBlockedReason` checks Closed only |
| Capacity check is not atomic | Same method (count, then insert) |
