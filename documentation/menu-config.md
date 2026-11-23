# Menu config (`menu.json`)

Switch whole menus off for **everyone** on one installation, without a rebuild. Roles still decide
who sees a menu that is on; this file only removes menus.

## Where

`menu.json` next to `index.html` in the App site (source `frontend/web/public/menu.json`). The app
reads it when it starts (`no-cache`), so a page reload picks up an edit. It is served to every
browser, so put nothing private in it.

The production build (`npm run build`) **leaves `menu.json` out**, so a deploy never overwrites the
server's copy. On a new installation, copy `frontend/web/public/menu.json` to the App site once and
edit it there. Until then every menu is on. `ng serve` (development) still serves the repo copy.
See [configuration-and-publishing.md](configuration-and-publishing.md) for the full steps.

```json
{
  "menus": {
    "dashboard": true,
    "calendar": true,
    "mailPlanner": true,
    "mealScanner": true,
    "users": true,
    "teams": true,
    "myTeam": true,
    "voucher": false
  }
}
```

## Menus that can be switched off

| Id | Home card | Pages |
|----|-----------|-------|
| `dashboard` | Dashboard | `/admin/dashboard` |
| `calendar` | Calendar | `/calendar` |
| `mailPlanner` | Mail Planner | `/mail-planner` |
| `mealScanner` | Meal Scanner | `/merchant/scan` |
| `users` | Users | `/admin/users` |
| `teams` | Teams | `/admin/teams` |
| `myTeam` | My Team | `/team` |
| `voucher` | Voucher | `/voucher` |

**Always on** (not in the file): Home, sign-in pages, public event and invite links, Events
(list, create, edit, detail), the check-in scanner, Confirm Registrations, Browse Events, My Events
(users land there after registering), My Account and Settings.

## Rules

- `false` hides the Home card and blocks the page: a signed-in user who opens its link goes to
  their home page; a signed-out visitor goes to sign-in.
- A menu left out of the file is **on**.
- Missing file, invalid JSON, `menus` not an object, or a value that is not `true`/`false`: every
  menu stays on and the browser console says why. A bad edit never locks anyone out.
- An unknown id (a typo, or an always-on menu) is ignored with a console warning.
- With `mealScanner` off, merchants land on Home, which then says "No modules are available."
- This only hides pages. The API is unchanged: a signed-in user can still call a switched-off
  module's API directly.
