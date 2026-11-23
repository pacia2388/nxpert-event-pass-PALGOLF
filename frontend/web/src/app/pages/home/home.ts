import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../../core/auth.service';
import { MenuId, menuEnabled } from '../../core/config';

type ModuleCategory = 'Events' | 'Operations' | 'Admin' | 'Account';

interface AppModule {
  title: string;
  subtitle: string;
  icon: string;
  link: string;
  category: ModuleCategory;
  /** The menu.json switch for this tile; none = always on. */
  menu?: MenuId;
}

// Tab order on the launcher; only categories the user actually has modules in are shown.
const CATEGORY_ORDER: ModuleCategory[] = ['Events', 'Operations', 'Admin', 'Account'];

@Component({
  selector: 'app-home',
  imports: [RouterLink, MatIconModule],
  templateUrl: './home.html',
  styleUrl: './home.scss',
})
export class HomeComponent {
  private readonly auth = inject(AuthService);

  readonly filter = signal<'All' | ModuleCategory>('All');

  // Modules available to the signed-in user, without the menus menu.json switched off.
  readonly modules = computed<AppModule[]>(() =>
    this.byRole().filter((m) => !m.menu || menuEnabled(m.menu)),
  );

  // Modules for the signed-in user's role, mirroring the role-based navigation.
  private byRole(): AppModule[] {
    if (this.auth.isAdmin()) {
      return [
        {
          title: 'Dashboard',
          subtitle: 'Insights & KPI',
          icon: 'insights',
          link: '/admin/dashboard',
          menu: 'dashboard',
          category: 'Operations',
        },
        {
          title: 'Events',
          subtitle: 'Manage & Schedule',
          icon: 'event',
          link: '/admin/events',
          category: 'Events',
        },
        {
          title: 'Calendar',
          subtitle: 'Event Dates & Invitations',
          icon: 'calendar_month',
          link: '/calendar',
          menu: 'calendar',
          category: 'Events',
        },
        {
          title: 'Mail Planner',
          subtitle: 'Send Days & Reminders',
          icon: 'schedule_send',
          link: '/mail-planner',
          menu: 'mailPlanner',
          category: 'Events',
        },
        {
          title: 'Browse Events',
          subtitle: 'Open Registrations',
          icon: 'travel_explore',
          link: '/events',
          category: 'Events',
        },
        {
          title: 'My Events',
          subtitle: 'Passes & Tickets',
          icon: 'confirmation_number',
          link: '/my-events',
          category: 'Events',
        },
        {
          title: 'Meal Scanner',
          subtitle: 'Meal Stub Redemption',
          icon: 'restaurant',
          link: '/merchant/scan',
          menu: 'mealScanner',
          category: 'Operations',
        },
        {
          title: 'Users',
          subtitle: 'People & Access',
          icon: 'group',
          link: '/admin/users',
          menu: 'users',
          category: 'Admin',
        },
        {
          title: 'Teams',
          subtitle: 'Captains & Members',
          icon: 'groups',
          link: '/admin/teams',
          menu: 'teams',
          category: 'Admin',
        },
      ];
    }
    if (this.auth.isMerchant()) {
      return [
        {
          title: 'Meal Scanner',
          subtitle: 'Meal Stub Redemption',
          icon: 'restaurant',
          link: '/merchant/scan',
          menu: 'mealScanner',
          category: 'Operations',
        },
      ];
    }
    const participant: AppModule[] = [
      {
        title: 'Browse Events',
        subtitle: 'Open Registrations',
        icon: 'travel_explore',
        link: '/events',
        category: 'Events',
      },
      {
        title: 'My Events',
        subtitle: 'Passes & Tickets',
        icon: 'confirmation_number',
        link: '/my-events',
        category: 'Events',
      },
      {
        title: 'Calendar',
        subtitle: 'Event Dates & Invitations',
        icon: 'calendar_month',
        link: '/calendar',
        menu: 'calendar',
        category: 'Events',
      },
      // { title: 'Reminders', subtitle: 'Mail Reminder', icon: 'schedule_send', link: '/mail-planner', category: 'Events' },
      {
        title: 'My Team',
        subtitle: 'Team members',
        icon: 'group',
        link: '/team',
        menu: 'myTeam',
        category: 'Account',
      },
      {
        title: 'Voucher',
        subtitle: 'Airline Baggage',
        icon: 'luggage',
        link: '/voucher',
        menu: 'voucher',
        category: 'Events',
      },
    ];
    if (this.auth.canConfirm()) {
      return [
        {
          title: 'Confirm Registrations',
          subtitle: 'Review & Approve',
          icon: 'how_to_reg',
          link: '/admin/events',
          category: 'Operations',
        },
        ...participant,
      ];
    }
    return participant;
  }

  readonly categories = computed(() => {
    const present = new Set(this.modules().map((m) => m.category));
    return CATEGORY_ORDER.filter((c) => present.has(c));
  });

  readonly visible = computed(() => {
    const f = this.filter();
    return f === 'All' ? this.modules() : this.modules().filter((m) => m.category === f);
  });
}
