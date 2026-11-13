import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ApiService } from './api.service';
import { CountryCodeInfo, PhoneCountry } from './models';
import { isCompleteStoredPhone, phoneSupported } from '../shared/phone';

/**
 * The admin-managed Phone country table: which country codes the mobile channels may send to.
 * Its distinct codes are the choices in the user phone control. Loaded once per app; the
 * Settings page calls reload() after a change.
 */
@Injectable({ providedIn: 'root' })
export class PhoneCountryService {
  private readonly api = inject(ApiService);
  private readonly http = inject(HttpClient);
  readonly rows = signal<PhoneCountry[]>([]);
  /** Every known country code with its country, from public/country-codes.json (dev-edited). */
  readonly catalog = signal<CountryCodeInfo[]>([]);
  private readonly names = computed(() => new Map(this.catalog().map((c) => [c.code, c.country])));
  readonly loaded = signal(false);

  /** Country codes, "63" first, then numeric order. */
  readonly codes = computed(() =>
    this.rows().map((r) => r.countryCode).sort((a, b) =>
      a === '63' ? -1 : b === '63' ? 1 : Number(a) - Number(b)),
  );

  constructor() {
    this.reload();
    this.http.get<CountryCodeInfo[]>('country-codes.json').subscribe({ next: (c) => this.catalog.set(c), error: () => {} });
  }

  /** Country for a code, e.g. "63" gives "Philippines"; "" when the code is not in the JSON. */
  countryName(code: string): string {
    return this.names().get(code) ?? '';
  }

  reload(): void {
    this.api.getPhoneCountries().subscribe({ next: (r) => { this.rows.set(r); this.loaded.set(true); }, error: () => {} });
  }

  /** False when a stored number's country code is not in the table (blank counts as fine). */
  supports(phone: string | null | undefined): boolean {
    return !this.loaded() || phoneSupported(phone, this.codes());
  }

  /** True when a phone-field value is a full number whose code is in the table. */
  isValid(phone: string | null | undefined): boolean {
    return !!phone && isCompleteStoredPhone(phone) && this.supports(phone);
  }
}
