// Runtime settings, read from appSettings.json (served next to index.html) before the app boots.
// Edit that file on the server to point the app elsewhere; no rebuild needed. Never put secrets there.
const DEFAULT_APP_NAME = 'PAL GOLF';
const DEFAULT_FOOTER_TEXT = 'Powered by N-PAX - NXPERT SYSTEM';

export let API_BASE = '';
export let FACE_ENGINE_BASE = '';
/** Wording the deployment can change. Read its fields when needed; they are filled in at boot. */
export const BRANDING = {
  /** The app's name: tab title, header, install banner, email preview, voucher image. */
  appName: DEFAULT_APP_NAME,
  /** The line in the footer under every page. */
  footerText: DEFAULT_FOOTER_TEXT,
};

export async function loadAppSettings(): Promise<void> {
  const res = await fetch('appSettings.json', { cache: 'no-cache' });
  if (!res.ok) throw new Error(`appSettings.json: HTTP ${res.status}`);
  const s = await res.json();
  if (typeof s.apiBaseUrl !== 'string' || typeof s.faceEngineBaseUrl !== 'string') {
    throw new Error('appSettings.json: apiBaseUrl and faceEngineBaseUrl must be strings');
  }
  API_BASE = s.apiBaseUrl.replace(/\/+$/, '');
  FACE_ENGINE_BASE = s.faceEngineBaseUrl.replace(/\/+$/, '');
  BRANDING.appName = wording(s, 'appName', DEFAULT_APP_NAME);
  BRANDING.footerText = wording(s, 'footerText', DEFAULT_FOOTER_TEXT);

  document.title = BRANDING.appName;
  // iOS names the home-screen icon from this tag.
  document.querySelector('meta[name="apple-mobile-web-app-title"]')?.setAttribute('content', BRANDING.appName);
}

// An optional line of wording: missing or blank keeps the built-in text.
function wording(settings: Record<string, unknown>, key: string, fallback: string): string {
  const value = settings[key];
  if (value === undefined || value === null) return fallback;
  if (typeof value !== 'string') throw new Error(`appSettings.json: ${key} must be a string`);
  return value.trim() || fallback;
}
