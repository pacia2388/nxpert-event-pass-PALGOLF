import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from './api.service';

/**
 * Names the mobile notification channel after the service the admin chose in Settings: the stored
 * channel value stays "Viber", but screens show "Mobile (Viber)" or "Mobile (SMS)".
 * Loaded once per app; falls back to Viber when the lookup fails.
 */
@Injectable({ providedIn: 'root' })
export class MobileChannelService {
  private readonly api = inject(ApiService);
  private readonly sms = signal(false);

  /** "Viber" or "SMS". */
  readonly name = computed(() => (this.sms() ? 'SMS' : 'Viber'));
  /** "Mobile (Viber)" or "Mobile (SMS)" — the channel option label. */
  readonly label = computed(() => `Mobile (${this.name()})`);

  constructor() {
    this.api.getMobileChannel().subscribe({
      next: (r) => this.sms.set(r.mobileProvider === 'Sms'),
      error: () => {},
    });
  }
}
