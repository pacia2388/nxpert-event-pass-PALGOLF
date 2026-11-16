import { Injectable, inject } from '@angular/core';
import { AuthService } from './auth.service';
import { ConfirmService } from './confirm.service';
import { EventCategory, categoryTeamWarning } from './event-category';

/**
 * Senior / Regular events: warns a player whose team is in the other category before registering.
 * Used by every Register button (event page, Browse Events card and details dialog).
 */
@Injectable({ providedIn: 'root' })
export class CategoryWarningService {
  private auth = inject(AuthService);
  private confirmer = inject(ConfirmService);

  /**
   * Resolves true to go ahead. No event category or no team → true without a dialog. The team is
   * looked up on each call, so a sign-in or team change after the page loaded is seen.
   */
  async confirm(category: EventCategory | null | undefined): Promise<boolean> {
    if (!category) return true;
    const message = categoryTeamWarning(category, await this.auth.myTeamCategory());
    if (!message) return true;
    return this.confirmer.confirm({
      title: 'Different category',
      message: `${message} Register anyway?`,
      confirmText: 'Register anyway',
      tone: 'warning',
    });
  }
}

/** Router navigation state set when the player already accepted the warning on Browse Events. */
export const CATEGORY_CONFIRMED_STATE = 'categoryConfirmed';
