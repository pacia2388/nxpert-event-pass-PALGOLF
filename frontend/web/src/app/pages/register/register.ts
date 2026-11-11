import { ChangeDetectorRef, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink, ActivatedRoute } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ToastService } from '../../core/toast.service';
import { AuthService, RegisterBody } from '../../core/auth.service';
import { ApiService } from '../../core/api.service';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { BIRTHDATE_MIN, birthdateMax, isValidBirthdate, toIsoDate } from '../../shared/birthdate';

@Component({
  selector: 'app-register',
  imports: [
    FormsModule,
    RouterLink,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatDatepickerModule,
    MatButtonModule,
    MatIconModule,
    MatProgressBarModule,
  ],
  templateUrl: './register.html',
})
export class RegisterComponent implements OnInit {
  private auth = inject(AuthService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private toast = inject(ToastService);
  private cdr = inject(ChangeDetectorRef);
  private api = inject(ApiService);

  /** Token from a team registration link (?team=): completes the account the Team Lead created. */
  readonly teamToken = this.route.snapshot.queryParamMap.get('team');
  /** Team link state: loading the pre-fill, ready, or the link is not usable. */
  teamState = signal<'loading' | 'ready' | 'invalid'>('loading');
  teamError = signal('');

  firstName = '';
  middleInitial = '';
  lastName = '';
  senateNo = '';
  level = '';
  email = '';
  password = '';
  phone = '';
  organization = '';
  birthdate: Date | null = null;
  readonly birthdateMin = BIRTHDATE_MIN;
  readonly birthdateMax = birthdateMax();
  photo: string | null = null;
  photoError = signal<string | null>(null);
  loading = signal(false);

  ngOnInit(): void {
    if (!this.teamToken) return;
    this.api.getTeamRegistration(this.teamToken).subscribe({
      next: (p) => {
        this.firstName = p.firstName;
        this.middleInitial = p.middleInitial ?? '';
        this.lastName = p.lastName;
        this.email = p.email;
        this.teamState.set('ready');
      },
      error: (err) => {
        this.teamError.set(err?.error?.message ?? 'This registration link is invalid, expired or already used.');
        this.teamState.set('invalid');
      },
    });
  }

  // Read the chosen photo, downscale it (max 512px) and store it as a compact JPEG data URL.
  onPhotoSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = ''; // allow re-selecting the same file later
    if (!file) return;

    this.photoError.set(null);
    if (!file.type.startsWith('image/')) {
      this.photoError.set('Please choose an image file.');
      return;
    }
    if (file.size > 8 * 1024 * 1024) {
      this.photoError.set('Image is too large (max 8 MB before compression).');
      return;
    }

    const reader = new FileReader();
    reader.onload = () => {
      const img = new Image();
      img.onload = () => {
        const maxDim = 512;
        const scale = Math.min(1, maxDim / Math.max(img.width, img.height));
        const w = Math.round(img.width * scale);
        const h = Math.round(img.height * scale);
        const canvas = document.createElement('canvas');
        canvas.width = w;
        canvas.height = h;
        canvas.getContext('2d')!.drawImage(img, 0, 0, w, h);
        this.photo = canvas.toDataURL('image/jpeg', 0.85);
        this.cdr.detectChanges();
      };
      img.onerror = () => {
        this.photoError.set('Could not read that image.');
        this.cdr.detectChanges();
      };
      img.src = reader.result as string;
    };
    reader.readAsDataURL(file);
  }

  removePhoto(): void {
    this.photo = null;
    this.photoError.set(null);
  }

  submit(): void {
    if (!isValidBirthdate(this.birthdate)) {
      this.toast.warning('Please enter a valid birthdate (not in the future).');
      return;
    }
    this.loading.set(true);
    const body: RegisterBody = {
        firstName: this.firstName.trim(),
        middleInitial: this.middleInitial.trim() || undefined,
        lastName: this.lastName.trim(),
        senateNo: this.senateNo.trim(),
        level: this.level,
        email: this.email.trim(),
        password: this.password,
        phone: this.phone || undefined,
        organization: this.organization || undefined,
        photo: this.photo,
        birthdate: toIsoDate(this.birthdate)!,
      };
    (this.teamToken ? this.auth.completeTeamRegistration(this.teamToken, body) : this.auth.register(body))
      .subscribe({
        next: () => {
          this.loading.set(false);
          // Demo: activate immediately (no email verification). Fake the confirmation.
          this.toast.success(
            this.teamToken ? 'Registration complete.' : 'Account created — confirmation email sent (demo).',
          );
          const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
          this.router.navigateByUrl(returnUrl ?? '/my-events');
        },
        error: (err) => {
          this.loading.set(false);
          this.toast.error(err?.error?.message ?? 'Registration failed. Please try again.');
        },
      });
  }
}
