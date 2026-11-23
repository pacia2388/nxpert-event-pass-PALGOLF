// The deployment's look. The API reads theme.json beside it (never published; see
// documentation/theming.md) and serves the resolved theme at GET /api/theme. It is applied before
// the app boots, so the page never flashes the wrong look. When the API can't be reached the
// styles.scss defaults (PALGOLF) stay.
import { API_BASE, BRANDING } from './config';

export interface ThemeDefinition {
  /** The built-in theme it starts from: PALGOLF or NXPass. */
  name: string;
  /** "file" (theme.json on the server) or "default" (no file). */
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
  fonts: { display: string };
  layout: {
    buttons: 'solid' | 'gradient';
    footer: 'solid' | 'glass';
    authBackground: 'gradient' | 'glow';
  };
  logos: { favicon: string; tabIcon: string; header: string | null; loginImage: string };
  login: { imageAlt: string; title: string; lines: string[]; caption: string };
}

/** The default look, used until (or unless) the API answers. Same values as styles.scss. */
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
  fonts: { display: "'Montserrat', 'Roboto', sans-serif" },
  layout: { buttons: 'solid', footer: 'solid', authBackground: 'gradient' },
  logos: {
    favicon: 'favicon.ico?v=3',
    tabIcon: 'icons/tab-192.png?v=3',
    header: null,
    loginImage: 'brand/pal-interclub-2027-poster.jpg',
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
  doc.querySelector('meta[name="theme-color"]')?.setAttribute('content', theme.colors.browserBar);

  if (theme.appName) BRANDING.appName = theme.appName;
  if (theme.footerText) BRANDING.footerText = theme.footerText;
  doc.title = BRANDING.appName;
  doc
    .querySelector('meta[name="apple-mobile-web-app-title"]')
    ?.setAttribute('content', BRANDING.appName);
}

/** Fetches and applies the deployment's theme. Never fails: a problem keeps the default look. */
export async function loadTheme(): Promise<void> {
  try {
    const res = await fetch(`${API_BASE}/api/theme`, {
      cache: 'no-cache',
      signal: AbortSignal.timeout(5000),
    });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    applyTheme((await res.json()) as ThemeDefinition);
  } catch (err) {
    console.warn('Theme not loaded; using the default look.', err);
  }
}
