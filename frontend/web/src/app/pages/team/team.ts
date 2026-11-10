import { Component, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule, NgForm } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { TeamDetailDto } from '../../core/models';

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

// The Team Lead's only page: their team, its members, and a form to add a Participant by name
// and email. An email that is not in the system yet creates the account.
@Component({
  selector: 'app-team',
  imports: [
    DatePipe,
    FormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatProgressBarModule,
  ],
  templateUrl: './team.html',
  styleUrl: './team.scss',
})
export class TeamComponent implements OnInit {
  private api = inject(ApiService);
  private toast = inject(ToastService);

  private readonly form = viewChild(NgForm);

  loading = signal(true);
  saving = signal(false);
  /** null = no team has been assigned to this Team Lead yet. */
  team = signal<TeamDetailDto | null>(null);

  readonly isFull = computed(() => {
    const t = this.team();
    return !!t && t.memberCount >= t.maxMembers;
  });

  firstName = '';
  middleInitial = '';
  lastName = '';
  email = '';

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api.getMyTeam().subscribe({
      next: (t) => {
        this.team.set(t);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  get valid(): boolean {
    return !!this.firstName.trim() && !!this.lastName.trim() && EMAIL_RE.test(this.email.trim());
  }

  add(): void {
    if (this.saving() || this.isFull()) return;
    if (!this.valid) {
      this.form()?.form.markAllAsTouched();
      this.toast.warning('Enter a first name, last name and a valid email.');
      return;
    }
    this.saving.set(true);
    this.api
      .addMyTeamMember({
        firstName: this.firstName.trim(),
        middleInitial: this.middleInitial.trim() || null,
        lastName: this.lastName.trim(),
        email: this.email.trim(),
      })
      .subscribe({
        next: (res) => {
          this.saving.set(false);
          this.team.update((t) =>
            t
              ? {
                  ...t,
                  memberCount: t.memberCount + 1,
                  members: [...t.members, res.member].sort((a, b) => a.fullName.localeCompare(b.fullName)),
                }
              : t,
          );
          this.form()?.resetForm({ firstName: '', middleInitial: '', lastName: '', email: '' });
          this.toast.success(
            res.created
              ? `Account created for ${res.member.fullName} and added to the team.`
              : `${res.member.fullName} already had an account and was added to the team.`,
          );
        },
        error: (err) => {
          this.saving.set(false);
          this.toast.error(err?.error?.message ?? 'Could not add this member.');
        },
      });
  }
}
