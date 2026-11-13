# End-to-end tests (Playwright)

The suite in `frontend/web/e2e/` drives the Angular app in Chromium against the real API.

## Run it

Prerequisites: .NET SDK 10, Node 20+, SQL Server LocalDB (`sqllocaldb info` lists
`MSSQLLocalDB`) and `sqlcmd` (used to drop the test database).

```bash
cd frontend/web
npx playwright install chromium
```

```bash
npm run e2e
```

| Script | What it does |
|---|---|
| `npm run e2e` | Runs the whole suite headless |
| `npm run e2e:ui` | Opens Playwright's UI mode to run and watch single tests |
| `npm run e2e:report` | Opens the HTML report of the last run |

Run one file or one project with `npx playwright test anonymous/login` or
`npx playwright test --project=admin`.

## What it starts

`playwright.config.ts` starts both servers itself:

| Server | Port | Notes |
|---|---|---|
| API | 5188 | `dotnet run`, environment `E2E`, database `EventPassE2E_<run id>` |
| Web | 4300 | `ng serve`; never reused, so free port 4300 first (the `web-alt` preview uses it) |

- Your dev servers (5088 / 4200) and dev database are not touched, so both can run at once.
- The API seeds a fresh database on start (`admin@demo.com`, `alice@example.com` …), and the
  global teardown drops it when the run ends.
- Email, Viber and SMS are off. Messages are written to `backend/EventRegistration.Api/outbox/`,
  which the tests read to check whether a pass was sent.
- The environment is `E2E`, not Development, so `appsettings.Development.Local.json` is not loaded.
- The browser is pointed at port 5188 by rewriting `appSettings.json` in each test
  (`e2e/fixtures.ts`), so no rebuild is needed.
- Set `EVENTPASS_E2E_SQLSERVER` to use a SQL Server other than LocalDB (Windows authentication).

## Layout

| Path | Contents |
|---|---|
| `e2e/env.ts` | Ports, database name, seeded accounts |
| `e2e/fixtures.ts` | `test` with the API rewrite plus `adminApi` and `publicApi` request contexts |
| `e2e/auth.setup.ts` | Signs in once as admin and as participant; saves the sessions to `e2e/.auth/` |
| `e2e/helpers/` | Test data (`createPublishedEvent`, `guestRegister`), outbox reader, UI sign-in |
| `e2e/anonymous/` | Signed-out specs |
| `e2e/admin/` | Specs run as the seeded admin |
| `e2e/participant/` | Specs run as the seeded participant |

The folder decides the project, and so the signed-in user.

## Writing a spec

- Import `test` and `expect` from `../fixtures`, not from `@playwright/test`.
- Create the data the test needs through `adminApi` (for example `createPublishedEvent`), with
  unique names, so tests stay independent and can run in parallel. Don't rely on another test's
  data or on the seeded sample event.
- Find elements by role and label (`getByRole`, `getByLabel`), as a user would. If an element has
  no accessible name, fix the template (it is an accessibility bug) rather than adding a
  `data-testid`.
- Mat icons are hidden from the accessibility tree, so a button "<icon> Confirm" is named
  `Confirm`.
- Text matchers still see icon text: a pill rendering `<mat-icon>elderly</mat-icon> Senior` has
  text `elderlySenior`, so `getByText('Senior', { exact: true })` misses it. Match with a regex or
  `toContainText`.
- A `mat-button-toggle-group` is a `radiogroup` and each toggle a `radio`:
  `getByRole('radiogroup', { name: 'Event category' }).getByRole('radio', { name: 'Senior', exact: true })`.
- Before trusting a new spec, break its key assertion once and check that it fails.

## CI

The `e2e` job in `.github/workflows/ci.yml` runs on `windows-latest` (LocalDB needs Windows). On
failure it uploads `playwright-report` and `test-results` (traces of retried tests) as an
artifact.
