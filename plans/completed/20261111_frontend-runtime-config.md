# Frontend runtime config (`appSettings.json`) ✅ Done (2026-11-11)

## Goal

Move the Angular SPA's settings (`apiBaseUrl`, `faceEngineBaseUrl`) out of
`src/environments/environment*.ts` (compiled into the bundle) into `appSettings.json`, served next
to `index.html` and loaded at startup. One build deploys anywhere; change the JSON, reload, done.

## Design

- `frontend/web/public/appSettings.json` — copied to `dist/web/browser/` by the existing `public`
  asset glob. Holds dev values. **No secrets** (it is public).
- `src/app/core/config.ts` keeps exporting `API_BASE` / `FACE_ENGINE_BASE` (now `export let`) and
  adds `loadAppSettings()`. All readers use them at call time, so live bindings pick up the values.
- `src/main.ts` awaits `loadAppSettings()` before `bootstrapApplication`. On failure the page shows
  a short error instead of silently calling the wrong API.
- Caching: fetched with `cache: 'no-cache'` (browser revalidates every load). The service worker
  caches it in a `freshness` data group so the PWA still starts offline.
- `src/environments/` and the `fileReplacements` entry in `angular.json` removed — nothing else
  used them (`isDevMode()` already covers dev/prod checks).

## Steps

- [x] Find every `environment.*` use (only `core/config.ts`)
- [x] Add `public/appSettings.json`
- [x] Loader in `config.ts`, await in `main.ts`
- [x] Remove `src/environments/` + `fileReplacements`
- [x] `ngsw-config.json` freshness data group for `appSettings.json`
- [x] Docs: `.claude/rules/deployment.md`, `.claude/rules/demo-and-config.md`, `deploy-iis` and
      `run-local-demo` skills, root `README.md`
- [x] `npm run build` passes; `dist` has `appSettings.json`
- [x] Manual test (user, 2026-11-11): edit deployed `appSettings.json`, reload, app calls the new API

## Notes

- Old `environment.ts` had a typo'd production URL `https://https://palgolf.com.ph:9000`. Set the
  correct value in the deployed `appSettings.json` on the server (e.g.
  `https://palgolf.com.ph:9000`), and `faceEngineBaseUrl: "/face-api"` (the IIS reverse proxy).
- Deploying a new build overwrites `appSettings.json` with the dev values — keep the server copy
  and restore it after each copy, or exclude it from the copy.

## Change log

| Date | Change |
|------|--------|
| 2026-11-11 | Plan written and implemented |
| 2026-11-11 | Manual test passed (user); moved to completed |
