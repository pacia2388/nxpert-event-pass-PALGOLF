// The deployment's look: the theme file appSettings.json names ("theme": theme.palgolf.json or
// theme.nxpass.json), served by the web app itself, so a slow API never delays or changes it. It
// is applied before the app boots, so the page never flashes the wrong look. When the file can't
// be read the PALGOLF look is used. See documentation/theming.md.
import { BRANDING, THEME_FILE } from './config';

export interface ThemeDefinition {
  /** The built-in theme it starts from: PALGOLF or NXPass. */
  name: string;
  /** "file" (the theme file was read) or "default" (it could not be). */
  source: 'file' | 'default';
  customised?: boolean;
  appName: string | null;
  footerText: string | null;
  colors: {
    primary: string;
    primaryDeep: string;
    primaryContainer: string;
    onPrimaryContainer: string;
    secondaryContainer: string;
    onSecondaryContainer: string;
    tertiary: string;
    navy: string;
    navyMid: string;
    sky: string;
    accent: string;
    gold: string;
    ink: string;
    inkSoft: string;
    line: string;
    appBackground: string;
    surfaceLow: string;
    surfaceHigh: string;
    surfaceHighest: string;
    tint: string;
    tintSoft: string;
    browserBar: string;
  };
  gradients: { brand: string; brandSoft: string; header: string; progress: string; button: string };
  /** stylesheet: an https stylesheet loading the display font (e.g. Google Fonts), or null. */
  fonts: { display: string; stylesheet: string | null };
  layout: {
    buttons: 'solid' | 'gradient';
    footer: 'solid' | 'glass';
    authBackground: 'gradient' | 'glow';
    /** "poster": the image fills the login panel. "logo": centred on a light panel, words shown. */
    loginImage: 'poster' | 'logo';
  };
  logos: {
    favicon: string;
    tabIcon: string;
    appleTouchIcon: string;
    header: string | null;
    loginImage: string;
  };
  login: { imageAlt: string; title: string; lines: string[]; caption: string };
}

/** The default look, used when the theme file can't be read. Same values as theme.palgolf.json. */
export const PALGOLF_THEME: ThemeDefinition = {
  name: 'PALGOLF',
  source: 'default',
  appName: null,
  footerText: null,
  colors: {
    primary: '#0f5fb8',
    primaryDeep: '#0b4a94',
    primaryContainer: '#d6e4f7',
    onPrimaryContainer: '#051f63',
    secondaryContainer: '#e8f0fb',
    onSecondaryContainer: '#0b4a94',
    tertiary: '#0a2d7a',
    navy: '#051f63',
    navyMid: '#0a2d7a',
    sky: '#1a5aa8',
    accent: '#e02a2a',
    gold: '#e1b115',
    ink: '#0b1b3f',
    inkSoft: '#46557a',
    line: '#cdd7e8',
    appBackground: '#f2f5fa',
    surfaceLow: '#f8fafd',
    surfaceHigh: '#f0f4fa',
    surfaceHighest: '#e6ecf5',
    tint: '#e8f0fb',
    tintSoft: '#f0f5fc',
    browserBar: '#051f63',
  },
  gradients: {
    brand: 'linear-gradient(135deg, #0a2d7a 0%, #0f5fb8 100%)',
    brandSoft: 'linear-gradient(135deg, #e8f0fb 0%, #f0f5fc 100%)',
    header: 'linear-gradient(90deg, #051f63 0%, #0a2d7a 55%, #0f5fb8 100%)',
    progress: 'linear-gradient(90deg, #e02a2a, #e8742a 55%, #e1b115)',
    button: 'linear-gradient(135deg, #0a2d7a 0%, #0f5fb8 100%)',
  },
  fonts: {
    display: "'Montserrat', 'Roboto', sans-serif",
    stylesheet: 'https://fonts.googleapis.com/css2?family=Montserrat:wght@600;800&display=swap',
  },
  layout: { buttons: 'solid', footer: 'solid', authBackground: 'gradient', loginImage: 'poster' },
  logos: {
    favicon: 'themes/palgolf/favicon.ico',
    tabIcon: 'themes/palgolf/tab-192.png',
    appleTouchIcon: 'themes/palgolf/apple-touch-icon.png',
    header: null,
    loginImage: 'themes/palgolf/login-poster.jpg',
  },
  login: {
    imageAlt: 'Philippine Airlines tournament poster: a golfer mid-swing above a golf course',
    title: '78th PAL Interclub Golf Tournament 2027',
    lines: ['Seniors Division: Feb 17–24, 2027', 'Men’s/Regular Division: Feb 25–Mar 4, 2027'],
    caption: 'Cagayan de Oro | Bukidnon',
  },
};

/** The theme in use. Read `current` when needed; it is set at boot. */
export const ACTIVE_THEME: { current: ThemeDefinition } = { current: PALGOLF_THEME };

/** CSS custom properties each theme value feeds (styles.scss reads these). */
function cssVariables(t: ThemeDefinition): Record<string, string> {
  const c = t.colors;
  return {
    '--pg-navy': c.navy,
    '--pg-navy-mid': c.navyMid,
    '--pg-sky': c.sky,
    '--pg-blue': c.primary,
    '--pg-blue-deep': c.primaryDeep,
    '--pg-red': c.accent,
    '--pg-gold': c.gold,
    '--pg-ink': c.ink,
    '--pg-ink-soft': c.inkSoft,
    '--pg-line': c.line,
    '--pg-tint': c.tint,
    '--pg-tint-soft': c.tintSoft,
    '--pg-display': t.fonts.display,
    '--pg-progress': t.gradients.progress,
    '--pg-header-grad': t.gradients.header,
    '--pg-button-grad': t.gradients.button,
    '--brand-grad': t.gradients.brand,
    '--brand-grad-soft': t.gradients.brandSoft,
    '--app-bg': c.appBackground,
    '--mat-sys-primary': c.primary,
    '--mat-sys-primary-container': c.primaryContainer,
    '--mat-sys-on-primary-container': c.onPrimaryContainer,
    '--mat-sys-secondary-container': c.secondaryContainer,
    '--mat-sys-on-secondary-container': c.onSecondaryContainer,
    '--mat-sys-tertiary': c.tertiary,
    '--mat-sys-surface-container-low': c.surfaceLow,
    '--mat-sys-surface-container-high': c.surfaceHigh,
    '--mat-sys-surface-container-highest': c.surfaceHighest,
  };
}

/** Paints the page with a theme: colours, layout variants, tab icons, browser bar and name. */
export function applyTheme(theme: ThemeDefinition, doc: Document = document): void {
  ACTIVE_THEME.current = theme;
  const html = doc.documentElement;
  for (const [name, value] of Object.entries(cssVariables(theme))) {
    if (value) html.style.setProperty(name, value);
  }
  html.dataset['theme'] = theme.name.toLowerCase();
  html.dataset['buttons'] = theme.layout.buttons;
  html.dataset['footer'] = theme.layout.footer;
  html.dataset['authBg'] = theme.layout.authBackground;

  doc
    .querySelector('link[rel="icon"][type="image/x-icon"]')
    ?.setAttribute('href', theme.logos.favicon);
  doc.querySelector('link[rel="icon"][sizes="192x192"]')?.setAttribute('href', theme.logos.tabIcon);
  doc
    .querySelector('link[rel="apple-touch-icon"]')
    ?.setAttribute('href', theme.logos.appleTouchIcon);
  if (theme.fonts.stylesheet) loadFont(theme.fonts.stylesheet, doc);
  doc.querySelector('meta[name="theme-color"]')?.setAttribute('content', theme.colors.browserBar);

  if (theme.appName) BRANDING.appName = theme.appName;
  if (theme.footerText) BRANDING.footerText = theme.footerText;
  nameTab(doc);
  remember(APP_NAME_STORAGE_KEY, BRANDING.appName);
  remember(
    THEME_HEAD_STORAGE_KEY,
    JSON.stringify({
      favicon: theme.logos.favicon,
      tabIcon: theme.logos.tabIcon,
      appleTouchIcon: theme.logos.appleTouchIcon,
      browserBar: theme.colors.browserBar,
    }),
  );
}

/** Loads the theme's font stylesheet (one link, replaced when the theme changes). */
function loadFont(href: string, doc: Document): void {
  let link = doc.querySelector<HTMLLinkElement>('link[data-theme-font]');
  if (!link) {
    link = doc.createElement('link');
    link.rel = 'stylesheet';
    link.dataset['themeFont'] = '';
    doc.head.appendChild(link);
  }
  link.href = href;
}

/** Names the tab and the iOS home-screen icon after the app. */
function nameTab(doc: Document): void {
  doc.title = BRANDING.appName;
  doc
    .querySelector('meta[name="apple-mobile-web-app-title"]')
    ?.setAttribute('content', BRANDING.appName);
}

// Read by the inline script in index.html, which sets the tab's name, icons and the browser bar
// colour from the last theme before the theme file arrives (so no other look flashes first).
export const APP_NAME_STORAGE_KEY = 'evt_app_name';
export const THEME_HEAD_STORAGE_KEY = 'evt_theme_head';

function remember(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Storage blocked (private mode, policy): the tab stays blank until the theme loads.
  }
}

type ThemeSection = 'colors' | 'gradients' | 'fonts' | 'layout' | 'logos' | 'login';

/** A theme file as written (theme.palgolf.json): "base" names the look, sections hold the values. */
export type ThemeFile = { base?: string; appName?: string | null; footerText?: string | null } & {
  [K in ThemeSection]?: Partial<ThemeDefinition[K]>;
};

/** A theme file as a theme. A key the file leaves out keeps its PALGOLF value. */
export function fromThemeFile(file: ThemeFile): ThemeDefinition {
  const d = PALGOLF_THEME;
  return {
    name: file.base ?? d.name,
    source: 'file',
    appName: file.appName ?? null,
    footerText: file.footerText ?? null,
    colors: { ...d.colors, ...file.colors },
    gradients: { ...d.gradients, ...file.gradients },
    fonts: { ...d.fonts, ...file.fonts },
    layout: { ...d.layout, ...file.layout },
    logos: { ...d.logos, ...file.logos },
    login: { ...d.login, ...file.login },
  };
}

/**
 * Reads and applies the theme file appSettings.json names. Never fails: a problem gives the
 * PALGOLF look. The file sits beside index.html, as trusted as appSettings.json itself.
 */
export async function loadTheme(): Promise<void> {
  try {
    if (!THEME_FILE) throw new Error('appSettings.json names no "theme" file');
    const res = await fetch(THEME_FILE, { cache: 'no-cache', signal: AbortSignal.timeout(5000) });
    if (!res.ok) throw new Error(`${THEME_FILE}: HTTP ${res.status}`);
    applyTheme(fromThemeFile((await res.json()) as ThemeFile));
  } catch (err) {
    // IIS answers a missing file with index.html, which is not JSON.
    console.warn('Theme not loaded; using the default look.', err);
    applyTheme(PALGOLF_THEME);
  }
}
