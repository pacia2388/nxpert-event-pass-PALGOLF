import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { loadAppSettings } from './core/config';
import { RealtimeService } from './core/realtime.service';

describe('App shell', () => {
  let fixture: ComponentFixture<App>;

  // Open the shell signed out, or signed in the way the app does: the stored session.
  function open(signedIn = false): void {
    localStorage.clear();
    if (signedIn) {
      localStorage.setItem(
        'evt_auth',
        JSON.stringify({ token: `x.${btoa('{}')}.x`, userId: 1, fullName: 'Test User', role: 'Admin' }),
      );
    }
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        // No notification hub to connect to in a test: a quiet, empty inbox.
        { provide: RealtimeService, useValue: { unread: signal(0), notifications: signal([]) } },
      ],
    });
    fixture = TestBed.createComponent(App);
    fixture.detectChanges();
  }

  const root = () => fixture.nativeElement as HTMLElement;
  const words = (el: Element | null) => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

  it('credits N-PAX in the footer on every page, signed in or not', () => {
    open();
    expect(words(root().querySelector('footer'))).toBe('Powered by N-PAX - NXPERT SYSTEM');
  });

  it('names the app PAL GOLF in the header once signed in', () => {
    open(true);
    expect(words(root().querySelector('header .brand-text'))).toBe('PAL GOLF');
    expect(words(root())).not.toContain('NXPass');
  });

  describe('when appSettings.json sets its own wording', () => {
    const realFetch = globalThis.fetch;

    // Boot the way main.ts does: read appSettings.json, then start the app.
    async function boot(settings: Record<string, unknown>): Promise<void> {
      globalThis.fetch = (async () =>
        new Response(JSON.stringify({ apiBaseUrl: '', faceEngineBaseUrl: '', ...settings }))) as typeof fetch;
      await loadAppSettings();
    }

    afterEach(async () => {
      await boot({}); // back to the built-in wording for the other tests
      globalThis.fetch = realFetch;
    });

    it('uses the configured app name in the tab title and the header', async () => {
      await boot({ appName: 'Demo Cup' });
      open(true);
      expect(document.title).toBe('Demo Cup');
      expect(words(root().querySelector('header .brand-text'))).toBe('Demo Cup');
    });

    it('uses the configured footer text', async () => {
      await boot({ footerText: 'Run by Demo Org' });
      open();
      expect(words(root().querySelector('footer'))).toBe('Run by Demo Org');
    });

    it('keeps the built-in wording when a setting is left blank', async () => {
      await boot({ appName: '  ', footerText: '' });
      open(true);
      expect(document.title).toBe('PAL GOLF');
      expect(words(root().querySelector('header .brand-text'))).toBe('PAL GOLF');
      expect(words(root().querySelector('footer'))).toBe('Powered by N-PAX - NXPERT SYSTEM');
    });

    it('refuses to start when a setting is not text', async () => {
      await expect(boot({ appName: 42 })).rejects.toThrow('appName');
    });
  });
});
