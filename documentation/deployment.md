# Deployment Guide (AWS EC2 Windows Server + IIS)

Fresh install of PAL GOLF on one AWS EC2 Windows Server instance: **Backend** (.NET API),
**Frontend** (Angular), and **Face Engine** (Python FastAPI). Rules this guide follows:
`.claude/rules/deployment.md` and `.claude/rules/security.md`.

## Overview

| Component | Runs as | Folder (example) | Port | Internet-facing? |
|-----------|---------|------------------|------|------------------|
| **Frontend (App)** | IIS site, static files | `C:\inetpub\wwwroot\PalGolf_App` | **443** (HTTPS) | Yes |
| **Backend (API)** | IIS site, ASP.NET Core | `C:\inetpub\wwwroot\PalGolf_API` | **8000** (HTTPS) | Yes |
| **Face Engine** | Windows service (uvicorn) | `C:\inetpub\wwwroot\PalGolf_FaceEngine` | **8001**, `127.0.0.1` only | No — reached through the App at `/face-api` |

Replace `SERVER` with the public DNS name the certificate is issued for (e.g. `palgolf.example.com`).
Because the App is on 443, its URL has no port: `https://SERVER`. The API URL keeps its port:
`https://SERVER:8000`. If you change a port, update every place in the
[Cross-reference checklist](#5-cross-reference-checklist).

```
Internet ──► App  https://SERVER (443) ──/face-api/*──► Face Engine http://127.0.0.1:8001
    │                                                         │
    └──────► API  https://SERVER:8000 ◄──X-Face-Engine-Key────┘
```

> The API on 8000 **must be HTTPS**. The App is HTTPS, and browsers block calls from an HTTPS page
> to an HTTP API (mixed content).

## 0. Server prerequisites (one time)

### 0.1 AWS

1. **Elastic IP** attached to the instance, so the public address survives stop/start.
2. **DNS** record (`A`) for `SERVER` pointing at the Elastic IP.
3. **Security group** inbound rules:

   | Port | Source | Purpose |
   |------|--------|---------|
   | 443 | `0.0.0.0/0` | App |
   | 8000 | `0.0.0.0/0` | API |
   | 80 | `0.0.0.0/0` | Only while issuing/renewing a Let's Encrypt certificate (HTTP-01) |
   | 3389 | your office IP only | RDP admin access |

   Do **not** open 8001 (Face Engine) or 1433 (SQL Server).
4. **Instance size:** the Face Engine runs on CPU and is RAM/CPU heavy; use at least 4 vCPU / 8 GB
   (e.g. `t3.xlarge` or `m6i.xlarge`). For a GPU instance see [3.2](#32-install-server).
5. Snapshot/backup: an EBS snapshot schedule (AWS Backup or Data Lifecycle Manager) covering the
   database and `datasets\faces`.

### 0.2 Windows

1. **IIS** with *Static Content*, *Default Document* and *WebSocket Protocol* (SignalR notifications).
2. **URL Rewrite** module (Angular deep links and the `/face-api` proxy).
3. **Application Request Routing (ARR) 3.0**, then in IIS Manager → server node →
   *Application Request Routing Cache* → *Server Proxy Settings* → tick **Enable proxy** → Apply.
   Without this the `/face-api` rewrite to `127.0.0.1` returns 404.
4. **.NET 10 ASP.NET Core Hosting Bundle**. Run `iisreset` after installing.
5. **SQL Server** (Express or higher) on the instance, or Amazon RDS for SQL Server in the same VPC.
   LocalDB does **not** work under IIS.
6. **Python 3.13** (same version as dev) with the `py` launcher. Check: `py -3.13 --version`.
7. **NSSM** (`nssm.exe`, from nssm.cc) on the `PATH`, to run the Face Engine as a service.
8. **TLS certificate** for `SERVER`, publicly trusted so phones can use the scanner camera.
   Free option: **win-acme** (Let's Encrypt) — it binds the certificate in IIS and renews it
   automatically (needs port 80 open during issue/renewal). Use the same certificate for 443 and 8000.
9. **Windows Firewall:** allow inbound 443 and 8000 (and 80 if used for Let's Encrypt).
   EC2 Windows AMIs keep the Windows Firewall on in addition to the security group.
10. **Time zone:** EC2 Windows defaults to UTC; calendar invites use server time, so set
    Philippine time (UTC+8):

    ```cmd
    tzutil /s "Singapore Standard Time"
    ```

11. Internet access for `pip install` (or see [Offline Python packages](#offline-python-packages)).

## 1. Backend (API)

### 1.1 Build (dev machine)

From `backend/EventRegistration.Api`:

```cmd
dotnet publish -c Release -o C:\deploy\PalGolf_API
```

Copy `C:\deploy\PalGolf_API` to `C:\inetpub\wwwroot\PalGolf_API` on the server.

### 1.2 Production config

Create `appsettings.Production.json` next to `EventRegistration.Api.dll`. Real values only;
never commit this file.

```jsonc
{
  "ConnectionStrings": {
    "Default": "Server=.\\SQLEXPRESS;Database=PalGolfDb;Trusted_Connection=True;TrustServerCertificate=True"
  },
  "Jwt": { "Key": "<long random secret, 32+ chars>" },
  "Email": {
    "Enabled": true,
    "User": "<smtp user>",
    "Password": "<smtp app password>",
    "FromAddress": "<from address>"
  },
  "FaceEngine": { "ApiKey": "<shared face engine key, see 1.3>" },
  // Email when a Viber / SMS message fails (default true). Set false when the provider falls
  // back on its side (e.g. Infobip Viber -> SMS), so registrants don't get the message twice.
  "Viber": { "FallbackToEmail": true },
  "Sms": { "FallbackToEmail": true },
  "App": {
    "FrontendBaseUrl": "https://SERVER",
    "RequireEmailVerification": false
  }
}
```

The published `appsettings.json` still holds dev values (dev JWT key, dev Face Engine key, dev
connection string). Every one of those must be overridden here or by environment variables.

The API creates/migrates the database and seeds on first start. The SQL login must be allowed to
create the database, or pre-create an empty `PalGolfDb` and grant the login `db_owner`.

> AWS SES or another SMTP relay: EC2 throttles outbound port 25; use 587 (the default here).

### 1.3 Generate the Face Engine key

Generate one new random key for this server; it goes in two places (API `FaceEngine:ApiKey` and
Face Engine `FRE_BACKEND_API_KEY`):

```cmd
py -3.13 -c "import secrets; print(secrets.token_urlsafe(32))"
```

Do not reuse the key from the repo; treat it as compromised.

### 1.4 IIS site

1. App pool `PalGolf_API`: **.NET CLR version = No Managed Code**, pipeline **Integrated**.
2. With `Trusted_Connection`, give `IIS AppPool\PalGolf_API` a SQL login on the server
   (or use a SQL login in the connection string, e.g. for RDS).
3. Site `PalGolf_API` → physical path `C:\inetpub\wwwroot\PalGolf_API`, app pool `PalGolf_API`,
   binding **`https :8000`**, host name `SERVER`, select the certificate. No HTTP binding.
4. Set `ASPNETCORE_ENVIRONMENT=Production`: IIS Manager → site → *Configuration Editor* →
   `system.webServer/aspNetCore` → `environmentVariables`, or in the site `web.config`:

   ```xml
   <aspNetCore ...>
     <environmentVariables>
       <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
     </environmentVariables>
   </aspNetCore>
   ```

### 1.5 Check

Open `https://SERVER:8000/` → `{"service":"EventRegistration.Api","status":"ok"}`.
If it fails, enable `stdoutLogEnabled="true"` in the site `web.config` and read `logs\`.

## 2. Frontend (App)

### 2.1 Build (dev machine)

From `frontend/web`:

```cmd
npm ci
```

```cmd
npm run build
```

Output: `dist/web/browser` (includes `web.config` and `appSettings.json` from `public/`).

### 2.2 IIS site

1. Copy the **contents** of `dist/web/browser` to `C:\inetpub\wwwroot\PalGolf_App`.
2. App pool `PalGolf_App`: **No Managed Code**.
3. Site `PalGolf_App` → physical path above, binding **`https :443`**, host name `SERVER`,
   select the certificate. Remove the *Default Web Site* binding on 443 (and 80) if present.

### 2.3 Runtime settings

Edit `C:\inetpub\wwwroot\PalGolf_App\appSettings.json` on the server (read at runtime, no rebuild).
It is served to every browser, so never put secrets in it.

```json
{
  "apiBaseUrl": "https://SERVER:8000",
  "faceEngineBaseUrl": "/face-api",
  "appName": "PAL GOLF",
  "footerText": "Powered by N-PAX - NXPERT SYSTEM"
}
```

`faceEngineBaseUrl` stays `/face-api`: the browser calls the App's own origin and the `web.config`
rule `FastAPI_Proxy` forwards it to the Face Engine.

### 2.4 Point the proxy at port 8001

The repo `web.config` forwards to port **8100**. Edit
`C:\inetpub\wwwroot\PalGolf_App\web.config`, rule `FastAPI_Proxy`:

```xml
<action type="Rewrite" url="http://127.0.0.1:8001/{R:1}" appendQueryString="true" />
```

Keep a copy of the edited `appSettings.json` and `web.config`; every new build overwrites both
with repo values.

### 2.5 Check

Open `https://SERVER/` → sign-in page. Refresh on a deep link (e.g. `/scanner`) must not 404.

## 3. Face Engine

### 3.1 Package (dev machine)

Copy `facerog-engine` to `C:\deploy\PalGolf_FaceEngine` **without** `.venv*`, `datasets\`,
`tests\`, `__pycache__\`, `.env`, `.env.local` and any `*.log`. Keep `app\ml\weights\`
(`det_10g.onnx`, `w600k_r50.onnx`); if it is empty, run `python scripts/download_weights.py` first.

```cmd
robocopy facerog-engine C:\deploy\PalGolf_FaceEngine /E /XD .venv .venv_ec2 datasets tests __pycache__ .pytest_cache /XF .env .env.local *.log
```

Zip it for transfer if you like:

```cmd
powershell Compress-Archive C:\deploy\PalGolf_FaceEngine C:\deploy\PalGolf_FaceEngine.zip
```

> The InsightFace SCRFD/ArcFace weights are licensed for non-commercial research use only.

### 3.2 Install (server)

Unzip to `C:\inetpub\wwwroot\PalGolf_FaceEngine`, then from that folder:

```cmd
py -3.13 -m venv .venv
```

```cmd
.venv\Scripts\pip install -r requirements.txt
```

`requirements.txt` installs CPU `onnxruntime`, which suits standard EC2 instances. On a GPU
instance (e.g. `g4dn`, NVIDIA driver installed) replace it with `onnxruntime-gpu`
(CUDA 12 + cuDNN 9) or `onnxruntime-directml`.

### 3.3 Config

Create `.env` in the Face Engine folder (all settings are optional; defaults are in
`app/core/config.py`):

```ini
FRE_MODEL_DIR=app/ml/weights
FRE_DETECTOR_MODEL_FILE=det_10g.onnx
FRE_EMBEDDER_MODEL_FILE=w600k_r50.onnx
FRE_USE_GPU=false

FRE_BACKEND_BASE_URL=https://SERVER:8000
FRE_CORS_ORIGINS=["https://SERVER"]

FRE_VIDEO_AUTOSTART=false
FRE_MATCH_THRESHOLD=0.40
FRE_MAX_UPLOAD_SIZE_BYTES=10485760
```

- `FRE_BACKEND_BASE_URL` = API public URL. `FRE_CORS_ORIGINS` = App public URL.
- `FRE_USE_GPU=true` only with a GPU `onnxruntime` package.
- `FRE_VIDEO_AUTOSTART=false`: the server has no camera; frames come from the browser.

Create `.env.local` (secret, machine-local) with the key from [1.3](#13-generate-the-face-engine-key):

```ini
FRE_BACKEND_API_KEY=<same value as API FaceEngine:ApiKey>
```

`datasets\faces` is created on the first gallery sync from the API.

### 3.4 Start script

Create `start_service.bat` in the Face Engine folder (replace the repo copy, which has dev paths):

```bat
@echo off
cd /d C:\inetpub\wwwroot\PalGolf_FaceEngine
".venv\Scripts\python.exe" -m uvicorn app.main:app --host 127.0.0.1 --port 8001 >> fastapi.log 2>&1
```

Bind to `127.0.0.1` only; the public path is the App's `/face-api` proxy.

### 3.5 Check

Run `start_service.bat`, then open `http://127.0.0.1:8001/health` on the server → HTTP 200.
`http://127.0.0.1:8001/api/v1/health` shows the execution provider and enrolled face count.
Stop it (Ctrl+C) before registering the service.

### 3.6 Register as a Windows service (NSSM)

From an elevated cmd:

```cmd
nssm install PalGolf_FaceEngine C:\inetpub\wwwroot\PalGolf_FaceEngine\start_service.bat
```

```cmd
nssm set PalGolf_FaceEngine AppDirectory C:\inetpub\wwwroot\PalGolf_FaceEngine
```

```cmd
nssm set PalGolf_FaceEngine Start SERVICE_AUTO_START
```

```cmd
nssm start PalGolf_FaceEngine
```

Then from a browser: `https://SERVER/face-api/health` → 200 (proves the IIS proxy works).

## 4. First login

Production does **not** seed the demo admin (`admin@demo.com` is Development only). Create the
first admin as agreed for this deployment, then sign in at `https://SERVER/`.

## 5. Cross-reference checklist

| Setting | Where | Value |
|---------|-------|-------|
| `apiBaseUrl` | App `appSettings.json` | `https://SERVER:8000` |
| `faceEngineBaseUrl` | App `appSettings.json` | `/face-api` |
| `FastAPI_Proxy` rule `url` | App `web.config` | `http://127.0.0.1:8001/{R:1}` |
| `App:FrontendBaseUrl` | API `appsettings.Production.json` | `https://SERVER` |
| `FaceEngine:ApiKey` | API `appsettings.Production.json` | key from 1.3 |
| `FRE_BACKEND_API_KEY` | Face Engine `.env.local` | same key |
| `FRE_BACKEND_BASE_URL` | Face Engine `.env` | `https://SERVER:8000` |
| `FRE_CORS_ORIGINS` | Face Engine `.env` | `["https://SERVER"]` |
| `--port` | Face Engine `start_service.bat` | `8001` (same as `web.config` rule) |
| Inbound ports | Security group + Windows Firewall | 443, 8000 only (80 for Let's Encrypt) |

## 6. Go-live security checklist

Both 443 and 8000 face the internet, so all items apply before go-live.

- [ ] New JWT key, SMTP password and Face Engine key; none of the repo values are used.
- [ ] CORS in `Program.cs` restricted to exactly the App origin, without `AllowCredentials`
      (the app uses Bearer tokens), then republish:
      `p.WithOrigins("https://SERVER").AllowAnyHeader().AllowAnyMethod()`.
- [ ] Both public sites HTTPS only, with a publicly trusted certificate.
- [ ] Security group: 8001, 1433 and RDP not open to `0.0.0.0/0`.
- [ ] `outbox/` folder disabled or not reachable in Production.
- [ ] `appsettings.Production.json` and `.env.local` readable only by administrators and the
      service/app-pool identities.
- [ ] Face photos in `datasets\faces` are biometric data: restrict folder access and include them
      in the EBS snapshot/backup with the database.

## 7. Updating later

- **Backend:** `dotnet publish` again, stop the `PalGolf_API` app pool, copy over the files
  (keep `appsettings.Production.json`), start the pool.
- **Frontend:** `npm ci` + `npm run build`, replace the site contents, then restore the server's
  `appSettings.json` and `web.config` (port 8001 proxy rule).
- **Face Engine:** `nssm stop PalGolf_FaceEngine`, copy the new code over (keep `.venv`, `.env`,
  `.env.local`, `datasets\`, `start_service.bat`), run `.venv\Scripts\pip install -r requirements.txt`
  if requirements changed, then `nssm start PalGolf_FaceEngine`.

`dotnet publish`, `iisreset` and database drops affect the live deployment: agree a window first.

## Offline Python packages

If the server has no internet, build a wheel bundle on a machine with the same Python (3.13, 64-bit
Windows):

```cmd
py -3.13 -m pip download -r requirements.txt -d wheels
```

Copy `wheels\` next to `requirements.txt` on the server, then:

```cmd
.venv\Scripts\pip install --no-index --find-links wheels -r requirements.txt
```

## Troubleshooting

| Symptom | Check |
|---------|-------|
| Site unreachable from outside | Security group inbound rule, Windows Firewall rule, Elastic IP/DNS |
| API 500.30 on start | `ASPNETCORE_ENVIRONMENT`, connection string, SQL login for the app pool; stdout log |
| App loads, calls fail with CORS/mixed content | `apiBaseUrl` is `https://SERVER:8000`; API CORS origin is exactly `https://SERVER` |
| `/face-api/...` returns 404 | ARR *Enable proxy* not ticked; Face Engine service not running |
| `/face-api/...` returns 502 | `web.config` rule port is not 8001 (a new build resets it to 8100) |
| Face check-in never matches anyone | `FRE_BACKEND_API_KEY` ≠ `FaceEngine:ApiKey`, or `FRE_BACKEND_BASE_URL` wrong; see `fastapi.log` |
| Scanner camera blocked | Site not on HTTPS, or certificate not trusted / name mismatch |
| Calendar invite times off by 8 hours | Server time zone still UTC (see 0.2 step 10) |
