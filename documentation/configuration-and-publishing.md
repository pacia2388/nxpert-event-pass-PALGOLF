# Configuration and publishing

How to change each configuration file of an installation, and how to publish a new version to an
environment (test, staging, production) without losing that installation's settings.

Full first-time server setup (IIS, certificates, SQL Server, Face Engine) is in
[deployment.md](deployment.md). This guide uses the same example folders:

| Site | Folder (example) | Public URL (example) |
|------|------------------|----------------------|
| API | `C:\inetpub\wwwroot\PalGolf_API` | `https://SERVER:8000` |
| App | `C:\inetpub\wwwroot\PalGolf_App` | `https://SERVER` |

## 1. The configuration files

Each installation keeps its own copy of these files on the server. Repo copies are for local
development only.

| File | Site | What it sets | In a new build? | After an edit |
|------|------|--------------|-----------------|---------------|
| `appsettings.json` | API | Defaults (dev values). **Do not edit on the server** | Yes (overwritten) | — |
| `appsettings.Production.json` | API | Real values: database, JWT key, email, Viber/SMS, URLs | No (server only, never committed) | Recycle the `PalGolf_API` app pool |
| `categories.json` | API | Event / team categories (or none) | **No** — left out of `dotnet publish` | Applies on the next request; no restart |
| `appSettings.json` | App | API URL, face engine URL, app name, footer | Yes (overwritten: restore it) | Reload the page |
| `menu.json` | App | Menus switched off for everyone | **No** — left out of `npm run build` | Reload the page |
| `web.config` | App | Face engine proxy port, SPA routes | Yes (overwritten: restore it) | None (IIS reads it on change) |

Keep a backup of every file in this table for each environment (outside the site folders), so a
server rebuild or a wrong copy can be undone.

## 2. Updating each file

Edit the files with a text editor on the server, as an administrator. They are JSON: no trailing
commas; text in double quotes. Check a file with any JSON validator before saving if in doubt.

### 2.1 API `appsettings.Production.json`

Holds every secret and per-environment value. Full list of keys: [deployment.md §1.2](deployment.md#12-production-config).

1. Edit `C:\inetpub\wwwroot\PalGolf_API\appsettings.Production.json`.
2. Recycle the app pool so every setting is read again:

   ```cmd
   %windir%\system32\inetsrv\appcmd recycle apppool /apppool.name:PalGolf_API
   ```

3. Open `https://SERVER:8000/` → `{"service":"EventRegistration.Api","status":"ok"}`.

The site must run with `ASPNETCORE_ENVIRONMENT=Production`, or this file is ignored.

### 2.2 API `categories.json`

Who events are for and what teams are. Events and teams share the list: a signed-in player is
warned before registering for an event whose category differs from their team's.

```json
{
  "Categories": {
    "Items": [
      { "Name": "Senior", "Icon": "elderly", "TeamMaxMembers": 8 },
      { "Name": "Regular", "Icon": "person", "TeamMaxMembers": 10 }
    ],
    "DefaultTeamMaxMembers": 10
  }
}
```

| Field | Rule |
|-------|------|
| `Name` | Required, 1–20 characters, unique (ignoring case). Shown as typed |
| `Icon` | Optional. A [Material icon](https://fonts.google.com/icons) name for the event card pill; blank = `label` |
| `TeamMaxMembers` | Optional, at least 1: most members a team in this category holds (the Captain is not counted). Blank = `DefaultTeamMaxMembers` |
| `DefaultTeamMaxMembers` | Optional, at least 1 (default 10): cap for a team with no category, or a category no longer listed |

- **No categories:** set `"Items": []` (or delete the file). The Category choice disappears from
  the event and team forms, the pills disappear, and nobody is warned.
- **Edits apply on the next request** — no restart. Users see the new list after a page reload.
- **Removing or renaming a category** does not change saved data: events and teams keep the old
  name (shown with the default icon, teams with the default cap) until an admin picks another.
- **A bad file** (blank or duplicate name, name over 20 characters, a cap below 1) stops the API
  from starting, and an edit that breaks a running API makes event and team requests fail. The API log names
  the problem, e.g. `categories.json: Category "Senior" is listed more than once.` Fix the file;
  no restart is needed once it is valid.
- **New installation:** the build does not include this file. Copy
  `backend/EventRegistration.Api/categories.json` from the repo next to `EventRegistration.Api.dll`
  and edit it. Until then the installation has no categories.

### 2.3 App `appSettings.json`

Read by the browser when the app starts. It is public (served to every visitor): never put
secrets in it.

```json
{
  "apiBaseUrl": "https://SERVER:8000",
  "faceEngineBaseUrl": "/face-api",
  "appName": "PAL GOLF",
  "footerText": "Powered by N-PAX - NXPERT SYSTEM"
}
```

| Key | Rule |
|-----|------|
| `apiBaseUrl` | Required. The API's public URL, **with its port**. Must match the API site binding |
| `faceEngineBaseUrl` | Required. Keep `/face-api` (the App's `web.config` proxies it) |
| `appName` | Optional. Tab title, header, install banner, email preview. Blank = `PAL GOLF` |
| `footerText` | Optional. Footer line. Blank = built-in text |

Edit `C:\inetpub\wwwroot\PalGolf_App\appSettings.json`, then reload the page. If it is missing or
`apiBaseUrl` is not text, the app shows "App failed to start".

### 2.3a API `user-fields.json`

Turns the optional **Department** and **Job role** profile fields on or off for this installation.

```json
{ "UserFields": { "Department": { "Enabled": false }, "JobRole": { "Enabled": false } } }
```

- Both are **off by default**. The API ignores a disabled field on input and leaves it out of its
  answers, so turning it off never overwrites or clears stored values.
- Read at runtime with `reloadOnChange`: an edit applies without a restart.
- The build does not include this file. Copy
  `backend/EventRegistration.Api/user-fields.json` to the API site root on a new install and edit
  it there; never delete it on deploy.

### 2.4 App `menu.json`

Switches menus off for everyone on this installation. Ids, defaults and rules:
[menu-config.md](menu-config.md).

```json
{ "menus": { "dashboard": true, "voucher": false } }
```

- A menu left out of the file is on. A missing or broken file leaves every menu on (the browser
  console says why); it never locks anyone out.
- **New installation:** the build does not include this file. Copy
  `frontend/web/public/menu.json` from the repo to `C:\inetpub\wwwroot\PalGolf_App` and edit it.

### 2.5 App `web.config`

Only the face engine proxy port changes per server (rule `FastAPI_Proxy`, see
[deployment.md §2.4](deployment.md#24-point-the-proxy-at-port-8001)). Keep the other rules as
built. The file also tells IIS and the browser not to cache `index.html`, `ngsw.json`,
`ngsw-worker.js`, `appSettings.json`, `menu.json` and the theme files, so an update is picked up
on the next load.

### 2.6 API `Mail:InlineImages` and `App:PasswordSetupLinkHours`

Inline images in composed emails and mail-plan messages have four limits; the message editors in
the App load the same numbers (`GET api/settings/mail-limits`) for their checks and help text.

```json
{
  "Mail": {
    "InlineImages": {
      "MaxSourceBytes": 10485760,
      "MaxImageBytes": 2097152,
      "MaxTotalBytes": 5242880,
      "MaxCount": 5
    }
  },
  "App": {
    "PasswordSetupLinkHours": 72
  }
}
```

- The whole email (body, inline images, attachments, base64 overhead) must stay under **25 MB**.
  Base64 adds about 33%, which is why the request cap is worked out from `MaxTotalBytes`, not set
  by hand.
- `PasswordSetupLinkHours` is how long a one-time set-password link stays valid (default 72).
- `App:MailOutgoingDir` (default `mail-outgoing/` in the API folder) holds a composed invitation or
  mail-plan email, with its attachments, until it is delivered. It contains personal data: never
  serve it, never delete it on deploy, and keep it in the backup plan.
- Changing these needs an API restart (they are read at startup).

## 3. Publishing to an environment

`dotnet publish`, app pool recycles and `iisreset` affect the live site: agree a window first.
Run the build steps on a development machine, from a clean checkout of the version to release.

### 3.1 Build

API, from `backend/EventRegistration.Api`:

```cmd
dotnet publish -c Release -o C:\deploy\PalGolf_API
```

App, from `frontend/web`:

```cmd
npm ci
```

```cmd
npm run build
```

The API output is `C:\deploy\PalGolf_API`; the App output is `frontend/web/dist/web/browser`.
Neither contains `categories.json`, `menu.json` or `appsettings.Production.json`, so copying them
over an installation never replaces those files.

### 3.2 First install

1. Set up the server and both IIS sites: [deployment.md](deployment.md) §0–§2.
2. API: copy `C:\deploy\PalGolf_API` to the API folder, add `appsettings.Production.json` (§2.1)
   and `categories.json` (§2.2).
3. App: copy the **contents** of `dist/web/browser` to the App folder, edit `appSettings.json`
   (§2.3) and `web.config` (§2.5), add `menu.json` (§2.4).
4. Start the API site. It creates or migrates the database and seeds it on first start.
5. Check (§3.4).

### 3.3 Update an existing installation

1. **Back up** the API's `appsettings.Production.json` and `categories.json`, and the App's
   `appSettings.json`, `menu.json` and `web.config`.
2. **API:** stop the app pool, copy the new `C:\deploy\PalGolf_API` files over the API folder (do
   not delete the folder first), start the app pool:

   ```cmd
   %windir%\system32\inetsrv\appcmd stop apppool /apppool.name:PalGolf_API
   ```

   ```cmd
   robocopy C:\deploy\PalGolf_API C:\inetpub\wwwroot\PalGolf_API /E
   ```

   ```cmd
   %windir%\system32\inetsrv\appcmd start apppool /apppool.name:PalGolf_API
   ```

   Database changes in the new version are applied when the API starts
   (`Database:AutoMigrate`, on by default).
3. **App:** copy the new `dist/web/browser` contents over the App folder (do not delete the folder
   first), then put back the backed-up `appSettings.json` and `web.config` — the build overwrote
   them with repo values. `menu.json` is not in the build and stays as it was.

   ```cmd
   robocopy <repo>\frontend\web\dist\web\browser C:\inetpub\wwwroot\PalGolf_App /E
   ```

4. Check (§3.4).

`robocopy ... /E` copies and overwrites files but never deletes extra ones. Do not use `/MIR`: it
deletes `categories.json`, `menu.json` and `appsettings.Production.json`.

### 3.4 Check after publishing

- `https://SERVER:8000/` → `{"service":"EventRegistration.Api","status":"ok"}`.
- `https://SERVER:8000/api/settings/categories` → this installation's category list (`[]` when none).
- `https://SERVER/` → sign-in page; a deep link (e.g. `/home`) refreshes without a 404.
- Sign in as an admin: Home shows the expected cards (menu.json), the event form shows the
  expected categories, and the footer and app name are right (appSettings.json).
- If the API does not start, enable `stdoutLogEnabled="true"` in the API site's `web.config` and
  read `logs\` (a bad `categories.json` is reported there).

### 3.5 Several environments

Each environment (test, staging, production) is its own pair of IIS sites with its own copies of
the files in §1. The build is the same for all of them; only those files differ. Keep each
environment's copies in its backup, labelled with the environment name, and never copy one
environment's `appsettings.Production.json` to another.
