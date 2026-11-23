import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { loadMenus } from '../../core/config';
import { HomeComponent } from './home';

// Open Home as a user whose listed role checks are true (every other check is false).
function open(roles: Record<string, boolean>): ComponentFixture<HomeComponent> {
  const auth = new Proxy(Object.fromEntries(Object.entries(roles).map(([k, v]) => [k, () => v])), {
    get: (t, k) => (t as Record<PropertyKey, unknown>)[k] ?? (() => false),
  });
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: AuthService, useValue: auth }],
  });
  const fixture = TestBed.createComponent(HomeComponent);
  fixture.detectChanges();
  return fixture;
}

const titles = (root: HTMLElement) =>
  Array.from(root.querySelectorAll('.module-title')).map((t) => t.textContent?.trim());

describe('Home category filters', () => {
  it('are toggle buttons that say which one is pressed', () => {
    const fixture = open({ isAdmin: true });
    const root = fixture.nativeElement as HTMLElement;
    const buttons = Array.from(root.querySelectorAll<HTMLButtonElement>('.pill-tab'));

    buttons.find((b) => b.textContent?.trim() === 'Events')!.click();
    fixture.detectChanges();

    expect(root.querySelector('.pill-tabs')?.getAttribute('role')).toBe('group');
    expect(root.querySelector('[role="tab"]')).toBeNull();
    expect(
      buttons.map((b) => [b.textContent?.trim(), b.getAttribute('aria-pressed')]),
    ).toContainEqual(['Events', 'true']);
    expect(buttons.find((b) => b.textContent?.trim() === 'All')?.getAttribute('aria-pressed')).toBe(
      'false',
    );
  });
});

describe('Home with menus switched off in menu.json', () => {
  const realFetch = globalThis.fetch;

  async function switchOff(...ids: string[]): Promise<void> {
    const menus = Object.fromEntries(ids.map((id) => [id, false]));
    globalThis.fetch = (async () => new Response(JSON.stringify({ menus }))) as typeof fetch;
    await loadMenus();
  }

  afterEach(async () => {
    await switchOff(); // every menu back on
    globalThis.fetch = realFetch;
  });

  it('shows every tile while all menus are on', async () => {
    await switchOff();
    const root = open({ isParticipant: true }).nativeElement as HTMLElement;
    expect(titles(root)).toEqual(['Browse Events', 'My Events', 'Calendar', 'My Team', 'Voucher']);
  });

  it('hides the tiles of menus that are off; event tiles stay', async () => {
    await switchOff('dashboard', 'calendar');
    const root = open({ isAdmin: true }).nativeElement as HTMLElement;
    expect(titles(root)).not.toContain('Dashboard');
    expect(titles(root)).not.toContain('Calendar');
    expect(titles(root)).toContain('Events');
    expect(titles(root)).toContain('My Events');
    expect(titles(root)).toContain('Browse Events');
  });

  it('hides a category tab once all its tiles are off', async () => {
    await switchOff('myTeam');
    const root = open({ isParticipant: true }).nativeElement as HTMLElement;
    const tabs = Array.from(root.querySelectorAll('.pill-tab')).map((b) => b.textContent?.trim());
    expect(tabs).not.toContain('Account');
  });

  it('says so when no tile is left', async () => {
    await switchOff('mealScanner');
    const root = open({ isMerchant: true }).nativeElement as HTMLElement;
    expect(titles(root)).toEqual([]);
    expect(root.querySelector('.empty-state')?.textContent).toContain('No modules are available.');
  });
});
