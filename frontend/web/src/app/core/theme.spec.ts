import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { BRANDING } from './config';
import { ACTIVE_THEME, PALGOLF_THEME, ThemeDefinition, applyTheme, loadTheme } from './theme';

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
    loginImage: 'nxpax-logo.png',
  },
};

function head(): void {
  document.head.innerHTML = `
    <link rel="icon" type="image/x-icon" href="favicon.ico?v=3" />
    <link rel="icon" type="image/png" sizes="192x192" href="icons/tab-192.png?v=3" />
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
    expect(ACTIVE_THEME.current.logos.loginImage).toBe('nxpax-logo.png');
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

  it('loads the theme from the API before the app starts', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(JSON.stringify(NXPASS), { status: 200 })),
    );

    await loadTheme();

    expect(document.documentElement.style.getPropertyValue('--pg-blue')).toBe('#0d9488');
  });

  it('starts in the default look when the API cannot be reached', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => {
        throw new TypeError('Failed to fetch');
      }),
    );

    await expect(loadTheme()).resolves.toBeUndefined();

    expect(document.documentElement.style.getPropertyValue('--pg-blue')).toBe('');
    expect(ACTIVE_THEME.current.name).toBe('PALGOLF');
  });
});
