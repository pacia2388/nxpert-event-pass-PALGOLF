import { Component, effect, inject, input, model, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';
import { PhoneCountryService } from '../../core/phone-country.service';
import { formatPhoneNumber, isCompletePhone, joinPhone, splitPhone } from '../phone';

/**
 * Phone entry used by every form: a country-code dropdown (codes from the Phone country table)
 * plus the number. Two-way binds the stored value "+<code><number>" (null when blank).
 * PH (+63) keeps the mobile mask "9XX XXX XXXX". The parent checks validity with
 * PhoneCountryService.isValid(value).
 */
@Component({
  selector: 'app-phone-field',
  imports: [FormsModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatIconModule],
  templateUrl: './phone-field.html',
  styleUrl: './phone-field.scss',
})
export class PhoneFieldComponent {
  protected readonly countries = inject(PhoneCountryService);

  readonly value = model<string | null>(null);
  readonly required = input(false);
  readonly label = input('Mobile number');
  readonly hint = input<string | null>(null);
  readonly requiredMessage = input('A mobile number is required.');

  protected code = '';
  protected number = '';
  protected readonly touched = signal(false);
  private emitted: string | null | undefined = undefined;
  private edited = false;

  constructor() {
    // Split the value when the parent sets a new one, and again when the codes arrive.
    effect(() => {
      const v = this.value();
      this.countries.codes();
      untracked(() => {
        if (v !== this.emitted || !this.edited) this.split(v);
      });
    });
  }

  private split(v: string | null): void {
    ({ code: this.code, number: this.number } = splitPhone(v, this.countries.codes()));
    this.emitted = v;
    this.edited = false;
  }

  private emit(): void {
    const v = this.code ? joinPhone(this.code, this.number) : this.number ? '+' + this.number.replace(/\D/g, '') : null;
    this.emitted = v;
    this.edited = true;
    this.value.set(v);
  }

  protected onNumberInput(e: Event): void {
    const el = e.target as HTMLInputElement;
    this.number = formatPhoneNumber(this.code, el.value);
    el.value = this.number;
    this.emit();
  }

  protected onCodeChange(): void {
    this.number = formatPhoneNumber(this.code, this.number);
    this.emit();
  }

  /** Message under the field, or null when the entry is fine (or untouched and empty). */
  protected problem(): string | null {
    if (!this.number) return this.required() && this.touched() ? this.requiredMessage() : null;
    if (!this.code || !this.countries.supports(this.value()))
      return "Mobile messages can't be sent to this number. Pick a listed country code.";
    if (!isCompletePhone(this.code, this.number))
      return this.code === '63' ? 'Use a mobile number: 9XX XXX XXXX.' : 'Enter the full mobile number.';
    return null;
  }
}
