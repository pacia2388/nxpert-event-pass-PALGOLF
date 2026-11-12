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
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, catchError, debounceTime, forkJoin, map, of, switchMap } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { ConfirmService } from '../../core/confirm.service';
import { AuthService } from '../../core/auth.service';
import { TeamCandidate, TeamDetailDto, TeamMemberDto } from '../../core/models';

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

// The Captain's only page: their team, its members, and a form to add a Participant by email
// and name. Typing an email suggests existing Participants (picking one fills the names); an
// email that is not in the system yet creates the account.
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
    MatTooltipModule,
    MatAutocompleteModule,
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
  /**
   * userIds of the checked members. One selection drives both header actions: Remove acts on all
   * of them, the registration notice on the unregistered ones only. Nothing starts checked, so a
   * removal is always a deliberate pick.
   */
  selected = signal<ReadonlySet<number>>(new Set());

  /** Members who can still get the notice (account not completed yet). */
  readonly pending = computed(() => this.team()?.members.filter((m) => !m.registered) ?? []);
  /** Checked members the notice goes to (registered members are exempt). */
  readonly noticeIds = computed(() => this.pending().filter((m) => this.selected().has(m.userId)).map((m) => m.userId));
  readonly allChecked = computed(() => {
    const ms = this.team()?.members ?? [];
    return ms.length > 0 && ms.every((m) => this.selected().has(m.userId));
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
        this.clearSelection();
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  private clearSelection(): void {
    this.selected.set(new Set());
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
    this.selected.set(new Set(checked ? (this.team()?.members ?? []).map((m) => m.userId) : []));
  }

  async sendNotice(): Promise<void> {
    const ids = this.noticeIds();
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
        this.clearSelection();
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

  removing = signal(false);

  /** Remove every checked member (one request each); members that fail stay checked. */
  async removeSelected(): Promise<void> {
    const members = (this.team()?.members ?? []).filter((m) => this.selected().has(m.userId));
    if (!members.length || this.removing()) return;
    const one = members.length === 1;
    const ok = await this.confirmer.confirm({
      title: one ? 'Remove member?' : `Remove ${members.length} members?`,
      message: `${one ? members[0].fullName : `${members.length} members`} will be removed from your team. Their accounts are kept, and any registration link they received stops working.`,
      confirmText: 'Remove',
      tone: 'warning',
    });
    if (!ok) return;
    this.removing.set(true);
    forkJoin(
      members.map((m) =>
        this.api.removeMyTeamMember(m.userId).pipe(
          map(() => true),
          // 404 = no longer a member (removed elsewhere meanwhile), so it is gone either way.
          catchError((err) => of(err?.status === 404)),
        ),
      ),
    ).subscribe((results) => {
      this.removing.set(false);
      const gone = new Set(members.filter((_, i) => results[i]).map((m) => m.userId));
      this.team.update((t) =>
        t ? { ...t, memberCount: t.memberCount - gone.size, members: t.members.filter((x) => !gone.has(x.userId)) } : t,
      );
      this.selected.set(new Set([...this.selected()].filter((id) => !gone.has(id))));
      const failed = members.length - gone.size;
      const msg = one && !failed ? `${members[0].fullName} removed from the team.` : `${gone.size} removed from the team.`;
      if (!failed) this.toast.success(msg);
      else if (gone.size) this.toast.warning(`${msg} ${failed} could not be removed; they are still selected.`);
      else this.toast.error(one ? 'Could not remove this member.' : 'Could not remove these members.');
    });
  }

  // ---- Email lookup: suggest existing Participants (in no team) as the Captain types ----

  /** Suggestions for the current email text. */
  candidates = signal<TeamCandidate[]>([]);
  /** The suggestion the Captain picked, while the email still matches it. */
  picked = signal<TeamCandidate | null>(null);
  private readonly emailTyped = new Subject<string>();

  /** The input shows the email when a suggestion is chosen (the option value is the object). */
  readonly candidateEmail = (v: TeamCandidate | string | null): string =>
    typeof v === 'string' ? v : (v?.email ?? '');

  constructor() {
    this.emailTyped
      .pipe(
        map((v) => v.trim().toLowerCase()),
        debounceTime(250),
        // No distinctUntilChanged: after a pick or an add clears the list, typing the same text
        // again must fetch again. The server decides the minimum length.
        switchMap((q) =>
          !q
            ? of([] as TeamCandidate[])
            : this.api.findTeamCandidates(q).pipe(catchError(() => of([] as TeamCandidate[]))),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((list) => this.candidates.set(list));
  }

  onEmailChange(value: TeamCandidate | string | null): void {
    if (value !== null && typeof value === 'object') return; // handled by pick()
    const text = value ?? '';
    const p = this.picked();
    if (p && text.trim().toLowerCase() !== p.email) this.picked.set(null);
    this.emailTyped.next(text);
  }

  /** Fill the form from an existing Participant. */
  pick(c: TeamCandidate): void {
    this.email = c.email;
    this.firstName = c.firstName;
    this.middleInitial = c.middleInitial ?? '';
    this.lastName = c.lastName;
    this.picked.set(c);
    this.candidates.set([]);
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
          this.picked.set(null);
          this.candidates.set([]);
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
