import { Component, DestroyRef, inject, signal } from '@angular/core';
import { Meta } from '@angular/platform-browser';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink, ActivatedRoute } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../../core/auth.service';
import { homeFor } from '../../core/guards';
import { ToastService } from '../../core/toast.service';
import { ACTIVE_THEME } from '../../core/theme';
import { capsLockOn } from '../../shared/caps-lock';
import { ForgotPasswordDialogComponent, ForgotPasswordData } from './forgot-password-dialog';

@Component({
  selector: 'app-login',
  imports: [
    FormsModule,
    RouterLink,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
  ],
  templateUrl: './login.html',
  styleUrl: './login.scss',
})
export class LoginComponent {
  private auth = inject(AuthService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private toast = inject(ToastService);
  private dialog = inject(MatDialog);

  /** The theme's login image and the words that go with it. */
  protected readonly poster = ACTIVE_THEME.current;
  /** "Label: value" lines show the value emphasised, like the poster's dates. */
  protected readonly posterLines = this.poster.login.lines.map((line) => {
    const at = line.indexOf(': ');
    return at < 0
      ? { label: line, value: '' }
      : { label: line.slice(0, at), value: line.slice(at + 2) };
  });

  constructor() {
    // Phones tint the browser toolbar from this tag. Match the page's sky while login is open,
    // then hand back the app's own colour.
    const meta = inject(Meta);
    const appColor = meta.getTag('name="theme-color"')?.content;
    // The top of the page's sky (the theme's --pg-sky).
    meta.updateTag({ name: 'theme-color', content: ACTIVE_THEME.current.colors.sky });
    inject(DestroyRef).onDestroy(() => {
      if (appColor) meta.updateTag({ name: 'theme-color', content: appColor });
    });
  }

  email = this.route.snapshot.queryParamMap.get('email') ?? '';
  password = '';
  loading = signal(false);
  showPassword = signal(false);
  pwdFocused = signal(false);
  /** Caps Lock state of the page (key events only), shown while the password field has focus. */
  protected readonly capsLockOn = capsLockOn;

  // A registered invitee lands here with their address already filled in.
  forgotPassword(): void {
    this.dialog.open<ForgotPasswordDialogComponent, ForgotPasswordData>(ForgotPasswordDialogComponent, {
      data: { email: this.email.trim() },
      width: '420px',
      maxWidth: '92vw',
      autoFocus: false,
    });
  }

  submit(): void {
    this.loading.set(true);
    this.auth.login(this.email.trim(), this.password).subscribe({
      next: () => {
        this.loading.set(false);
        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
        if (returnUrl) {
          this.router.navigateByUrl(returnUrl);
        } else {
          this.router.navigate([homeFor(this.auth)]);
        }
      },
      error: (err) => {
        this.loading.set(false);
        this.toast.error(err?.error?.message ?? 'Login failed. Please try again.');
      },
    });
  }
}
