import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

// The default landing page for a logged-in user, by role.
export function homeFor(auth: AuthService): string {
  return auth.isMerchant() ? '/merchant/scan' : '/home';
}

export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.isLoggedIn()) return true;
  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

export const adminGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.isAdmin()) return true;
  if (auth.isLoggedIn()) return router.createUrlTree([homeFor(auth)]);
  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

// Admins OR users granted the "confirm registrations" permission.
export const confirmerGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.canConfirm()) return true;
  if (auth.isLoggedIn()) return router.createUrlTree([homeFor(auth)]);
  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

// Routes hidden from merchant and Team Lead accounts (e.g. Edit account). Merchants can only scan
// meal stubs and Team Leads only manage their team, so both are bounced to their own page;
// everyone else logged-in passes.
export const nonMerchantGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.isLoggedIn() && !auth.isMerchant()) return true;
  if (auth.isLoggedIn()) return router.createUrlTree([homeFor(auth)]);
  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

// Merchant-only routes (the meal-stub scanner).
export const merchantGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.isMerchant() || auth.isAdmin()) return true;
  if (auth.isLoggedIn()) return router.createUrlTree([homeFor(auth)]);
  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

// The Voucher page: Participants and Team Leads only.
export const voucherGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.canUseVoucher()) return true;
  if (auth.isLoggedIn()) return router.createUrlTree([homeFor(auth)]);
  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

// Participant or Team Lead (TL) only routes (e.g. the baggage Voucher page).
export const participantOrTlGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.isParticipant() || auth.isTeamLead()) return true;
  if (auth.isLoggedIn()) return router.createUrlTree([homeFor(auth)]);
  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

// Sign-in / sign-up pages: a user who is already signed in goes to their landing page instead.
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.isLoggedIn() ? inject(Router).createUrlTree([homeFor(auth)]) : true;
};
