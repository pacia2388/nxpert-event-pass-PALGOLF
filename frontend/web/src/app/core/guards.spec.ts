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
import {
  adminGuard,
  authGuard,
  confirmerGuard,
  isTeamLead,
  merchantGuard,
  nonMerchantGuard,
} from './guards';

type Role = AuthResponse['role'];

// Sign in as a role (or stay signed out) the way the app does: the stored session.
function signIn(role: Role | null, canConfirmRegistrations = false): void {
  localStorage.clear();
  if (role) {
    localStorage.setItem(
      'evt_auth',
      JSON.stringify({ token: 't', userId: 1, fullName: 'Test User', role, canConfirmRegistrations }),
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
  describe('a Team Lead can open only the Team page', () => {
    it('opens the Team page', () => {
      signIn('TL');
      expect(visit(isTeamLead, '/team')).toBe('allowed');
    });

    it.each([
      ['Home, Browse Events and My Events', authGuard, '/home'],
      ['Calendar, Mail Planner and Edit account', nonMerchantGuard, '/account'],
      ['admin pages', adminGuard, '/admin/users'],
      ['the meal scanner', merchantGuard, '/merchant/scan'],
    ])('is sent to the Team page from %s', (_label, guard, url) => {
      signIn('TL');
      expect(visit(guard, url)).toBe('/team');
    });

    it('is sent to the Team page from Attendance even with the confirm grant', () => {
      signIn('TL', true);
      expect(visit(confirmerGuard, '/admin/events')).toBe('/team');
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

    it('keeps participants and admins out of the Team page', () => {
      signIn('Participant');
      expect(visit(isTeamLead, '/team')).toBe('/home');
      TestBed.resetTestingModule();
      signIn('Admin');
      expect(visit(isTeamLead, '/team')).toBe('/home');
    });

    it('still sends a merchant to the meal scanner', () => {
      signIn('Merchant');
      expect(visit(nonMerchantGuard, '/account')).toBe('/merchant/scan');
      expect(visit(isTeamLead, '/team')).toBe('/merchant/scan');
    });

    it('sends a signed-out visitor to login with a return URL', () => {
      signIn(null);
      expect(visit(isTeamLead, '/team')).toBe('/login?returnUrl=%2Fteam');
      expect(visit(authGuard, '/home')).toBe('/login?returnUrl=%2Fhome');
    });
  });
});
