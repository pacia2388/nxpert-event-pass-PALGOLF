# CI checks to run locally

Run these before you push. They are the same checks `ci.yml` runs, in the same order, so a clean
local run means a green CI run. Run each block from the repo root (`C:\wt\nxpert-PALGOLF`).

## Prerequisites

- .NET SDK 10, Node 22 (CI version; 20+ works), Python 3 for the face engine.
- SQL Server LocalDB running: `sqllocaldb info` lists `MSSQLLocalDB`. If not:
  `sqllocaldb create MSSQLLocalDB -s`. The API tests and e2e tests create their own throwaway
  database on it; your dev database is never touched.
- `%TEMP%` must be writable (the outbox tests write there).

## 1. API (job `api`)

```powershell
cd backend
dotnet format whitespace EventRegistration.Api.slnx --verify-no-changes
dotnet test --project EventRegistration.Api.Tests
```

- Formatting fails? Fix with `dotnet format whitespace EventRegistration.Api.slnx`.
- Run `dotnet test` from `backend/`: its `global.json` selects Microsoft.Testing.Platform (the
  VSTest path fails on the .NET 10 SDK). If `dotnet test` cannot start the runner, run the test
  exe directly: `.\EventRegistration.Api.Tests\bin\Debug\net10.0\EventRegistration.Api.Tests.exe`.

## 2. Web (job `web`)

```powershell
cd frontend/web
npm ci
npx ng lint
npx prettier --check --end-of-line auto src e2e
npx ng test --watch=false
npx ng build
```

- Formatting fails? Fix with `npm run format`.
- `--end-of-line auto`: on Windows git checks files out with CRLF, which plain
  `npm run format:check` reports on every file. CI (Linux, LF) runs `npm run format:check`; the
  flag gives the same result locally.

## 3. End-to-end (job `e2e`)

```powershell
cd frontend/web
npx playwright install chromium
dotnet build ../../backend/EventRegistration.Api
npx playwright test
```

- Playwright starts the API and the web app itself and drops its database afterwards; stop your
  own dev servers on ports 5088 / 4200 first.
- On failure: `npm run e2e:report` opens the report.

## 4. Face engine (not in CI — local only)

```powershell
cd facerog-engine
pip install -r requirements-dev.txt
ruff check .
pytest
```

The tests use a fake detector and embedder, so no model weights are needed.

## Running CI by hand (manual dispatch)

CI runs automatically on pull requests into `main` and pushes to `main`. The `workflow_dispatch`
trigger in `ci.yml` lets you run it on any branch. It only works once that trigger is on `main`;
until then GitHub answers that the workflow has no `workflow_dispatch` trigger.

**From GitHub:** Actions → **CI** → **Run workflow** → pick the branch → **Run workflow**.

**From a terminal** (any shell opened in the repo folder; needs the GitHub CLI `gh`, signed in):

```powershell
# once per clone, if gh asks for a default repository
gh repo set-default pacia2388/nxpert-event-pass-PALGOLF

# start CI on a branch; branch names are case-sensitive (DEV_1783, not dev_1783)
gh workflow run ci.yml --ref DEV_1783

# follow it, then list recent runs
gh run watch
gh run list --workflow ci.yml --limit 5
```

- `HTTP 422: No ref found for: …` — the branch name is wrong or not pushed. List remote branches
  with `git ls-remote --heads origin`.
- A failed run's Playwright report is attached to the run as the `playwright-report` artifact:
  `gh run download <run-id> -n playwright-report`.
