import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { BRANDING, THEME_FILE, loadAppSettings } from './config';
import {
  ACTIVE_THEME,
  PALGOLF_THEME,
  THEME_HEAD_STORAGE_KEY,
  ThemeDefinition,
  applyTheme,
  loadTheme,
} from './theme';

const NXPASS: ThemeDefinition = {
  ...PALGOLF_THEME,
  name: 'NXPass',
  source: 'file',
  appName: 'NXPass',
  footerText: 'Powered by @NOW — NXPERT OFFICE WORKSPACE',
  colors: { ...PALGOLF_THEME.colors, primary: '#0d9488', browserBar: '#0d9488' },
  layout: { buttons: 'gradient', footer: 'glass', authBackground: 'glow', loginImage: 'logo' },
  logos: {
    ...PALGOLF_THEME.logos,
    favicon: 'themes/nxpass/favicon.ico',
    loginImage: 'themes/nxpass/logo.png',
  },
};

/** Answers appSettings.json (naming theme.nxpass.json) and the theme file itself. */
function serve(themeFile: unknown | Error): void {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string) => {
      if (url === 'appSettings.json') {
        return new Response(
          JSON.stringify({ apiBaseUrl: '', faceEngineBaseUrl: '', theme: 'theme.nxpass.json' }),
        );
      }
      if (url !== 'theme.nxpass.json') return new Response('', { status: 404 });
      if (themeFile instanceof Error) throw themeFile;
      return new Response(JSON.stringify(themeFile));
    }),
  );
}

function head(): void {
  document.head.innerHTML = `
    <link rel="icon" type="image/x-icon" href="favicon.ico?v=3" />
    <link rel="icon" type="image/png" sizes="192x192" href="themes/palgolf/tab-192.png" />
    <link rel="apple-touch-icon" href="themes/palgolf/apple-touch-icon.png" />
    <meta name="theme-color" content="#051f63" />`;
}

describe('theme', () => {
  const brandingBefore = { ...BRANDING };

  beforeEach(() => {
    head();
    document.documentElement.removeAttribute('style');
    ACTIVE_THEME.current = PALGOLF_THEME;
  });

  afterEach(() => {
    Object.assign(BRANDING, brandingBefore);
    vi.unstubAllGlobals();
  });

  it('paints the page with the theme: colours, layout variants, icons and browser bar', () => {
    applyTheme(NXPASS);

    const html = document.documentElement;
    expect(html.style.getPropertyValue('--pg-blue')).toBe('#0d9488');
    expect(html.style.getPropertyValue('--mat-sys-primary')).toBe('#0d9488');
    expect(html.dataset['theme']).toBe('nxpass');
    expect(html.dataset['buttons']).toBe('gradient');
    expect(html.dataset['footer']).toBe('glass');
    expect(html.dataset['authBg']).toBe('glow');
    expect(document.querySelector('link[type="image/x-icon"]')?.getAttribute('href')).toBe(
      'themes/nxpass/favicon.ico',
    );
    expect(document.querySelector('meta[name="theme-color"]')?.getAttribute('content')).toBe(
      '#0d9488',
    );
    expect(ACTIVE_THEME.current.logos.loginImage).toBe('themes/nxpass/logo.png');
  });

  it("remembers the theme's icons and bar colour for the next page load", () => {
    applyTheme(NXPASS);

    expect(JSON.parse(localStorage.getItem(THEME_HEAD_STORAGE_KEY)!)).toEqual({
      favicon: 'themes/nxpass/favicon.ico',
      tabIcon: NXPASS.logos.tabIcon,
      appleTouchIcon: NXPASS.logos.appleTouchIcon,
      browserBar: '#0d9488',
    });
  });

  it("uses the theme's app name and footer when it has them", () => {
    applyTheme(NXPASS);

    expect(BRANDING.appName).toBe('NXPass');
    expect(BRANDING.footerText).toBe('Powered by @NOW — NXPERT OFFICE WORKSPACE');
    expect(document.title).toBe('NXPass');
  });

  it('keeps the configured name when the theme has none', () => {
    BRANDING.appName = 'Demo Cup';

    applyTheme({ ...NXPASS, appName: null, footerText: null });

    expect(BRANDING.appName).toBe('Demo Cup');
  });

  it('loads the theme file appSettings.json names, never the API', async () => {
    serve({
      base: 'NXPass',
      appName: 'NXPass',
      colors: { primary: '#0d9488' },
      fonts: { display: "'Roboto', sans-serif", stylesheet: null },
      logos: { appleTouchIcon: 'themes/nxpass/tab-192.png' },
    });
    await loadAppSettings();

    await loadTheme();

    expect(THEME_FILE).toBe('theme.nxpass.json');
    expect(ACTIVE_THEME.current.name).toBe('NXPass');
    expect(ACTIVE_THEME.current.source).toBe('file');
    expect(document.documentElement.style.getPropertyValue('--pg-blue')).toBe('#0d9488');
    expect(document.querySelector('link[rel="apple-touch-icon"]')?.getAttribute('href')).toBe(
      'themes/nxpass/tab-192.png',
    );
    // A key the file leaves out keeps its PALGOLF value.
    expect(ACTIVE_THEME.current.colors.navy).toBe(PALGOLF_THEME.colors.navy);
    expect(document.querySelector('link[data-theme-font]')).toBeNull();
  });

  it("loads the theme's display font", () => {
    applyTheme(PALGOLF_THEME);

    expect(document.querySelector('link[data-theme-font]')?.getAttribute('href')).toBe(
      PALGOLF_THEME.fonts.stylesheet,
    );
  });

  it('starts in the PALGOLF look, named from appSettings.json, when the theme file cannot be read', async () => {
    serve(new TypeError('Failed to fetch'));
    await loadAppSettings();
    BRANDING.appName = 'Demo Cup';
    document.title = 'NXPass';

    await expect(loadTheme()).resolves.toBeUndefined();

    expect(document.documentElement.style.getPropertyValue('--pg-blue')).toBe('#0f5fb8');
    expect(ACTIVE_THEME.current.name).toBe('PALGOLF');
    expect(ACTIVE_THEME.current.source).toBe('default');
    expect(document.title).toBe('Demo Cup');
  });
});
