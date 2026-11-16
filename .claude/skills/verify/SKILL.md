---
name: verify
description: Verify a change in the running PAL GOLF app — start the API, web app and face engine, drive the changed screen or endpoint, and report a PASS/FAIL verdict with evidence. Use when asked to verify, confirm a change works, or check a branch, PR or feature in the real app.
---

# Verify a change in the running app

Verification is **runtime observation**: run the app, drive it to where the changed code executes,
and capture what you see. Test runs and typechecks are CI's job; the evidence here is screenshots,
response bodies and server logs.

## 1. Find the change

```bash
git fetch -q origin
git status -sb                      # local main is often behind origin/main: pull --ff-only first
git diff HEAD --stat                # uncommitted
git log --oneline origin/main..     # unpushed commits on this branch
git diff origin/main... --stat      # whole branch vs main
```

State the commit count. No diff and no feature named → say so and ask what to verify. The diff is
ground truth; a PR body or commit message is a claim about it, and a mismatch is a finding.

## 2. Start the servers

Use `preview_start` with these `.claude/launch.json` names. Start only what the change reaches.

| Name | Port | Serves |
|------|------|--------|
| `api` | 5088 | .NET API (Development, LocalDB). Starts with Email, Viber and SMS **off** |
| `web` | 4200 | Angular SPA, talks to `api` |
| `api-alt` / `web-alt` | 5090 / 4400 | Same pair for when another checkout holds 5088 / 4200; `web-alt` talks to `api-alt` (`frontend/web/config/alt/appSettings.json`), so no config edits |
| `face` | 8000 | Face engine dashboard + API; needed only for face check-in / TV scanner changes |

- **Messaging stays off.** The committed `appsettings.json` enables real Gmail and Viber; the `api`
  config overrides that with `--Email:Enabled=false` etc. Confirm the log line
  `Email delivery: outbox (SMTP is off).` before driving any confirm/invite/resend flow. Sent
  messages land in `backend/EventRegistration.Api/outbox/` (`.html` + `.qr.png`) — read them there.
- The API takes ~15 s to build; the NU1902/NU1903 package warnings are pre-existing.
- First start on an empty DB seeds it. Dropping the DB (`dotnet ef database drop --force`) needs
  the user's go-ahead.
- In a worktree, run `scripts\setup-worktree.cmd` first, then use `api-alt` + `web-alt`.

## 3. Drive the surface

For a flow worth keeping, write a Playwright spec in `frontend/web/e2e/<role>/` instead of seeding
the dev DB by hand: it starts its own API (5188), web app (4300) and throwaway DB, signs in per role,
and arranges data through `adminApi` (`e2e/helpers/data.ts`). Run one file with
`npm run e2e --prefix frontend/web -- e2e/<role>/<file>.spec.ts`.

| Change in | Drive it through |
|-----------|------------------|
| `frontend/web/**` | The browser pane at `http://localhost:4200/<route>` (routes: `frontend/web/src/app/app.routes.ts`). Click through as a user would. Read text with `read_page` / `get_page_text`; screenshot at `scale: 0.5` for evidence (a full-scale `zoom` costs ~100K characters). For template or style changes, check at 1440px+ and at 375px (`resize_window`), then reset to `desktop`. |
| `backend/**` controllers/services | The screen that calls it, plus `curl` for edge cases (below). |
| Email/pass/stub templates | Trigger the send in the UI, then open the outbox `.html` in the pane. |
| `facerog-engine/**` | `http://localhost:8000` dashboard, or the TV at `/scanner/<eventId>/tv`. |

**Accounts** (Development seed, `Data/DbSeeder.cs`): admin `admin@demo.com` / `Admin123!`;
seeded participants use `Passw0rd!`. The pane often keeps a signed-in session — open the page
before signing in. Sign out and in again after changing a user's grants (they apply at next sign-in).
Typing into the login form in the pane can fail to submit; sign in with `javascript_tool` on any
app page instead, then navigate:

```js
const r = await fetch('<api>/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email: 'admin@demo.com', password: 'Admin123!' }) });
localStorage.setItem('evt_auth', await r.text());
```

**API calls** (`api-alt`: port 5090):

```bash
TOKEN=$(curl -s -X POST http://localhost:5088/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"admin@demo.com","password":"Admin123!"}' | sed 's/.*"token":"\([^"]*\)".*/\1/')
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5088/api/<route>
```

Then **probe around the change** at the same surface: wrong role, empty or oversized field,
repeat the action, past/future dates, pending vs confirmed registration. Check the API log
(`preview_logs`) and browser console for errors after each step. Business rules to hold are in
`.claude/rules/registration-flow.md` and `.claude/rules/validation.md`.

## 4. Report

```
## Verification: <what changed>
**Verdict:** PASS | FAIL | BLOCKED | SKIP
**Claim:** <what the diff/description says it does; note mismatches>
**Method:** <servers started, account used>
### Steps
1. ✅/❌/⚠️/🔍 <action in the running app> → <what it showed> <evidence>
### Findings
<anything that made you pause, including probes that held>
```

- **PASS** only when you watched the change work in the running app. At least one 🔍 probe.
- **FAIL** when it doesn't, breaks something adjacent, or the claim and diff disagree.
- **BLOCKED** when a server won't come up — say where it stopped.
- **SKIP** for docs-, plans- or tests-only diffs: no runtime surface.

Send key screenshots with `SendUserFile`. Stop the servers you started when done.
