---
name: deploy-iis
description: Publish the Event Registration app to IIS as two independent sites (Angular static app + .NET API) on separate ports — prerequisites, dotnet publish, production appsettings, app pool/site/bindings, apiBaseUrl, CORS, HTTPS, and update procedure. Use when asked to deploy, publish, host on IIS, or update a deployed instance.
---

# Deploy to IIS (API and App on separate ports)

Two independent IIS sites on the same server, each on its own port. Example ports (change to
suit; replace `SERVER` with hostname/IP):

| Site         | Serves               | HTTP | HTTPS | Example URL            |
|--------------|----------------------|------|-------|------------------------|
| **App** (FE) | Angular static files | 8080 | 8443  | `https://SERVER:8443`  |
| **API** (BE) | .NET Web API         | 5000 | 5001  | `https://SERVER:5001`  |

Different ports = different origins → the browser applies CORS. Rules for this deployment are in
`.claude/rules/deployment.md`. The full original guide is in [reference.md](reference.md).

> **HTTPS matters:** the QR scanner (`getUserMedia`) only works over HTTPS or `localhost`. Give
> both sites an HTTPS binding (avoids mixed-content calls from the SPA).

## 0. One-time server prerequisites

1. **IIS** with *Static Content*, *Default Document*, and (recommended) *WebSocket Protocol*.
2. **URL Rewrite module** (required for Angular deep links).
3. **.NET 10 ASP.NET Core Hosting Bundle** (runtime + ANCM). Run `iisreset` after installing.
4. **SQL Server** reachable from the server (Express is fine). **LocalDB does not work under IIS.**
5. Open the chosen ports (8080/8443/5000/5001) in **Windows Firewall** inbound.

## 1. Backend (.NET Web API)

From `backend/EventRegistration.Api`:

```cmd
dotnet publish -c Release -o C:\inetpub\EventReg\api
```

Create `appsettings.Production.json` next to the DLL (real values, never dev secrets):

```jsonc
{
  "ConnectionStrings": {
    "Default": "Server=.\\SQLEXPRESS;Database=EventRegistrationDb;Trusted_Connection=True;TrustServerCertificate=True"
  },
  "Jwt": { "Key": "REPLACE-with-a-long-random-secret-at-least-32-chars" },
  "Email": { "Enabled": true, "User": "you@gmail.com", "Password": "gmail-app-password", "FromAddress": "you@gmail.com" },
  "App": {
    "FrontendBaseUrl": "https://SERVER:8443",   // public App URL incl. port — invite links use it
    "RequireEmailVerification": false
  }
}
```

Viber late-failure fallback: add a Sinch Conversation webhook (trigger `MESSAGE_DELIVERY`, target
`https://SERVER:5001/api/webhooks/sinch/conversation`, with a secret) and set the same secret as
`Viber:WebhookSecret` here. Needs the API on public HTTPS. Unset = late failures send no email.
See `documentation/deployment.md` → "Sinch delivery reports".

The app runs EF migrations and seeds on startup (`DbSeeder`); the SQL login must be able to create
the DB (or pre-create an empty `EventRegistrationDb` and grant `db_owner`).

**API site:**

1. App pool `EventRegApi`: **.NET CLR version = No Managed Code**, pipeline *Integrated*. For
   `Trusted_Connection`, give `IIS AppPool\EventRegApi` a SQL login (or run as a service account).
2. Site → `C:\inetpub\EventReg\api`; bindings `http :5000`, `https :5001` (select TLS cert).
3. Set `ASPNETCORE_ENVIRONMENT=Production` (pool env vars or in `web.config` under
   `<aspNetCore><environmentVariables>`).
4. Verify: `https://SERVER:5001/` → `{"service":"EventRegistration.Api","status":"ok"}`.

**Theme:** both sites ship `theme.palgolf.json` and `theme.nxpass.json`. Pick one with `"theme"`
in the App site's `appSettings.json` and `"theme"` in `appsettings.Production.json` (API; recycle
the pool after changing it). Set both to the same file. The web app reads its file from its own
site, never the API. Custom look: a new file name (e.g. `theme.custom.json`) in both sites, which
deploys never touch. See `documentation/theming.md`.

Dev Kestrel ports (5088/7088 in `launchSettings.json`) are ignored under IIS.

## 2. Frontend (Angular)

Settings live in `appSettings.json` (source `frontend/web/public/appSettings.json`), read at app
start — **not** compiled in. After copying the build, edit it on the server:

```json
{ "apiBaseUrl": "https://SERVER:5001", "faceEngineBaseUrl": "/face-api" }
```

From `frontend/web`:

```cmd
npm ci && npm run build
```

Output: `dist/web/browser` (includes `web.config` from `public/`).

**App site:** copy the *contents* of `dist/web/browser` to `C:\inetpub\EventReg\web`; static site
with bindings `http :8080`, `https :8443` (TLS cert). The `web.config` rewrites unknown paths to
`index.html` (needs URL Rewrite) so `/scanner`, `/e/{slug}`, `/verify/{token}` work on refresh.
Browse `https://SERVER:8443/` → sign-in page.

## 3. Cross-cutting checklist

- URLs must reference each other **with ports**: `apiBaseUrl = https://SERVER:5001` (SPA) and
  `App:FrontendBaseUrl = https://SERVER:8443` (API).
- **CORS:** API currently allows any origin. For production tighten in `Program.cs` to exactly the
  App origin and republish:
  ```csharp
  builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
      .WithOrigins("https://SERVER:8443")
      .AllowAnyHeader().AllowAnyMethod()));
  ```
- **Certificate:** must be trusted by the scanning phone (self-signed only if installed as
  trusted). Mixed content (HTTPS app → HTTP API) is blocked — keep both HTTPS.

## 4. Updating later

- **Backend:** re-run `dotnet publish -c Release -o C:\inetpub\EventReg\api` and overwrite. ANCM
  restarts on file change; `iisreset` if a DLL is locked.
- **Frontend:** re-run `npm run build`, replace contents of `C:\inetpub\EventReg\web` (hashed
  filenames avoid stale cache).
- `categories.json` (API folder, event/team categories) is left out of `dotnet publish`, so a
  deploy never overwrites it. Copy it once on a new install; never delete it when copying files.
- Changed `apiBaseUrl`? Edit `appSettings.json` on the server and reload — no rebuild. A new build
  overwrites it with dev values: restore the server copy after each frontend deploy.
- `menu.json` (next to `appSettings.json`, switches menus off) is left out of the production
  build, so a deploy never overwrites it. Copy it once on a new install; never delete it when
  replacing the site files.
- `theme.palgolf.json` / `theme.nxpass.json` are published (both sites); a custom
  `theme.custom.json` is never touched. Custom logo images in the App site are overwritten:
  restore them after each frontend deploy.
