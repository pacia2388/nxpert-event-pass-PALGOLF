import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  ActivatedRouteSnapshot,
  CanActivateFn,
  Router,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';
import { AuthResponse } from './models';
import { loadMenus } from './config';
import {
  authGuard,
  confirmerGuard,
  homeFor,
  menuGuard,
  nonMerchantGuard,
  participantOrCaptainGuard,
  voucherGuard,
} from './guards';
import { AuthService } from './auth.service';

type Role = AuthResponse['role'];

// Sign in as a role (or stay signed out) the way the app does: the stored session.
function signIn(role: Role | null, canConfirmRegistrations = false): void {
  localStorage.clear();
  if (role) {
    localStorage.setItem(
      'evt_auth',
      // JWT-shaped with no expiry: AuthService reads the payload to drop expired sessions.
      JSON.stringify({
        token: `x.${btoa('{}')}.x`,
        userId: 1,
        fullName: 'Test User',
        role,
        canConfirmRegistrations,
      }),
    );
  }
  TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient()] });
}

// Where a guard sends the user: 'allowed', or the URL it redirects to.
function visit(guard: CanActivateFn, url: string): string {
  const result = TestBed.runInInjectionContext(() =>
    guard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot),
  );
  if (result === true) return 'allowed';
  return TestBed.inject(Router).serializeUrl(result as UrlTree);
}

describe('route guards', () => {
  describe('a Captain can open the Team page', () => {
    it('opens the Team page', () => {
      signIn('Captain');
      expect(visit(participantOrCaptainGuard, '/team')).toBe('allowed');
    });
  });

  describe('the Voucher page is for Participants and Captains', () => {
    it.each(['Participant', 'Captain'] as Role[])('lets a %s open it', (role) => {
      signIn(role);
      expect(visit(voucherGuard, '/voucher')).toBe('allowed');
    });

    it.each([
      ['Admin', '/home'],
      ['SuperAdmin', '/home'],
      ['Merchant', '/merchant/scan'],
    ] as [Role, string][])('sends a %s to %s', (role, landing) => {
      signIn(role);
      expect(visit(voucherGuard, '/voucher')).toBe(landing);
    });

    it('sends a signed-out visitor to login with a return URL', () => {
      signIn(null);
      expect(visit(voucherGuard, '/voucher')).toBe('/login?returnUrl=%2Fvoucher');
    });
  });

  describe('everyone else is unaffected', () => {
    it('lets a participant into the participant pages', () => {
      signIn('Participant');
      expect(visit(authGuard, '/home')).toBe('allowed');
      expect(visit(nonMerchantGuard, '/account')).toBe('allowed');
    });

    it('lets a participant with the confirm grant into Attendance', () => {
      signIn('Participant', true);
      expect(visit(confirmerGuard, '/admin/events')).toBe('allowed');
    });

    it('lets a participant view the Team page and keeps admins out', () => {
      signIn('Participant');
      expect(visit(participantOrCaptainGuard, '/team')).toBe('allowed');
      TestBed.resetTestingModule();
      signIn('Admin');
      expect(visit(participantOrCaptainGuard, '/team')).toBe('/home');
    });

    it('still sends a merchant to the meal scanner', () => {
      signIn('Merchant');
      expect(visit(nonMerchantGuard, '/account')).toBe('/merchant/scan');
      expect(visit(participantOrCaptainGuard, '/team')).toBe('/merchant/scan');
    });

    it('sends a signed-out visitor to login with a return URL', () => {
      signIn(null);
      expect(visit(participantOrCaptainGuard, '/team')).toBe('/login?returnUrl=%2Fteam');
      expect(visit(authGuard, '/home')).toBe('/login?returnUrl=%2Fhome');
    });
  });
});

describe('menus switched off in menu.json', () => {
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

  it('lets anyone through to a menu that is on', async () => {
    await switchOff('dashboard');
    signIn('Participant');
    expect(visit(menuGuard('voucher'), '/voucher')).toBe('allowed');
  });

  it('sends a signed-in user home from a menu that is off', async () => {
    await switchOff('voucher');
    signIn('Participant');
    expect(visit(menuGuard('voucher'), '/voucher')).toBe('/home');
  });

  it('sends a signed-out visitor to login from a menu that is off', async () => {
    await switchOff('voucher');
    signIn(null);
    expect(visit(menuGuard('voucher'), '/voucher')).toBe('/login');
  });

  it('lands a merchant on Home when the Meal Scanner is off', async () => {
    signIn('Merchant');
    const auth = TestBed.inject(AuthService);
    expect(homeFor(auth)).toBe('/merchant/scan');
    await switchOff('mealScanner');
    expect(homeFor(auth)).toBe('/home');
  });
});
