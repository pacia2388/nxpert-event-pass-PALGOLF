import { Directive, OnChanges, forwardRef, input } from '@angular/core';
import { AbstractControl, NG_VALIDATORS, ValidationErrors, Validator } from '@angular/forms';
import { isValidPassword } from './password';

// Marks a new-password field invalid ({ passwordRule: true }) when its value breaks the password
// rule; the bound value is the user's email, for the "not your email" rule. An empty field is
// left to `required`.
@Directive({
  selector: '[appPasswordRule]',
  providers: [
    { provide: NG_VALIDATORS, useExisting: forwardRef(() => PasswordRuleDirective), multi: true },
  ],
})
export class PasswordRuleDirective implements Validator, OnChanges {
  readonly email = input<string | null | undefined>(undefined, { alias: 'appPasswordRule' });
  private onChange?: () => void;

  // Re-check the field when the email changes.
  ngOnChanges(): void {
    this.onChange?.();
  }

  validate(control: AbstractControl): ValidationErrors | null {
    const value: string = control.value ?? '';
    return !value || isValidPassword(value, this.email()) ? null : { passwordRule: true };
  }

  registerOnValidatorChange(fn: () => void): void {
    this.onChange = fn;
  }
}
