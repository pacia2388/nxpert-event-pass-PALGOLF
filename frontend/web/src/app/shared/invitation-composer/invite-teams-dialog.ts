import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxChange, MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { TeamDto, TeamMemberDto } from '../../core/models';
import { TeamCheckState, memberAddress, selectableEmails, teamCheckState, toggleMember, toggleTeam } from '../../core/team-invite.util';

export interface InviteTeamsDialogData {
  // Addresses already in the To field (lower-cased); their owners are shown as added.
  alreadyInTo: string[];
}

/**
 * Picks invitation recipients from the teams: tick single members, or a team to take all of its
 * members. Closes with the picked email addresses. The Team Lead is not a member and is not listed.
 */
@Component({
  selector: 'app-invite-teams-dialog',
  imports: [RouterLink, MatButtonModule, MatCheckboxModule, MatDialogModule, MatIconModule, MatProgressSpinnerModule],
  template: `
    <h2 mat-dialog-title>Invite teams</h2>
    <mat-dialog-content>
      <p class="hint">Tick a team to invite all its members, or open it to pick members one by one.</p>
      @if (loading()) {
        <div class="state"><mat-spinner diameter="28" /> <span>Loading teams…</span></div>
      } @else if (error()) {
        <div class="state" role="alert">
          <span>Could not load teams.</span>
          <button type="button" mat-button class="retry" (click)="load()">Retry</button>
        </div>
      } @else if (!teams().length) {
        <div class="state">
          <span>No teams yet. Create one on the <a routerLink="/admin/teams" (click)="ref.close()">Teams page</a>.</span>
        </div>
      } @else {
        <ul class="teams">
          @for (t of teams(); track t.id) {
            <li data-team [class.open]="isOpen(t.id)">
              <div class="team-head">
                <mat-checkbox [checked]="state(t) === 'all'" [indeterminate]="state(t) === 'some'"
                              [disabled]="!canTick(t)" (change)="tickTeam(t, $event)"
                              [aria-label]="'Invite all members of ' + t.description" />
                <button type="button" class="expand" (click)="toggleOpen(t)" [attr.aria-expanded]="isOpen(t.id)"
                        [attr.aria-label]="(isOpen(t.id) ? 'Hide members of ' : 'Show members of ') + t.description">
                  <span class="team-name">{{ t.description }}</span>
                  <span class="team-meta">
                    {{ t.category }} ·
                    {{ t.memberCount ? t.memberCount + (t.memberCount === 1 ? ' member' : ' members') : 'No members' }}
                  </span>
                  <mat-icon>{{ isOpen(t.id) ? 'expand_less' : 'expand_more' }}</mat-icon>
                </button>
              </div>
              @if (isOpen(t.id)) {
                <div class="members">
                  @if (members()[t.id]; as list) {
                    @for (m of list; track m.userId) {
                      <div class="member" data-member>
                        <mat-checkbox [checked]="isAdded(m) || isPicked(m)" [disabled]="!canPick(m)"
                                      (change)="tickMember(m)">
                          <span class="member-name">{{ m.fullName }}</span>
                          <span class="member-email">{{ m.email || 'No email address' }}</span>
                        </mat-checkbox>
                        @if (isAdded(m)) { <span class="added">Already added</span> }
                      </div>
                    } @empty {
                      <p class="muted">No members in this team.</p>
                    }
                  } @else if (failed().has(t.id)) {
                    <p class="muted" role="alert">
                      Could not load members.
                      <button type="button" mat-button (click)="loadMembers(t)">Retry</button>
                    </p>
                  } @else {
                    <p class="muted">Loading members…</p>
                  }
                </div>
              }
            </li>
          }
        </ul>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button type="button" mat-button class="cancel" (click)="ref.close()">Cancel</button>
      <button type="button" mat-flat-button class="add" (click)="confirm()" [disabled]="!picked().size">
        {{ picked().size ? 'Add ' + picked().size + ' to To' : 'Add to To' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [
    `
      .hint { margin: 0 0 12px; color: #64748b; font-size: 13.5px; }
      .state { display: flex; align-items: center; gap: 10px; padding: 18px 4px; color: #475569; }
      .teams { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; }
      .teams li { border: 1px solid #e2e8f0; border-radius: 12px; overflow: hidden; }
      .teams li.open { border-color: #99d5cc; }
      .team-head { display: flex; align-items: center; padding: 2px 6px 2px 2px; }
      .expand { flex: 1; min-width: 0; display: flex; align-items: center; gap: 10px; padding: 8px 4px; border: 0;
        background: none; font: inherit; color: inherit; text-align: left; cursor: pointer; }
      .team-name { font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
      .team-meta { margin-left: auto; color: #64748b; font-size: 12.5px; white-space: nowrap; }
      .expand mat-icon { color: #64748b; }
      .members { padding: 2px 10px 8px 26px; border-top: 1px solid #eef2f6; background: #f8fafc; }
      .member { display: flex; align-items: center; gap: 8px; }
      .member mat-checkbox { flex: 1; min-width: 0; }
      .member-name { display: block; font-weight: 500; }
      .member-email { display: block; color: #64748b; font-size: 12.5px; overflow-wrap: anywhere; }
      .added { color: #0f766e; font-size: 12px; font-weight: 600; white-space: nowrap; }
      .muted { margin: 8px 0 4px; color: #64748b; font-size: 13px; }
    `,
  ],
})
export class InviteTeamsDialogComponent {
  readonly data = inject<InviteTeamsDialogData>(MAT_DIALOG_DATA);
  readonly ref = inject(MatDialogRef<InviteTeamsDialogComponent, string[]>);
  private api = inject(ApiService);

  teams = signal<TeamDto[]>([]);
  loading = signal(true);
  error = signal(false);
  // Members per team id, loaded the first time a team is opened or ticked.
  members = signal<Record<number, TeamMemberDto[]>>({});
  failed = signal<ReadonlySet<number>>(new Set());
  private opened = signal<ReadonlySet<number>>(new Set());
  private loadingMembers = new Set<number>();
  picked = signal<ReadonlySet<string>>(new Set());
  private added = computed(() => this.data.alreadyInTo);

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.api.listTeams().subscribe({
      next: (teams) => {
        this.teams.set(teams);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  isOpen(teamId: number): boolean {
    return this.opened().has(teamId);
  }

  toggleOpen(team: TeamDto): void {
    const next = new Set(this.opened());
    if (!next.delete(team.id)) next.add(team.id);
    this.opened.set(next);
    if (next.has(team.id)) this.loadMembers(team);
  }

  state(team: TeamDto): TeamCheckState {
    return teamCheckState(this.members()[team.id] ?? [], this.picked(), this.added());
  }

  // A team can be ticked while it may still have someone to invite.
  canTick(team: TeamDto): boolean {
    const list = this.members()[team.id];
    return list ? selectableEmails(list, this.added()).length > 0 : team.memberCount > 0;
  }

  tickTeam(team: TeamDto, change: MatCheckboxChange): void {
    const pickAll = (list: TeamMemberDto[]) => {
      this.picked.set(toggleTeam(list, this.picked(), this.added()));
      // Keep the box in step when nothing could be picked (everyone is already in To).
      change.source.checked = this.state(team) === 'all';
    };
    const list = this.members()[team.id];
    if (list) return pickAll(list);
    // Not loaded yet: load, then take everyone. A failed load leaves the box unticked.
    this.loadMembers(team, pickAll, () => {
      change.source.checked = false;
      this.opened.set(new Set(this.opened()).add(team.id));
    });
  }

  loadMembers(team: TeamDto, then?: (list: TeamMemberDto[]) => void, failed?: () => void): void {
    if (this.members()[team.id] || this.loadingMembers.has(team.id)) return;
    this.loadingMembers.add(team.id);
    this.setFailed(team.id, false);
    this.api.getTeam(team.id).subscribe({
      next: (detail) => {
        this.loadingMembers.delete(team.id);
        this.members.set({ ...this.members(), [team.id]: detail.members });
        then?.(detail.members);
      },
      error: () => {
        this.loadingMembers.delete(team.id);
        this.setFailed(team.id, true);
        failed?.();
      },
    });
  }

  private setFailed(teamId: number, value: boolean): void {
    const next = new Set(this.failed());
    value ? next.add(teamId) : next.delete(teamId);
    this.failed.set(next);
  }

  isAdded(member: TeamMemberDto): boolean {
    const address = memberAddress(member);
    return !!address && this.added().includes(address);
  }

  isPicked(member: TeamMemberDto): boolean {
    const address = memberAddress(member);
    return !!address && this.picked().has(address);
  }

  canPick(member: TeamMemberDto): boolean {
    return !!memberAddress(member) && !this.isAdded(member);
  }

  tickMember(member: TeamMemberDto): void {
    const address = memberAddress(member);
    if (address && this.canPick(member)) this.picked.set(toggleMember(address, this.picked()));
  }

  confirm(): void {
    if (this.picked().size) this.ref.close([...this.picked()]);
  }
}

export function openInviteTeamsDialog(dialog: MatDialog, data: InviteTeamsDialogData): Observable<string[] | undefined> {
  return dialog.open<InviteTeamsDialogComponent, InviteTeamsDialogData, string[]>(InviteTeamsDialogComponent, {
    data,
    width: '520px',
    maxWidth: '95vw',
    autoFocus: false,
  }).afterClosed();
}
