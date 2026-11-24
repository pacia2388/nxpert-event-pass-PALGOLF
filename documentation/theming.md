# Theming

One build can wear either of two looks:

| Theme | File | Look |
|-------|------|------|
| **PALGOLF** (default) | `theme.palgolf.json` | Navy/blue PAL Interclub look: tournament poster on the login page, Montserrat headings, solid buttons and footer |
| **NXPass** | `theme.nxpass.json` | The original teal/green NXPass look: green "N" logo on the login page, gradient buttons, glass footer, soft glow behind sign-in pages, app name "NXPass" |

The theme covers the web app's colours, fonts (and the stylesheet that loads them), favicon, tab
icon, home-screen icon (iPhone), browser bar colour, login image and its words, header logo, the
app name and footer text, emails, the pass page and the voucher image. The PWA manifest icons and
the `PALGOLF-` voucher code prefix do not change.

Emails are sent under the app's name (the theme's `appName`, else `App:Name`) unless
`Email:FromName` is set in the API config.

## How to set the theme

Each side ships both theme files and names the one to use with the same key, `"theme"`, in its
own settings. **Set both to the same file.**

| Side | Files | Setting |
|------|-------|---------|
| Web app (App site) | `theme.palgolf.json`, `theme.nxpass.json` beside `index.html` (source `frontend/web/public/`) | `"theme"` in `appSettings.json`, e.g. `"theme": "theme.nxpass.json"` |
| API | `theme.palgolf.json`, `theme.nxpass.json` beside `EventRegistration.Api.dll` (source `backend/EventRegistration.Api/`) | `"theme"` in `appsettings.json` / `appsettings.Production.json`, e.g. `"theme": "theme.nxpass.json"` |

The web app reads its theme file from its own site, never from the API, so a slow or busy API can't
delay or change the look. The API uses its file for emails, the pass page and meal stubs.

### On a server (IIS)

1. In the App site folder, edit `appSettings.json`: `"theme": "theme.nxpass.json"` (or
   `theme.palgolf.json`).
2. In the API site folder, set `"theme"` in `appsettings.Production.json` to the same file name.
3. Recycle the API app pool (the API reads `theme` at start-up), then reload the web app in
   the browser. No rebuild or republish. Edits inside a theme file apply on the next request.

A new frontend build overwrites `appSettings.json` with the dev values: keep the server copy and
restore it after each deploy (see [deployment.md](deployment.md)).

### In development

Edit `frontend/web/public/appSettings.json` (`theme`) and the API's `theme` (for a local-only
change, put it in `appsettings.Development.Local.json`). Reload `http://localhost:4200`.

### Check which theme is on

- `<App URL>/appSettings.json` shows the web app's `theme` file.
- `<API URL>/api/theme` (for example `http://localhost:5088/api/theme`) shows the API's theme
  (`name`), the one emails use.

A value the API refuses is skipped (the base value stays) and logged as a warning in the API log.

## Where the theme comes from

- **Web app:** the file `appSettings.json` names (`theme`), fetched before the app starts. A missing
  key, a missing file or a file that is not JSON gives PALGOLF (logged in the browser console). A
  key the file leaves out keeps its PALGOLF value.
- **API:** the file its `theme` setting names (relative to the API folder; default
  `theme.palgolf.json`). No file or bad JSON = PALGOLF (logged as a warning). An edit applies on the
  next request.
- **Both theme files are published** (they are the shipped looks), so a deploy refreshes them. For
  your own look use a new file name (below): a deploy never adds or removes it.
- `index.html` carries no look: until the theme file is read, the tab has no icon or name (never
  PALGOLF's). The browser remembers the last theme's app name, icons and bar colour, so on a
  reload they show straight away; only a browser's very first visit waits for the theme file.

## Theme images

Each theme's images live in its own folder of the web app:

| Folder | Files |
|--------|-------|
| `themes/palgolf/` | `favicon.ico`, `tab-192.png`, `apple-touch-icon.png`, `login-poster.jpg` |
| `themes/nxpass/` | `favicon.ico`, `tab-192.png` (also its home-screen icon), `logo.png` |

The PWA install icons stay in `icons/` (the manifest is not themed).

## A custom look

1. Copy `theme.palgolf.json` or `theme.nxpass.json` (every key filled in).
2. Change the keys you want and save it under a new name, e.g.
   `theme.custom.json`, in **both** the App site folder and the API site folder. Put custom images
   in the App site, e.g. `themes/custom/`.
3. Point `"theme"` in both (web app and API) at it.

Publishing never outputs or deletes a file it does not ship, so `theme.custom.json` survives
redeploys. Custom images in the App site do not: a new frontend build replaces that folder, so
keep a copy and restore them after each deploy, as with `appSettings.json`.

## Format (version 1)

`base` picks a built-in theme; every other key overrides one value of it. The shipped files list
every key. Delete a key to keep the base value (the web app then uses the PALGOLF value, so keep
custom files complete).

```json
{
  "version": 1,
  "base": "NXPass",
  "appName": "Demo Cup",
  "colors": { "primary": "#aa3300" },
  "logos": { "header": "themes/custom/logo.png" },
  "login": { "lines": ["Feb 1, 2027"] }
}
```

| Key | Values |
|-----|--------|
| `base` | `PALGOLF` \| `NXPass` (case ignored). Anything else → PALGOLF |
| `version` | `1` |
| `appName`, `footerText` | Text, 1–200 characters, or `null`. `null` (or left out) keeps the name from `appSettings.json` (web) and `App:Name` (API) |
| `colors.*` | `#rgb` or `#rrggbb`. Keys: `primary`, `primaryDeep`, `primaryContainer`, `onPrimaryContainer`, `secondaryContainer`, `onSecondaryContainer`, `tertiary`, `navy`, `navyMid`, `sky`, `accent`, `gold`, `ink`, `inkSoft`, `line`, `appBackground`, `surfaceLow`, `surfaceHigh`, `surfaceHighest`, `tint`, `tintSoft`, `browserBar` |
| `gradients.*` | `linear-gradient(…)` or `radial-gradient(…)` of colours. Keys: `brand`, `brandSoft`, `header`, `progress`, `button` |
| `fonts.display` | A font list, e.g. `'Montserrat', 'Roboto', sans-serif` |
| `fonts.stylesheet` | An `https://` stylesheet that loads the display font (e.g. a Google Fonts URL), or `null` when the font is already loaded (Roboto always is) |
| `layout.buttons` | `solid` \| `gradient` |
| `layout.footer` | `solid` \| `glass` |
| `layout.authBackground` | `gradient` \| `glow` (sign-in, sign-up and similar pages) |
| `layout.loginImage` | `poster` (the image fills the login panel; its words are read by screen readers and shown on phones) \| `logo` (the image is centred on a light panel with the title, lines and caption shown under it) |
| `logos.favicon`, `logos.tabIcon`, `logos.appleTouchIcon`, `logos.loginImage` | A path on the web app (e.g. `themes/custom/logo.png`) or an `https://` URL |
| `logos.header` | Same, or `null` for the square badge in the header. Also shown at the top of emails |
| `login.imageAlt`, `login.title`, `login.caption` | Text, 1–200 characters |
| `login.lines` | Up to 6 texts. `Label: value` lines show the label in bold |

## Validation

The API checks every value; a value that fails is dropped (the base value stays) and a warning is
logged. A theme problem never stops the app. The web app trusts its theme file as it trusts
`appSettings.json` (both are served by the App site); keep the two copies identical.

- Colours: `#rgb` or `#rrggbb` only (no transparency): the meal-stub image and email clients read
  nothing else reliably.
- Gradients: only colours, numbers, `%`, `deg` and commas inside `linear-`/`radial-gradient()`; no
  `url`, `image` or `expression`; ≤ 500 characters.
- Fonts: letters, digits, spaces, quotes, commas and hyphens only. Stylesheet: `https://` only.
- Paths: letters, digits and `._~-/?=&%`; no `..`, no `//` at the start; or an `https://` URL.
- Text: trimmed, 1–200 characters, no control characters.
- Unknown keys are ignored with a warning.

## Where it lives in the code

| Part | File |
|------|------|
| Theme files | `backend/EventRegistration.Api/theme.*.json`, `frontend/web/public/theme.*.json` (identical; `ThemeTests` checks it) |
| Built-in themes (API fallback and base values) | `backend/EventRegistration.Api/Services/Theming/BuiltInThemes.cs` |
| Reading and checking the file | `Services/Theming/ThemeResolver.cs`, `ThemeStore.cs` |
| Endpoint | `Controllers/ThemeController.cs` (`GET /api/theme`) |
| Emails, pass page, meal stubs | `Services/EmailTemplates.cs`, `MailPlanDispatcher.cs`, `PassService.cs`, `Controllers/PassesController.cs` |
| Web app | `frontend/web/src/app/core/theme.ts` (loads and applies), `src/styles.scss` (tokens and layout variants) |
| Theme images | `frontend/web/public/themes/palgolf/`, `frontend/web/public/themes/nxpass/` |
