import { Component, DestroyRef, inject, signal } from '@angular/core';
import { Meta } from '@angular/platform-browser';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink, ActivatedRoute } from '@angular/router';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../../core/auth.service';
import { homeFor } from '../../core/guards';
import { ToastService } from '../../core/toast.service';

// The top of the page's sky, for the phone toolbar. Same value as --pg-sky in styles.scss.
const SKY = '#1a5aa8';

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

  constructor() {
    // Phones tint the browser toolbar from this tag. Match the page's sky while login is open,
    // then hand back the app's own colour.
    const meta = inject(Meta);
    const appColor = meta.getTag('name="theme-color"')?.content;
    meta.updateTag({ name: 'theme-color', content: SKY });
    inject(DestroyRef).onDestroy(() => {
      if (appColor) meta.updateTag({ name: 'theme-color', content: appColor });
    });
  }

  email = '';
  password = '';
  loading = signal(false);
  showPassword = signal(false);

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
