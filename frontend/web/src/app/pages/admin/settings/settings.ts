import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckbox } from '@angular/material/checkbox';
import { MatChipsModule, MatChipInputEvent } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { ApiService } from '../../../core/api.service';
import { ToastService } from '../../../core/toast.service';
import { AppSettingsDto, MobileProvider, PhoneCountry } from '../../../core/models';
import { PhoneCountryService } from '../../../core/phone-country.service';
import { ConfirmService } from '../../../core/confirm.service';
import { ACTIVE_THEME } from '../../../core/theme';

@Component({
  selector: 'app-settings',
  imports: [
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatCheckbox,
    MatButtonToggleModule,
    MatChipsModule,
    MatIconModule,
    MatButtonModule,
    MatProgressBarModule,
  ],
  templateUrl: './settings.html',
  styles: [
    `
      .settings-body {
        max-width: 640px;
      }
      .settings-card {
        overflow: hidden;
      }
      .section-sub {
        margin: 4px 0 16px;
        font-size: 13px;
      }
      .full-width {
        width: 100%;
      }
      .mobile-head {
        margin-top: 24px;
      }
      .mobile-current {
        display: flex;
        align-items: center;
        gap: 8px;
        margin: 0;
      }
      .mobile-note {
        margin-top: 12px;
        margin-bottom: 0;
      }
      .pc-table {
        width: 100%;
        border-collapse: collapse;
        margin-bottom: 12px;
      }
      .pc-table th,
      .pc-table td {
        text-align: left;
        padding: 4px 8px;
        border-bottom: 1px solid var(--mat-sys-outline-variant);
      }
      .pc-actions {
        text-align: right;
        width: 48px;
      }
      .pc-add {
        display: flex;
        flex-wrap: wrap;
        align-items: flex-start;
        gap: 12px;
      }
      .theme-current {
        display: flex;
        align-items: center;
        gap: 8px;
        margin: 0 0 12px;
      }
      .pc-code {
        width: 260px;
        /* Same height as the Add button (40px). */
        --mat-form-field-container-height: 40px;
        --mat-form-field-container-vertical-padding: 8px;
      }
    `,
  ],
})
export class SettingsComponent implements OnInit {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  private confirmer = inject(ConfirmService);
  protected countries = inject(PhoneCountryService);
  newCode = signal('');
  /** Codes from country-codes.json not yet in the table. */
  codeChoices = computed(() => {
    const taken = new Set(this.countries.rows().map((r) => r.countryCode));
    return this.countries.catalog().filter((c) => !taken.has(c.code));
  });

  loading = signal(true);
  saving = signal(false);
  ccEmails = signal<string[]>([]);
  inputError = signal<string | null>(null);
  // Which transport delivers phone notifications (entry pass, kit-claim, edit link).
  mobileProvider = signal<MobileProvider>('Viber');
  // Which services are set up on the server; the admin can choose only when both are.
  viberReady = signal(false);
  smsReady = signal(false);
  canSwitch = computed(() => this.viberReady() && this.smsReady());
  // Plain name of the channel in use, for the "currently used" note.
  providerName = computed(() => (this.mobileProvider() === 'Sms' ? 'SMS' : 'Viber'));
  // The channel in use is not set up, so phone messages go by email.
  providerDown = computed(() =>
    this.mobileProvider() === 'Sms' ? !this.smsReady() : !this.viberReady(),
  );

  // The look this deployment wears, from theme.json beside the API (or the default).
  protected readonly theme = ACTIVE_THEME.current;
  protected readonly themeSource =
    ACTIVE_THEME.current.source === 'file' ? 'theme.json on the server' : 'default, no theme.json';

  private static readonly EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

  ngOnInit(): void {
    this.api.getSettings().subscribe({
      next: (s) => {
        this.apply(s);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  private apply(s: AppSettingsDto): void {
    this.ccEmails.set(s.ccEmails ?? []);
    this.mobileProvider.set(s.mobileProvider ?? 'Viber');
    this.viberReady.set(!!s.viberReady);
    this.smsReady.set(!!s.smsReady);
  }

  // Add one or more addresses (the chip input may deliver a comma/space separated paste).
  add(event: MatChipInputEvent): void {
    const parts = (event.value ?? '')
      .split(/[,;\s]+/)
      .map((p) => p.trim())
      .filter(Boolean);
    let added = false;
    for (const p of parts) {
      if (!SettingsComponent.EMAIL_RE.test(p)) {
        this.inputError.set(`"${p}" is not a valid email address.`);
        continue;
      }
      if (this.ccEmails().some((e) => e.toLowerCase() === p.toLowerCase())) continue;
      this.ccEmails.update((list) => [...list, p]);
      added = true;
    }
    if (added) this.inputError.set(null);
    event.chipInput?.clear();
  }

  remove(email: string): void {
    this.ccEmails.update((list) => list.filter((e) => e !== email));
  }

  save(): void {
    this.saving.set(true);
    this.api.updateSettings(this.ccEmails(), this.mobileProvider()).subscribe({
      next: (s) => {
        this.apply(s);
        this.saving.set(false);
        this.toast.success('Settings saved.');
      },
      error: (err) => {
        this.saving.set(false);
        this.toast.error(err?.error?.message ?? 'Could not save settings.');
      },
    });
  }

  setChannels(r: PhoneCountry, viber: boolean, sms: boolean, box: MatCheckbox): void {
    this.api.setPhoneCountryChannels(r.id, viber, sms).subscribe({
      next: (saved) =>
        this.countries.rows.update((rows) => rows.map((x) => (x.id === saved.id ? saved : x))),
      error: (err) => {
        box.checked = !box.checked;
        this.toast.error(err?.error?.message ?? 'Could not save the channels.');
      },
    });
  }

  addCountry(): void {
    const code = this.newCode();
    if (!code) return;
    this.api.addPhoneCountry(code).subscribe({
      next: () => {
        this.newCode.set('');
        this.countries.reload();
        this.toast.success(`(+${code}) ${this.countries.countryName(code)} added.`);
      },
      error: (err) => this.toast.error(err?.error?.message ?? 'Could not add the country code.'),
    });
  }

  async removeCountry(r: PhoneCountry): Promise<void> {
    const ok = await this.confirmer.confirm({
      title: `Remove +${r.countryCode}?`,
      message: `Users can no longer save new +${r.countryCode} numbers, and messages to existing ones go by email. Users keep their numbers.`,
      confirmText: 'Remove',
      tone: 'error',
    });
    if (!ok) return;
    this.api.removePhoneCountry(r.id).subscribe({
      next: () => {
        this.countries.reload();
        this.toast.success(
          `(+${r.countryCode}) ${this.countries.countryName(r.countryCode)} removed.`,
        );
      },
      error: (err) => this.toast.error(err?.error?.message ?? 'Could not remove the country code.'),
    });
  }

  downloadThemeTemplate(): void {
    this.api.downloadThemeTemplate().subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = 'theme-template.json';
        a.click();
        URL.revokeObjectURL(url);
      },
      error: () => this.toast.error('Could not download the theme template.'),
    });
  }
}
