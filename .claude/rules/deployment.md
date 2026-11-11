# Deployment rules (IIS)

Source: `deploy-iis` skill.

- API and App are **two separate IIS sites on separate ports** (example: App 8080/8443, API
  5000/5001). Do not merge them or change ports without updating the cross-references below.
- **Cross-references must carry ports:** `apiBaseUrl` (`appSettings.json` in the App site root,
  source `frontend/web/public/appSettings.json`) = API public URL; `App:FrontendBaseUrl` (API
  config) = App public URL.
- **`appSettings.json` is read at runtime** — edit it on the server and reload; no rebuild. It is
  public (served to every browser), so never put secrets there. A new build overwrites it with the
  dev values: keep the server copy and restore it after each deploy.
- Use `npm ci` (not `npm install`) for production builds.
- Production config lives in `appsettings.Production.json` with `ASPNETCORE_ENVIRONMENT=Production`;
  real values only, never dev secrets, never committed.
- **LocalDB is dev-only.** Production uses real SQL Server; the SQL login must be able to create the
  DB (or pre-create it and grant `db_owner`).
- API app pool: **No Managed Code**, Integrated pipeline.
- Both sites need **HTTPS** (the scanner camera and mixed-content rules require it); the cert must
  be trusted by scanning devices.
- Tighten CORS to exactly the App origin before go-live.
- Required server components: IIS (Static Content, Default Document), URL Rewrite, .NET 10 Hosting
  Bundle; open the firewall for the chosen ports.
- Keep the server on Philippine time (calendar invites use server time zone).
- `dotnet publish`, `iisreset`, and database drops are **deployment-affecting — ask the user first**.
