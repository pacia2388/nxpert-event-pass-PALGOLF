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

  // The tab is named once the theme is known (core/theme.ts), so it never flashes a wrong name.
}

// An optional line of wording: missing or blank keeps the built-in text.
function wording(settings: Record<string, unknown>, key: string, fallback: string): string {
  const value = settings[key];
  if (value === undefined || value === null) return fallback;
  if (typeof value !== 'string') throw new Error(`appSettings.json: ${key} must be a string`);
  return value.trim() || fallback;
}

/** Menus an installation can switch off for everyone in menu.json. Every other page is always on. */
export const MENU_IDS = [
  'dashboard',
  'calendar',
  'mailPlanner',
  'mealScanner',
  'users',
  'teams',
  'myTeam',
  'voucher',
] as const;
export type MenuId = (typeof MENU_IDS)[number];

const disabledMenus = new Set<MenuId>();

/** False when menu.json switches this menu off. */
export function menuEnabled(id: MenuId): boolean {
  return !disabledMenus.has(id);
}

/**
 * Reads menu.json (served next to index.html). Never stops the app: a missing or broken file
 * leaves every menu on and logs why, so a bad edit cannot lock users out.
 */
export async function loadMenus(): Promise<void> {
  disabledMenus.clear();
  let file: unknown;
  try {
    const res = await fetch('menu.json', { cache: 'no-cache' });
    if (!res.ok) {
      console.warn(`menu.json: HTTP ${res.status}; every menu is on`);
      return;
    }
    file = await res.json();
  } catch (err) {
    // IIS answers a missing menu.json with index.html, which is not JSON.
    console.error('menu.json is missing or not valid JSON; every menu is on', err);
    return;
  }
  const menus = (file as { menus?: unknown } | null)?.menus;
  if (typeof menus !== 'object' || menus === null || Array.isArray(menus)) {
    console.error('menu.json: "menus" must be an object; every menu is on');
    return;
  }
  const off: MenuId[] = [];
  for (const [id, on] of Object.entries(menus)) {
    if (!(MENU_IDS as readonly string[]).includes(id)) {
      console.warn(`menu.json: "${id}" is not a menu that can be switched off; ignored`);
    } else if (typeof on !== 'boolean') {
      console.error(`menu.json: "${id}" must be true or false; every menu is on`);
      return;
    } else if (!on) {
      off.push(id as MenuId);
    }
  }
  off.forEach((id) => disabledMenus.add(id));
}
