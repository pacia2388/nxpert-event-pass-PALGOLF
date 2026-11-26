import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../../core/auth.service';
import { homeFor } from '../../core/guards';
import { SetPasswordInfo } from '../../core/models';
import { ToastService } from '../../core/toast.service';
import { PasswordRequirementsComponent } from '../../shared/password-requirements/password-requirements';
import { isValidPassword } from '../../shared/password';
import { capsLockOn } from '../../shared/caps-lock';

// Public page opened from a one-time "set your password" link (admin-created / imported account,
// captain-added member, or forgot password). Setting the password signs the user in.
@Component({
  selector: 'app-set-password',
  imports: [
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    PasswordRequirementsComponent,
  ],
  templateUrl: './set-password.html',
  styleUrl: './set-password.scss',
})
export class SetPasswordComponent implements OnInit {
  private auth = inject(AuthService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private toast = inject(ToastService);

  private token = '';
  loading = signal(true);
  info = signal<SetPasswordInfo | null>(null);
  password = '';
  confirm = '';
  showPassword = signal(false);
  saving = signal(false);
  /** The password field with focus; the Caps Lock warning shows under it. */
  focusedField = signal<'password' | 'confirm' | null>(null);
  /** Caps Lock state of the page (key events only). */
  protected readonly capsLockOn = capsLockOn;

  ngOnInit(): void {
    this.token = this.route.snapshot.paramMap.get('token') ?? '';
    if (!this.token) {
      this.loading.set(false);
      return;
    }
    this.auth.setPasswordInfo(this.token).subscribe({
      next: (info) => {
        this.info.set(info);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  get passwordOk(): boolean {
    const email = this.info()?.email ?? null;
    return isValidPassword(this.password, email);
  }

  get canSubmit(): boolean {
    return (
      !this.saving() &&
      this.passwordOk &&
      this.password.length > 0 &&
      this.password === this.confirm
    );
  }

  submit(): void {
    if (!this.canSubmit) return;
    this.saving.set(true);
    this.auth.setPassword(this.token, this.password).subscribe({
      next: () => {
        this.saving.set(false);
        this.toast.success('Password set. You are signed in.');
        this.router.navigate([homeFor(this.auth)]);
      },
      error: (err) => {
        this.saving.set(false);
        this.toast.error(err?.error?.message ?? 'Could not set the password. Please try again.');
      },
    });
  }
}
