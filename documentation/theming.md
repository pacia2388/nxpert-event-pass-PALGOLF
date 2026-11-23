# Theming

One build can wear either of two looks:

| Theme | Look |
|-------|------|
| **PALGOLF** (default) | Navy/blue PAL Interclub look: tournament poster on the login page, solid buttons and footer |
| **NXPass** | The original teal/green NXPass look: green "N" logo on the login page, gradient buttons, glass footer, soft glow behind sign-in pages, app name "NXPass" |

The theme covers the web app's colours, fonts, logos, tab icon, browser bar colour, login image
and its words, header logo, the app name and footer text, emails, the pass page and the voucher
image. The PWA manifest and the `PALGOLF-` voucher code prefix do not change.

Emails are sent under the app's name (the theme's `appName`, else `App:Name`) unless
`Email:FromName` is set in the API config.

## How to set the theme

The theme is set by one file, `theme.json`, beside the API. It is not changed in the app.

| You want | Put in `theme.json` |
|----------|---------------------|
| **PALGOLF** (new look) | Nothing: delete the file. Or `{ "base": "PALGOLF" }` |
| **NXPass** (old look) | `{ "base": "NXPass" }` |
| Either look with changes | `base` plus only the keys to change (see [Format](#format-version-1)) |

### On a server (IIS)

1. Open the API site folder, the one with `EventRegistration.Api.dll` (for example
   `C:\inetpub\wwwroot\PalGolf_API`).
2. Create or edit `theme.json` there with one of the contents above, saved as UTF-8.
3. Reload the web app in the browser. No restart, rebuild or republish.

Do this once per server: publishing never adds or replaces `theme.json`, so later deploys keep it.

### In development

1. Create or edit `backend/EventRegistration.Api/theme.json` in the checkout whose API you run
   (git ignores it).
2. Reload `http://localhost:4200`. The API reads the file again on the next request: no restart.

If the page loads while the API is still starting, it shows PALGOLF; reload once the API is up.

### Check which theme is on

- Sign in as admin and open **Settings → Theme**: it shows the theme name and whether it comes
  from "theme.json on the server" or is the default.
- Or open `<API URL>/api/theme` (for example `http://localhost:5088/api/theme`) and read `name`.

A value the API refuses is skipped (the base value stays) and logged as a warning in the API log.

## Where the theme comes from

- A file named **`theme.json` in the API site folder** (beside `EventRegistration.Api.dll`).
  Another path can be set with `Theme:Path` (relative to the API folder).
- **Publishing never outputs it** (`CopyToPublishDirectory="Never"` in the `.csproj`), so a redeploy
  keeps the server's copy. A developer adds it by hand after the first publish.
- **No file = PALGOLF.** A file that is not valid JSON also gives PALGOLF (logged as a warning).
- An edit applies on the next request; no restart. Browsers pick it up on their next page load.
- The API serves the resolved theme at **`GET /api/theme`** (public). The web app reads it before it
  starts, so the page never flashes the wrong look. If the API can't be reached, the web app stays
  on PALGOLF.
- The browser remembers the last theme's app name, so on a reload the tab shows it straight away
  instead of the default name. Only a browser's very first visit shows "PAL GOLF" until the theme
  arrives.

`backend/EventRegistration.Api/theme.json` is git-ignored, so each developer can switch looks
locally without committing it.

## The template

Admins download it from **Settings → Theme → Download theme template** (`GET
/api/settings/theme-template`, admin only). It is the whole PALGOLF theme with every key filled in.
Saved unchanged as `theme.json`, it gives exactly the default look.

## Format (version 1)

`base` picks a built-in theme; every other key is optional and overrides one value of it. Delete
any key to keep the base value.

The smallest file:

```json
{ "base": "NXPass" }
```

A customised one:

```json
{
  "version": 1,
  "base": "NXPass",
  "appName": "Demo Cup",
  "colors": { "primary": "#aa3300" },
  "logos": { "header": "brand/demo-logo.png" },
  "login": { "lines": ["Feb 1, 2027"] }
}
```

| Key | Values |
|-----|--------|
| `base` | `PALGOLF` \| `NXPass` (case ignored). Anything else → PALGOLF |
| `version` | `1` |
| `appName`, `footerText` | Text, 1–200 characters, or `null`. `null` (or left out of a PALGOLF file) keeps the name from `appSettings.json` (web) and `App:Name` (API) |
| `colors.*` | `#rgb` or `#rrggbb`. Keys: `primary`, `primaryDeep`, `primaryContainer`, `onPrimaryContainer`, `secondaryContainer`, `onSecondaryContainer`, `tertiary`, `navy`, `navyMid`, `sky`, `accent`, `gold`, `ink`, `inkSoft`, `line`, `appBackground`, `surfaceLow`, `surfaceHigh`, `surfaceHighest`, `tint`, `tintSoft`, `browserBar` |
| `gradients.*` | `linear-gradient(…)` or `radial-gradient(…)` of colours. Keys: `brand`, `brandSoft`, `header`, `progress`, `button` |
| `fonts.display` | A font list, e.g. `'Montserrat', 'Roboto', sans-serif` (the font must already be loaded by the web app) |
| `layout.buttons` | `solid` \| `gradient` |
| `layout.footer` | `solid` \| `glass` |
| `layout.authBackground` | `gradient` \| `glow` (sign-in, sign-up and similar pages) |
| `layout.loginImage` | `poster` (the image fills the login panel; its words are read by screen readers and shown on phones) \| `logo` (the image is centred on a light panel with the title, lines and caption shown under it) |
| `logos.favicon`, `logos.tabIcon`, `logos.loginImage` | A path on the web app (e.g. `brand/logo.png`) or an `https://` URL |
| `logos.header` | Same, or `null` for the square badge in the header. Also shown at the top of emails |
| `login.imageAlt`, `login.title`, `login.caption` | Text, 1–200 characters |
| `login.lines` | Up to 6 texts. `Label: value` lines show the label in bold |

Logo paths are relative to the **web app** site, so custom images go in the App site folder (for
example `C:\inetpub\…\App\brand\demo-logo.png`). A new frontend build replaces that folder: keep a
copy of custom images and restore them after each deploy, as with `appSettings.json`.

## Validation

The API checks every value; a value that fails is dropped (the base value stays) and a warning is
logged. A theme problem never stops the app.

- Colours: `#rgb` or `#rrggbb` only (no transparency): the meal-stub image and email clients read
  nothing else reliably.
- Gradients: only colours, numbers, `%`, `deg` and commas inside `linear-`/`radial-gradient()`; no
  `url`, `image` or `expression`; ≤ 500 characters.
- Fonts: letters, digits, spaces, quotes, commas and hyphens only.
- Paths: letters, digits and `._~-/?=&%`; no `..`, no `//` at the start; or an `https://` URL.
- Text: trimmed, 1–200 characters, no control characters.
- Unknown keys are ignored with a warning.

## Deploying

1. Publish as usual (see [deployment.md](deployment.md)); `theme.json` is not in the output.
2. First deploy only: download the template from **Settings → Theme**, keep the keys you want, and
   save it as `theme.json` in the API site folder. For the old look, `{ "base": "NXPass" }` is
   enough.
3. Reload the web app. **Settings → Theme** shows the theme name and "theme.json on the server".

To go back to PALGOLF, delete `theme.json` (or set `"base": "PALGOLF"`).

## Where it lives in the code

| Part | File |
|------|------|
| Built-in themes | `backend/EventRegistration.Api/Services/Theming/BuiltInThemes.cs` |
| Reading and checking the file | `Services/Theming/ThemeResolver.cs`, `ThemeStore.cs` |
| Endpoints | `Controllers/ThemeController.cs`, `SettingsController.cs` (`theme-template`) |
| Emails, pass page, meal stubs | `Services/EmailTemplates.cs`, `MailPlanDispatcher.cs`, `PassService.cs`, `Controllers/PassesController.cs` |
| Web app | `frontend/web/src/app/core/theme.ts` (loads and applies), `src/styles.scss` (tokens and layout variants) |
| NXPass icons | `frontend/web/public/themes/nxpass/` |
