import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ApiService } from '../../core/api.service';
import { PublicFeedback } from '../../core/models';

// Public page opened from the one-time feedback link sent after an event. No sign-in; the token
// identifies the recipient and keeps one answer per person (a second submit updates it).
@Component({
  selector: 'app-event-feedback',
  imports: [
    DatePipe,
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './event-feedback.html',
  styleUrl: './event-feedback.scss',
})
export class EventFeedbackComponent implements OnInit {
  private api = inject(ApiService);
  private route = inject(ActivatedRoute);

  readonly stars = [1, 2, 3, 4, 5];
  token = '';
  loading = signal(true);
  info = signal<PublicFeedback | null>(null);
  rating = signal(0);
  hover = signal(0);
  organizationRating = signal(0);
  comment = '';
  wentWell = '';
  improve = '';
  anonymous = false;
  saving = signal(false);
  done = signal(false);
  error = signal<string | null>(null);

  ngOnInit(): void {
    this.token = this.route.snapshot.paramMap.get('token') ?? '';
    if (!this.token) {
      this.loading.set(false);
      return;
    }
    this.api.publicFeedback(this.token).subscribe({
      next: (info) => {
        this.info.set(info);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  submit(): void {
    if (this.rating() < 1 || this.saving()) return;
    this.saving.set(true);
    this.error.set(null);
    this.api
      .submitPublicFeedback(this.token, {
        rating: this.rating(),
        comment: this.comment.trim() || null,
        isAnonymous: this.anonymous,
        organizationRating: this.organizationRating() || null,
        wentWell: this.wentWell.trim() || null,
        improve: this.improve.trim() || null,
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.done.set(true);
        },
        error: (err) => {
          this.saving.set(false);
          this.error.set(err?.error?.message ?? 'Could not send your feedback. Please try again.');
        },
      });
  }
}
