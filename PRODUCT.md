# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

- **Primary: the organizer at a desk.** An admin on a laptop who sets up events and sub-events,
  sends invitations, confirms registrations, manages users and teams, and exports attendance
  before and after the event. When design trade-offs conflict, this job wins.
- **Event-day staff.** Admins and participants with *Can confirm registrations* scanning QR
  passes and faces on phones at the venue, sometimes offline. Merchants scan meal stubs only.
- **Attendees.** Participants and guests on phones, usually arriving from an emailed link, who
  register, open their pass and check My Events.
- **Team Leads (TL).** See only the Team page (add Participants to their team) and the prototype
  Voucher page.

## Product Purpose

PAL GOLF is N-PAX's event registration and check-in app (NXPERT SYSTEM), customised here for
Philippine Airlines' 78th PAL Interclub Golf Tournament 2027 (Seniors Division Feb 17–24,
Men's/Regular Division Feb 25–Mar 4, Cagayan de Oro | Bukidnon).
An organizer creates events and invites people; registrants are confirmed, receive a QR pass
(plus meal stubs, and a meeting link for online/hybrid events), and are checked in on the day;
the organizer exports the attendance sheet. Success is an organizer running the whole event,
from invitation to attendance report, without a spreadsheet on the side.

## Positioning

Undecided. No differentiating claim against other event tools has been confirmed.

## Operating Context

- Before the event: desk work in the admin area (events, custom registration forms, invitations
  and the mail planner, users and CSV import, teams, settings).
- Event day: phones at the venue. The scanner needs HTTPS and a camera; check-ins queue offline
  and sync later; meal-stub scanning needs internet. A face check-in TV screen exists.
- Attendees mostly enter through an emailed or Viber link to `/e/{slug}`.
- Deployed as two IIS sites (Angular app and .NET API); server kept on Philippine time.
- Installable PWA, portrait orientation.

## Capabilities and Constraints

- Stack: Angular 21 + Angular Material, .NET 10 Web API, SQL Server, MailKit, QRCoder, and a
  separate face recognition engine.
- Roles are hard-coded: SuperAdmin, Admin, Participant, TL, Merchant, plus the single
  *Can confirm registrations* permission. See `documentation/roles.md`.
- Registration flow rules are fixed: registrations start Pending, no pass until confirmed, only
  confirmed passes check in, each sub-event is its own registration and pass, online events get
  no QR pass, the meeting link is shown to confirmed registrants only.
- Destructive actions need a confirmation dialog with the safe option pre-selected.
- The server is authoritative for validation; the client is UX only.
- Terminology in use: event, sub-event, pass, meal stub, kit, Attendance, My Events, Browse
  Events, team, Team Lead, voucher.
- Undecided: whether the Voucher page stays past prototype.

## Brand Commitments

- **Name:** PAL GOLF. The name and the footer line ("Powered by N-PAX - NXPERT SYSTEM") are the
  `appName` and `footerText` settings in `frontend/web/public/appSettings.json`, read at runtime.
  Emails and calendar invites take the name from `App:Name` in the API's appsettings; keep it
  the same as `appName`. The installed-app name in `manifest.webmanifest` and the email sender
  name (`Email:FromName`) are set separately.
- **Tournament poster:** `frontend/web/public/brand/pal-interclub-2027-poster.jpg`, supplied by
  the client. The login page takes its palette (navy, PAL blue, red-to-gold arc) from it.
- **Not binding yet:** the N-PAX logo (`frontend/web/public/nxpax-logo.png`), the teal theme
  colour and the Roboto/Material look on every page other than login are placeholders; the
  app-wide recolour to the poster's palette is planned in `plans/pal-golf-recolour.md`.

## Evidence on Hand

- Guides and references: `documentation/` (roles, events, meal stubs, database tables, admin
  guide PDF) and `plans/` (team page, invite teams).
- Seeded demo data only (Development): a demo admin, five participants, one event.
- The client's tournament poster is the only client brand asset in the repo. No separate logo
  files, testimonials, usage numbers or pricing exist. Future work must not invent them.

## Product Principles

1. The organizer's desk workflow comes first; event-day and attendee screens must not cost it
   clarity.
2. State is always legible: Pending, Confirmed, Checked in and Cancelled are never ambiguous.
3. Event day cannot stall: scanning works on a phone, in a hurry, with a bad connection.
4. Attendees get in with the least possible effort: a link, a name, a pass.
5. Nothing destructive happens by accident.
