import { Component, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule, NgForm } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { ConfirmService } from '../../core/confirm.service';
import { AuthService } from '../../core/auth.service';
import { TeamDetailDto, TeamMemberDto } from '../../core/models';

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

// The Captain's only page: their team, its members, and a form to add a Participant by name
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
    MatCheckboxModule,
  ],
  templateUrl: './team.html',
  styleUrl: './team.scss',
})
export class TeamComponent implements OnInit {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  private confirmer = inject(ConfirmService);
  private auth = inject(AuthService);

  /** Captains manage their team; Participants only view the team they belong to. */
  readonly canEdit = this.auth.isCaptain;

  private readonly form = viewChild(NgForm);

  loading = signal(true);
  saving = signal(false);
  /** null = no team has been assigned to this Captain yet. */
  team = signal<TeamDetailDto | null>(null);

  sending = signal(false);
  /** userIds checked to receive the registration notice. Only unregistered members can be checked. */
  selected = signal<ReadonlySet<number>>(new Set());

  /** Members who can still get the notice (account not completed yet). */
  readonly pending = computed(() => this.team()?.members.filter((m) => !m.registered) ?? []);
  readonly allChecked = computed(() => {
    const p = this.pending();
    return p.length > 0 && p.every((m) => this.selected().has(m.userId));
  });
  readonly someChecked = computed(() => this.selected().size > 0 && !this.allChecked());

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
        this.resetSelection();
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  /** Default: unregistered members not notified yet. */
  private resetSelection(): void {
    this.selected.set(new Set(this.pending().filter((m) => !m.notificationFlag).map((m) => m.userId)));
  }

  isChecked(m: TeamMemberDto): boolean {
    return this.selected().has(m.userId);
  }

  toggle(m: TeamMemberDto, checked: boolean): void {
    const next = new Set(this.selected());
    if (checked) next.add(m.userId);
    else next.delete(m.userId);
    this.selected.set(next);
  }

  toggleAll(checked: boolean): void {
    this.selected.set(new Set(checked ? this.pending().map((m) => m.userId) : []));
  }

  async sendNotice(): Promise<void> {
    const ids = [...this.selected()];
    if (!ids.length || this.sending()) return;
    const ok = await this.confirmer.confirm({
      title: 'Send registration notice?',
      message: `Email a registration link to ${ids.length} member${ids.length === 1 ? '' : 's'}. Any earlier link they received stops working.`,
      confirmText: 'Send',
    });
    if (!ok) return;
    this.sending.set(true);
    this.api.sendTeamRegistrationNotice(ids).subscribe({
      next: (res) => {
        this.sending.set(false);
        this.team.update((t) => (t ? { ...t, members: res.members } : t));
        this.resetSelection();
        const msg = `Registration notice sent to ${res.sent} member${res.sent === 1 ? '' : 's'}.`;
        if (res.failed) this.toast.warning(`${msg} ${res.failed} could not be sent; try again.`);
        else this.toast.success(msg);
      },
      error: (err) => {
        this.sending.set(false);
        this.toast.error(err?.error?.message ?? 'Could not send the registration notice.');
      },
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
          if (!res.member.registered) this.toggle(res.member, true);
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
