# Database tables

The database is SQL Server (LocalDB in development), built with EF Core migrations from
[`AppDbContext.cs`](../backend/EventRegistration.Api/Data/AppDbContext.cs). Entity classes live in
[`Models/Entities.cs`](../backend/EventRegistration.Api/Models/Entities.cs),
[`Models/InvitationEmail.cs`](../backend/EventRegistration.Api/Models/InvitationEmail.cs) and
[`Models/MailPlan.cs`](../backend/EventRegistration.Api/Models/MailPlan.cs).

**Enumerated values are plain strings, not lookup tables or DB constraints.** The allowed values are
C# constants (listed under each column) and are checked only in code.

The facerog-engine has no database. It reads registrant photos from the API
(`/api/face-engine/...`) and keeps its gallery in memory.

## Tables at a glance

| Table | Purpose |
|---|---|
| `Users` | Accounts that can sign in: admins, participants, merchants. Profile, photo, face data, role and permissions. |
| `Events` | Events: details, dates, venue, status, capacity, format (in person / online / hybrid), custom form, banner. |
| `Sessions` | Optional sub-events of an event. When an event has sessions, people register per session. |
| `Registrations` | One person's registration for an event or session: account or guest, pass token, confirmation, check-in, kit and meal state. Soft-deleted. |
| `Meals` | Meal stub definitions for an event (per sub-event or per day): name, value, quantity, validity. |
| `MealStubs` | Individual meal stubs issued to a registration on confirmation, each with its own QR control number. |
| `MealRedemptions` | Per-day meal redemption for events without sub-events: one row per registrant per day. |
| `Invitations` | Per-email invitation to an event, with its magic-link token. |
| `InvitationEmails` | Invitation email messages (recipients, content, schedule, delivery status and retries). |
| `MailPlans` | Mail planner plans: kind, channel, audience, CC. |
| `MailPlanSends` | One target day of a mail plan, with its own subject, body and delivery status. |
| `MailPlanDeliveries` | One receiver of one sent day, plus that receiver's personal reminder (My Reminders). |
| `MailPlanEventNames` | Event names typed in the mail planner, saved for reuse. |
| `MailPlanSavedCcs` | CC addresses saved from the mail planner, offered later. |
| `Feedbacks` | Event rating and comment, one per user per event. |
| `AppSettings` | Key/value store for admin settings. |
| `__EFMigrationsHistory` | EF Core's record of applied migrations (managed by EF). |

## Relationships

```
Users 1─* Registrations *─1 Events 1─* Sessions
                │                │  └─1─* Meals (SessionId optional)
                │                ├─* Meals
                │                ├─* Invitations
                │                ├─* InvitationEmails
                │                ├─* Feedbacks (by EventId/UserId, no FK)
                │                └─* MailPlans (EventId optional, SET NULL on delete)
                ├─* MealStubs
                └─* MealRedemptions
MailPlans 1─* MailPlanSends 1─* MailPlanDeliveries
```

Delete rules: deleting an Event cascades to Sessions, Registrations, Meals, Invitations and
InvitationEmails. A User with registrations cannot be deleted (Restrict). Registration→Session and
Meal→Session are NoAction (cleaned up in code).

---

## Column matrices

Types are the SQL Server types EF generates. `max` means `nvarchar(max)`.
Null = column allows NULL.

### Users

| Column | Type | Null | Notes / enumerated values |
|---|---|---|---|
| Id | int | | PK, identity |
| CompanyId | int | ✓ | Tenant id (seed uses 1) |
| FullName | nvarchar(200) | | Display name built from the parts below |
| FirstName | nvarchar(100) | ✓ | |
| MiddleInitial | nvarchar(10) | ✓ | |
| LastName | nvarchar(100) | ✓ | |
| SenateNo | nvarchar(50) | ✓ | Member ID No. |
| Level | nvarchar(100) | ✓ | Membership level / designation (free text, e.g. Senator, Chairman Circle) |
| PhotoUrl | max | ✓ | Profile photo as data URL; source for the face engine gallery |
| FaceDescriptor | max | ✓ | JSON array of 128 floats (browser face check-in) |
| Email | nvarchar(256) | | **Unique**, lower-case |
| Username | nvarchar(100) | | **Unique**, lower-case login handle |
| PasswordHash | max | | |
| Role | nvarchar(20) | | **`Admin` · `Participant` (default) · `TL` (Team Lead) · `SuperAdmin` · `Merchant`** — see [roles.md](roles.md) |
| Phone | nvarchar(40) | ✓ | Required when channel uses Viber |
| Organization | nvarchar(200) | ✓ | |
| NotificationChannel | nvarchar(10) | | **`Email` (default) · `Viber` · `Both`** |
| CreatedAt | datetime2 | | DB default `GETUTCDATE()` |
| EmailVerified | bit | | |
| EmailVerificationToken | uniqueidentifier | ✓ | Used only when `App:RequireEmailVerification=true` |
| CanConfirmRegistrations | bit | | Grant: confirm registrations without being admin |

### Events

| Column | Type | Null | Notes / enumerated values |
|---|---|---|---|
| Id | int | | PK |
| CompanyId | int | ✓ | |
| Title | nvarchar(300) | | |
| Description | max | | |
| StartsAt | datetime2 | | Wall-clock (server local time) |
| EndsAt | datetime2 | | |
| Venue | nvarchar(300) | | |
| IsPublicLinkEnabled | bit | | Default true; must be on to appear in Browse Events |
| PublicSlug | nvarchar(100) | | **Unique**; public URL `/e/{slug}` |
| CreatedByUserId | int | | User id (no FK) |
| Status | nvarchar(20) | | **`Draft` · `Published` (default) · `Closed`** |
| Capacity | int | ✓ | NULL = unlimited |
| VerifyOnly | bit | | Scanners look passes up but record no check-ins |
| FormSchema | max | ✓ | JSON array of form fields. Field `Type`: **`text` · `textarea` · `number` · `date` · `time` · `select` · `multiselect` · `radio` · `checkbox` · `image`** |
| BannerImage | max | ✓ | Data URL |
| WelcomeMessage | nvarchar(200) | ✓ | Scanner TV greeting |
| RegistrationFees | max | ✓ | Free text |
| ScheduleJson | max | ✓ | JSON array of `{ Day, Time, Activity }` |
| NotifyOnKitClaim | bit | | Notify registrant when kit is claimed |
| Format | nvarchar(10) | | **`InPerson` (default) · `Online` · `Hybrid`** |
| MeetingUrl | nvarchar(1000) | ✓ | Online/Hybrid only; never shown publicly |
| InviteSequence | int | | iCalendar SEQUENCE; bumped on time/title/link change |
| CreatedAt | datetime2 | | DB default `GETUTCDATE()` |

### Sessions (sub-events)

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | int | | PK |
| EventId | int | | FK → Events (cascade) |
| Title | nvarchar(300) | | |
| Description | max | ✓ | |
| StartsAt / EndsAt | datetime2 | | Check-in opens only within these dates |
| Venue | nvarchar(300) | ✓ | Falls back to event venue |
| Capacity | int | ✓ | NULL = unlimited |
| MeetingUrl | nvarchar(1000) | ✓ | Falls back to event MeetingUrl |
| PosterImage | max | ✓ | Data URL |

### Registrations

Either account-based (`UserId` set) or guest (`Guest*` fields set). Soft-deleted rows
(`DeletedAt` set) are hidden by a global query filter, so their pass stops working.

| Column | Type | Null | Notes / enumerated values |
|---|---|---|---|
| Id | int | | PK |
| DeletedAt | datetime2 | ✓ | Soft delete |
| DeletedByUserId | int | ✓ | |
| EventId | int | | FK → Events (cascade) |
| SessionId | int | ✓ | FK → Sessions; NULL = whole event |
| UserId | int | ✓ | FK → Users (restrict); NULL = guest. Unique per (EventId, SessionId, UserId) among active rows |
| GuestFullName | nvarchar(200) | ✓ | |
| GuestSenateNo | nvarchar(50) | ✓ | Used for guest de-duplication |
| GuestEmail | nvarchar(256) | ✓ | |
| GuestPhone | nvarchar(40) | ✓ | |
| GuestNotificationChannel | nvarchar(10) | ✓ | **`Email` · `Viber` · `Both`** (guests only) |
| Token | uniqueidentifier | | **Unique**; QR pass payload, `/r/{token}` |
| RegisteredAt | datetime2 | | DB default `GETUTCDATE()` |
| ConfirmedAt | datetime2 | ✓ | NULL = **Pending**; set = **Confirmed** (pass sent) |
| CheckedInAt | datetime2 | ✓ | |
| CheckInMethod | nvarchar(10) | ✓ | **`Scan` · `Face` · `Manual`** |
| KitClaimedAt | datetime2 | ✓ | |
| KitClaimedBy | nvarchar(200) | ✓ | Name of who collected it |
| MealsRedeemedAt | datetime2 | ✓ | Sub-event events: whole stub set redeemed |
| MealsRedeemedBy | nvarchar(200) | ✓ | |
| MealsRedeemedMethod | nvarchar(10) | ✓ | **`Scan` · `Manual`** |
| PassEmailSentAt | datetime2 | ✓ | |
| FormResponses | max | ✓ | JSON object keyed by form field key |
| FaceDescriptor | max | ✓ | Guests only |
| GuestPhoto | max | ✓ | Guests only, data URL |

Derived registration state (not a column): **Pending** (ConfirmedAt NULL) → **Confirmed** → **Checked in**; **Removed** when DeletedAt is set.

### Meals (meal stub definitions)

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | int | | PK |
| EventId | int | | FK → Events (cascade) |
| SessionId | int | ✓ | FK → Sessions (no action); NULL = event-level per-day stub |
| Day | int | | 1-based day; used when SessionId is NULL |
| Name | nvarchar(100) | | e.g. Lunch |
| Denomination | decimal(10,2) | | Face value |
| Quantity | int | | Copies issued per registrant |
| Color | nvarchar(20) | ✓ | Hex or color name |
| ValidFrom / ValidTo | datetime2 | | Inclusive validity |
| SortOrder | int | | |

### MealStubs (issued stubs)

| Column | Type | Null | Notes / enumerated values |
|---|---|---|---|
| Id | int | | PK |
| RegistrationId | int | | FK → Registrations (cascade) |
| MealId | int | | Source definition (reference only, no FK) |
| ControlNumber | uniqueidentifier | | **Unique**; QR payload |
| Day | int | | |
| Name | nvarchar(100) | | Snapshot |
| Denomination | decimal(10,2) | | Snapshot |
| Color | nvarchar(20) | ✓ | Snapshot |
| ValidFrom / ValidTo | datetime2 | | Snapshot |
| CopyIndex | int | | 1..Quantity |
| RedeemedAt | datetime2 | ✓ | NULL = not redeemed (redeemable once) |
| RedeemedBy | nvarchar(200) | ✓ | Display name |
| RedeemedByUserId | int | ✓ | Merchant/admin who scanned |
| RedeemedMethod | nvarchar(10) | ✓ | **`Scan` · `Manual`** |

### MealRedemptions

| Column | Type | Null | Notes / enumerated values |
|---|---|---|---|
| Id | int | | PK |
| RegistrationId | int | | FK → Registrations (cascade). Unique with Day |
| Day | int | | |
| RedeemedAt | datetime2 | | |
| RedeemedBy | nvarchar(200) | ✓ | |
| Method | nvarchar(10) | ✓ | **`Scan` · `Manual`** |

### Invitations

| Column | Type | Null | Notes / enumerated values |
|---|---|---|---|
| Id | int | | PK |
| EventId | int | | FK → Events (cascade) |
| Email | nvarchar(256) | | Indexed with EventId |
| Token | uniqueidentifier | | **Unique**; magic link `?invite={token}` |
| SentAt | datetime2 | | DB default `GETUTCDATE()` |
| Status | nvarchar(20) | | **`Sent` (default) · `Queued` (saved to outbox) · `Registered`** |

### InvitationEmails

| Column | Type | Null | Notes / enumerated values |
|---|---|---|---|
| Id | int | | PK |
| EventId | int | | FK → Events (cascade) |
| ToJson / CcJson | max | | JSON arrays of addresses |
| Subject | nvarchar(200) | | |
| BodyHtml | max | | |
| Status | nvarchar(20) | | **`Scheduled` (default) · `Sending` · `Sent` · `Queued` · `Failed` · `Cancelled`**. Editable only when Scheduled, Queued or Failed |
| CreatedAt / UpdatedAt | datetimeoffset | | |
| ScheduledAt | datetimeoffset | ✓ | |
| SentAt | datetimeoffset | ✓ | |
| LastAttemptAt | datetimeoffset | ✓ | |
| NextAttemptAt | datetimeoffset | | Scheduler pick-up time |
| Attempts | int | | |
| LastError | nvarchar(1000) | ✓ | |
| Version | rowversion | | Optimistic concurrency |

### MailPlans

| Column | Type | Null | Notes / enumerated values |
|---|---|---|---|
| Id | int | | PK |
| Kind | nvarchar(20) | | **`Reminder` · `Announcement` (default) · `Follow-up`** |
| EventId | int | ✓ | FK → Events (SET NULL on delete) |
| EventName | nvarchar(200) | ✓ | Typed event name (kept if event is deleted) |
| Channel | nvarchar(10) | | **`Email` (default) · `Viber` · `Both`** |
| Draft | bit | | Drafts are never sent |
| GroupIdsJson | max | | JSON array of audience group ids: **`all` · `admins` · `org:{organization}` · `level:{level}` · `event:{eventId}`** |
| EmailsJson | max | | JSON array of typed addresses |
| CcJson | max | | JSON array |
| CreatedByUserId | int | | |
| CreatedAt / UpdatedAt | datetimeoffset | | |
| Version | rowversion | | |

### MailPlanSends

| Column | Type | Null | Notes / enumerated values |
|---|---|---|---|
| Id | int | | PK |
| PlanId | int | | FK → MailPlans (cascade) |
| SendAt | datetimeoffset | | Target day/time |
| Subject | nvarchar(200) | | |
| BodyHtml | max | | Sanitized markup, inline images as data URLs |
| PreviewText | nvarchar(500) | | |
| ImageCount | int | | |
| Status | nvarchar(20) | | **`Scheduled` (default) · `Sending` · `Sent` · `Queued` · `Failed`** |
| NextAttemptAt | datetimeoffset | | |
| LastAttemptAt | datetimeoffset | ✓ | |
| Attempts | int | | |
| DispatchedAt | datetimeoffset | ✓ | Receivers frozen at first attempt |
| SentAt | datetimeoffset | ✓ | |
| RecipientCount | int | | |
| LastError | nvarchar(1000) | ✓ | |
| UpdatedAt | datetimeoffset | | |
| Version | rowversion | | |

### MailPlanDeliveries

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | int | | PK |
| SendId | int | | FK → MailPlanSends (cascade). Unique with Email |
| Email | nvarchar(256) | | Indexed (My Reminders lookup) |
| UserId | int | ✓ | |
| Phone | nvarchar(40) | ✓ | |
| IsCc | bit | | CC receivers get email only, never Viber |
| EmailSentAt / ViberSentAt | datetimeoffset | ✓ | Per-channel progress (no double send on retry) |
| RemindAt | datetimeoffset | ✓ | Receiver's own reminder |
| Snoozed | bit | | |
| ReminderSentAt | datetimeoffset | ✓ | |
| DoneAt | datetimeoffset | ✓ | |

### MailPlanEventNames

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | int | | PK |
| Name | nvarchar(200) | | **Unique** |
| CreatedAt | datetimeoffset | | |

### MailPlanSavedCcs

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | int | | PK |
| Email | nvarchar(256) | | **Unique** |
| CreatedAt | datetimeoffset | | |

### Feedbacks

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | int | | PK |
| EventId | int | | Unique with UserId (one per user per event; no FK) |
| UserId | int | | No FK |
| Rating | int | | 1–5 |
| Comment | max | ✓ | ≤ 1000 chars (validation rule) |
| CreatedAt | datetime2 | | DB default `GETUTCDATE()` |

### AppSettings

| Column | Type | Null | Notes / enumerated values |
|---|---|---|---|
| Key | nvarchar(100) | | PK. Known keys: **`Email:Cc`** (comma-separated CC list) · **`Notifications:MobileProvider`** |
| Value | max | ✓ | For `Notifications:MobileProvider`: **`Viber` (default) · `Sms`** |

---

## Enumerations summary

| Enumeration (C# class) | Values | Used in |
|---|---|---|
| `Roles` | Admin, Participant, TL, SuperAdmin, Merchant | Users.Role |
| `NotificationChannels` | Email, Viber, Both | Users.NotificationChannel, Registrations.GuestNotificationChannel |
| `EventStatus` | Draft, Published, Closed | Events.Status |
| `EventFormats` | InPerson, Online, Hybrid | Events.Format |
| `CheckInMethods` | Scan, Face, Manual | Registrations.CheckInMethod, *RedeemedMethod / Method (Scan, Manual) |
| `InvitationStatus` | Sent, Queued, Registered | Invitations.Status |
| `InvitationEmailStatus` | Scheduled, Sending, Sent, Queued, Failed, Cancelled | InvitationEmails.Status |
| `MailPlanKinds` | Reminder, Announcement, Follow-up | MailPlans.Kind |
| `MailPlanChannels` | Email, Viber, Both | MailPlans.Channel |
| `MailSendStatus` | Scheduled, Sending, Sent, Queued, Failed | MailPlanSends.Status |
| `MobileProviders` | Viber, Sms | AppSettings `Notifications:MobileProvider` |
| `SettingKeys` | Email:Cc, Notifications:MobileProvider | AppSettings.Key |
| `FormFieldDef.Type` | text, textarea, number, date, time, select, multiselect, radio, checkbox, image | Events.FormSchema (JSON) |
| `MailPlanAudience` group ids | all, admins, org:\*, level:\*, event:\* | MailPlans.GroupIdsJson (JSON) |
