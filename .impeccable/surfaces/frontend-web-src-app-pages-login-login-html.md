---
version: 1
slug: "frontend-web-src-app-pages-login-login-html"
primary_target: "frontend/web/src/app/pages/login/login.html"
related_targets: ["frontend/web/src/app/pages/login/login.scss"]
---

# Login surface

Scope: `/login` only. Mode: Operate. Audience: organizers at a desk and attendees on phones who
arrive to sign in. Task: sign in, or go create a participant account. Content: the tournament
poster supplied by the client (`frontend/web/public/brand/pal-interclub-2027-poster.jpg`), the
sign-in form, the register link. Constraint: the rest of the app keeps its teal look for now
(recolour tracked as a separate issue); form behaviour is unchanged.

## Direction contract

THESIS: The client's tournament poster is the whole brand statement, shown complete and uncropped
beside a plain, fast sign-in form. Refuses the gradient panel with a headline and three feature
bullets.

OWN-WORLD: Navy ground sampled from the poster's lower band (#051f63 rising to #1d5fae). White
card. PAL blue #0f5fb8 for the action and focus. The mark's red-to-gold appears once, as the
signing-in progress stroke. Montserrat heavy for the heading ("Sign in"); Roboto for fields. Pill buttons, 12px fields.

STORY: The visitor recognises the tournament, reads dates and venue, then signs in or creates a
participant account.

FIRST VIEWPORT: Desktop: one centred frame; left, the square poster (up to 600px); right, a white
card of equal height with "Sign in", two fields, the Sign in pill, the register link.
Phone: poster cropped to its top band (logo, title, golfer), fading into navy, dates and venue as
live text on navy, then the card. Tall tablets: stacked, whole poster.

FORM: Split sign-in, the category standard. seed: none (pinned) "redesign the login page
according to this", "The website can actually look like this".

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance
