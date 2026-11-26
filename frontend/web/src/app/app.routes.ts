import { Routes } from '@angular/router';
import {
  authGuard,
  adminGuard,
  confirmerGuard,
  merchantGuard,
  nonMerchantGuard,
  participantOrCaptainGuard,
  guestGuard,
  voucherGuard,
  menuGuard,
} from './core/guards';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./pages/login/login').then((m) => m.LoginComponent),
  },
  {
    path: 'register',
    canActivate: [guestGuard],
    loadComponent: () => import('./pages/register/register').then((m) => m.RegisterComponent),
  },
  {
    path: 'verify/:token',
    loadComponent: () => import('./pages/verify/verify').then((m) => m.VerifyComponent),
  },
  {
    // Public one-time link: choose a password for an account created for you (or after forgot password).
    path: 'set-password/:token',
    loadComponent: () =>
      import('./pages/set-password/set-password').then((m) => m.SetPasswordComponent),
  },
  {
    path: 'e/:slug',
    loadComponent: () =>
      import('./pages/public-event/public-event').then((m) => m.PublicEventComponent),
  },
  {
    // Public one-time link: rate an event after it ended.
    path: 'feedback/:token',
    loadComponent: () =>
      import('./pages/event-feedback/event-feedback').then((m) => m.EventFeedbackComponent),
  },
  {
    path: 'r/:token',
    loadComponent: () =>
      import('./pages/registration-edit/registration-edit').then(
        (m) => m.RegistrationEditComponent,
      ),
  },
  {
    // Module launcher: role-based cards for every area the user can open.
    path: 'home',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/home/home').then((m) => m.HomeComponent),
  },
  {
    path: 'admin/dashboard',
    canActivate: [menuGuard('dashboard'), adminGuard],
    loadComponent: () =>
      import('./pages/admin/dashboard/dashboard').then((m) => m.DashboardComponent),
  },
  {
    path: 'events',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./pages/events-browse/events-browse').then((m) => m.EventsBrowseComponent),
  },
  {
    // Event calendar (merchants only scan meal stubs).
    path: 'calendar',
    canActivate: [menuGuard('calendar'), nonMerchantGuard],
    loadComponent: () => import('./pages/calendar/calendar').then((m) => m.CalendarComponent),
  },
  {
    // Mail Planner: send plans for admins, reminders on received mails for everyone.
    path: 'mail-planner',
    canActivate: [menuGuard('mailPlanner'), nonMerchantGuard],
    loadComponent: () =>
      import('./pages/mail-planner/mail-planner').then((m) => m.MailPlannerComponent),
  },
  {
    path: 'my-events',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/my-events/my-events').then((m) => m.MyEventsComponent),
  },
  {
    path: 'account',
    canActivate: [nonMerchantGuard],
    loadComponent: () => import('./pages/account/account').then((m) => m.AccountComponent),
  },
  {
    path: 'admin/users',
    canActivate: [menuGuard('users'), adminGuard],
    loadComponent: () => import('./pages/admin/users/users').then((m) => m.UsersComponent),
  },
  {
    path: 'admin/teams',
    canActivate: [menuGuard('teams'), adminGuard],
    loadComponent: () => import('./pages/admin/teams/teams').then((m) => m.TeamsComponent),
  },
  {
    // Captain: view + manage their team. Participant: view the team they belong to (read-only).
    path: 'team',
    canActivate: [menuGuard('myTeam'), participantOrCaptainGuard],
    loadComponent: () => import('./pages/team/team').then((m) => m.TeamComponent),
  },
  {
    // Prototype: golf equipment voucher for a Philippine Airlines booking reference.
    path: 'voucher',
    canActivate: [menuGuard('voucher'), voucherGuard],
    loadComponent: () => import('./pages/voucher/voucher').then((m) => m.VoucherComponent),
  },
  {
    path: 'admin/settings',
    canActivate: [adminGuard],
    loadComponent: () => import('./pages/admin/settings/settings').then((m) => m.SettingsComponent),
  },
  {
    path: 'admin/events',
    canActivate: [confirmerGuard],
    loadComponent: () =>
      import('./pages/admin/events-list/events-list').then((m) => m.EventsListComponent),
  },
  {
    path: 'admin/events/new',
    canActivate: [adminGuard],
    loadComponent: () =>
      import('./pages/admin/event-form/event-form').then((m) => m.EventFormComponent),
  },
  {
    path: 'admin/events/:id/edit',
    canActivate: [adminGuard],
    loadComponent: () =>
      import('./pages/admin/event-form/event-form').then((m) => m.EventFormComponent),
  },
  {
    path: 'admin/events/:id',
    canActivate: [confirmerGuard],
    loadComponent: () =>
      import('./pages/admin/event-detail/event-detail').then((m) => m.EventDetailComponent),
  },
  {
    // Door TV for camera check-in through the face recognition engine.
    path: 'scanner/:eventId/tv',
    canActivate: [adminGuard],
    loadComponent: () => import('./pages/face-tv/face-tv').then((m) => m.FaceTvComponent),
  },
  {
    path: 'scanner/:eventId',
    canActivate: [adminGuard],
    loadComponent: () => import('./pages/scanner/scanner').then((m) => m.ScannerComponent),
  },
  {
    path: 'merchant/scan',
    canActivate: [menuGuard('mealScanner'), merchantGuard],
    loadComponent: () =>
      import('./pages/merchant/meal-scanner').then((m) => m.MealScannerComponent),
  },
  {
    // Airline baggage vouchers (golf equipment), Participant and Captain only.
    path: 'voucher',
    canActivate: [menuGuard('voucher'), participantOrCaptainGuard],
    loadComponent: () => import('./pages/voucher/voucher').then((m) => m.VoucherComponent),
  },
  { path: '**', redirectTo: 'login' },
];
