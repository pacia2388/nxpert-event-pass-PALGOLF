import { Component, ElementRef, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { FormsModule, NgForm } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { forkJoin } from 'rxjs';
import { ApiService } from '../../../core/api.service';
import { ConfirmService } from '../../../core/confirm.service';
import { ToastService } from '../../../core/toast.service';
import {
  TEAM_CATEGORIES,
  TeamCategory,
  TeamDetailDto,
  TeamDto,
  TeamMemberDto,
  UserDto,
} from '../../../core/models';

// Team master: list of teams on the left, the selected (or new) team's form and members on the
// right. Shares the User Master look (users.scss / user-form-panel.scss).
@Component({
  selector: 'app-teams',
  imports: [
    FormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    MatTooltipModule,
  ],
  templateUrl: './teams.html',
  styleUrls: ['../users/users.scss', '../users/user-form-panel.scss', './teams.scss'],
})
export class TeamsComponent implements OnInit {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  private confirmer = inject(ConfirmService);

  private readonly form = viewChild(NgForm);
  private readonly formPane = viewChild<ElementRef<HTMLElement>>('formPane');

  readonly categories = TEAM_CATEGORIES;

  loading = signal(true);
  saving = signal(false);
  teams = signal<TeamDto[]>([]);
  users = signal<UserDto[]>([]);
  search = signal('');

  /** The team shown in the form; null = a new, unsaved team. */
  selected = signal<TeamDetailDto | null>(null);

  description = '';
  category: TeamCategory = 'Regular';
  teamLeadUserId: number | null = null;
  memberToAdd: number | null = null;

  filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    if (!q) return this.teams();
    return this.teams().filter(
      (t) => t.description.toLowerCase().includes(q) || t.teamLeadName.toLowerCase().includes(q),
    );
  });

  // TL users for the lead picker, each with the other team they already lead (if any): a Team
  // Lead leads one team only.
  leads = computed(() => {
    const selectedId = this.selected()?.id;
    const leading = new Map(this.teams().map((t) => [t.teamLeadUserId, t]));
    return this.users()
      .filter((u) => u.role === 'TL')
      .map((u) => {
        const team = leading.get(u.id);
        return { user: u, otherTeam: team && team.id !== selectedId ? team.description : null };
      });
  });

  // Participants who are not already in the selected team.
  candidates = computed(() => {
    const inTeam = new Set((this.selected()?.members ?? []).map((m) => m.userId));
    return this.users().filter((u) => u.role === 'Participant' && !inTeam.has(u.id));
  });

  isFull = computed(() => {
    const t = this.selected();
    return !!t && t.memberCount >= t.maxMembers;
  });

  ngOnInit(): void {
    this.load();
  }

  load(selectId?: number): void {
    this.loading.set(true);
    forkJoin({ teams: this.api.listTeams(), users: this.api.listUsers() }).subscribe({
      next: ({ teams, users }) => {
        this.teams.set(teams);
        this.users.set(users);
        this.loading.set(false);
        const id = selectId ?? this.selected()?.id;
        if (id != null && teams.some((t) => t.id === id)) this.open(id);
        else this.show(null);
      },
      error: () => this.loading.set(false),
    });
  }

  private open(id: number): void {
    this.api.getTeam(id).subscribe({ next: (t) => this.show(t) });
  }

  // Put a team (or a blank record) into the form.
  private show(t: TeamDetailDto | null): void {
    this.selected.set(t);
    this.description = t?.description ?? '';
    this.category = t?.category ?? 'Regular';
    this.teamLeadUserId = t?.teamLeadUserId ?? null;
    this.memberToAdd = null;
    this.form()?.form.markAsUntouched();
  }

  isDirty(): boolean {
    const t = this.selected();
    return (
      this.description.trim() !== (t?.description ?? '') ||
      this.category !== (t?.category ?? 'Regular') ||
      this.teamLeadUserId !== (t?.teamLeadUserId ?? null)
    );
  }

  // Leaving a record with unsaved edits asks first.
  private async confirmDiscard(): Promise<boolean> {
    if (!this.isDirty()) return true;
    return this.confirmer.confirm({
      title: 'Discard changes?',
      message: 'You have unsaved changes to this team. They will be lost if you continue.',
      confirmText: 'Discard',
      cancelText: 'Keep editing',
      tone: 'warning',
    });
  }

  async select(t: TeamDto): Promise<void> {
    if (this.selected()?.id === t.id || !(await this.confirmDiscard())) return;
    this.open(t.id);
    // On stacked (mobile) layout, bring the form into view.
    if (window.matchMedia('(max-width: 900px)').matches) {
      this.formPane()?.nativeElement.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  }

  async newTeam(): Promise<void> {
    if (!(await this.confirmDiscard())) return;
    this.show(null);
  }

  async refresh(): Promise<void> {
    if (!(await this.confirmDiscard())) return;
    this.load();
  }

  get valid(): boolean {
    const len = this.description.trim().length;
    return len >= 2 && len <= 200 && this.teamLeadUserId != null;
  }

  save(): void {
    if (this.saving()) return;
    if (!this.valid) {
      this.form()?.form.markAllAsTouched();
      this.toast.warning('Enter a team name and choose a Team Lead.');
      return;
    }
    const body = {
      description: this.description.trim(),
      category: this.category,
      teamLeadUserId: this.teamLeadUserId!,
    };
    const current = this.selected();
    this.saving.set(true);
    (current ? this.api.updateTeam(current.id, body) : this.api.createTeam(body)).subscribe({
      next: (t) => {
        this.saving.set(false);
        this.toast.success(current ? 'Team saved.' : 'Team created.');
        this.show(t);
        this.load(t.id);
      },
      error: (err) => {
        this.saving.set(false);
        this.toast.error(err?.error?.message ?? 'Could not save the team.');
      },
    });
  }

  async remove(): Promise<void> {
    const t = this.selected();
    if (!t) return;
    const ok = await this.confirmer.confirm({
      title: 'Delete team?',
      message: `${t.description} and its member list will be permanently removed. The user accounts are kept. This cannot be undone.`,
      confirmText: 'Delete',
      tone: 'error',
    });
    if (!ok) return;
    this.api.deleteTeam(t.id).subscribe({
      next: () => {
        this.toast.success('Team deleted.');
        this.show(null);
        this.load();
      },
      error: (err) => this.toast.error(err?.error?.message ?? 'Could not delete the team.'),
    });
  }

  addMember(): void {
    const t = this.selected();
    if (!t || this.memberToAdd == null || this.isFull() || this.saving()) return;
    this.saving.set(true);
    this.api.addTeamMember(t.id, this.memberToAdd).subscribe({
      next: (res) => {
        this.saving.set(false);
        this.toast.success(`${res.member.fullName} added to ${t.description}.`);
        this.afterMembersChanged(t.id);
      },
      error: (err) => {
        this.saving.set(false);
        this.toast.error(err?.error?.message ?? 'Could not add this member.');
      },
    });
  }

  async removeMember(m: TeamMemberDto): Promise<void> {
    const t = this.selected();
    if (!t) return;
    const ok = await this.confirmer.confirm({
      title: 'Remove member?',
      message: `${m.fullName} will be removed from ${t.description}. Their account is kept.`,
      confirmText: 'Remove',
      tone: 'warning',
    });
    if (!ok) return;
    this.api.removeTeamMember(t.id, m.userId).subscribe({
      next: () => {
        this.toast.success(`${m.fullName} removed from the team.`);
        this.afterMembersChanged(t.id);
      },
      error: (err) => this.toast.error(err?.error?.message ?? 'Could not remove this member.'),
    });
  }

  // Refresh the member list and the list's counts without touching unsaved header edits.
  private afterMembersChanged(teamId: number): void {
    this.memberToAdd = null;
    forkJoin({ team: this.api.getTeam(teamId), teams: this.api.listTeams() }).subscribe({
      next: ({ team, teams }) => {
        this.teams.set(teams);
        this.selected.set(team);
      },
    });
  }
}
