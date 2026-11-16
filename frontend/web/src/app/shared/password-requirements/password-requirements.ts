import { Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { passwordChecks } from '../password';

// The password rule (PasswordRules on the API) as a checklist under a new-password field; each
// line ticks as the typed password meets it. The email line shows only when the email is known.
@Component({
  selector: 'app-password-requirements',
  imports: [MatIconModule],
  template: `
    <p class="title">Your password needs:</p>
    <ul>
      @for (rule of rules(); track rule.text) {
        <li [class.met]="rule.met">
          <mat-icon aria-hidden="true">{{
            rule.met ? 'check_circle' : 'radio_button_unchecked'
          }}</mat-icon>
          {{ rule.text }}
        </li>
      }
    </ul>
  `,
  styles: `
    :host {
      display: block;
      margin: 0 0 16px;
      padding: 10px 12px;
      border-radius: 8px;
      background: #f0f5fc;
      font-size: 13px;
      color: var(--pg-ink-soft);
    }
    .title {
      margin: 0 0 4px;
      font-weight: 500;
    }
    ul {
      margin: 0;
      padding: 0;
      list-style: none;
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
      gap: 2px 12px;
    }
    li {
      display: flex;
      align-items: center;
      gap: 6px;
    }
    li.met {
      color: #15803d;
    }
    mat-icon {
      font-size: 16px;
      width: 16px;
      height: 16px;
      flex: none;
    }
  `,
})
export class PasswordRequirementsComponent {
  readonly password = input('');
  readonly email = input<string | null>();

  protected readonly rules = computed(() => passwordChecks(this.password() ?? '', this.email()));
}
