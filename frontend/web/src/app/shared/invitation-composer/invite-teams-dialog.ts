import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckbox, MatCheckboxChange, MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { TeamDto, TeamMemberDto } from '../../core/models';
import {
  TeamCheckState, TeamInviteChanges, memberAddress, selectableEmails, selectionChanges, teamCheckState, toggleMember, toggleTeam,
} from '../../core/team-invite.util';

export interface InviteTeamsDialogData {
  // Addresses already in the To field (lower-cased); their owners start ticked and can be unticked.
  alreadyInTo: string[];
}

/**
 * Picks invitation recipients from the teams: tick single members, or a team to take all of its
 * members. Members already in To start ticked; unticking one takes them out. Closes with the addresses
 * to add and to remove. The Team Lead is not a member and is not listed.
 */
@Component({
  selector: 'app-invite-teams-dialog',
  imports: [RouterLink, MatButtonModule, MatCheckboxModule, MatDialogModule, MatIconModule, MatProgressSpinnerModule],
  template: `
    <h2 mat-dialog-title>Invite teams</h2>
    <mat-dialog-content>
      <p class="hint">Tick a team to invite all its members, or open it to pick members one by one.
        Ticked members are added to the To field; untick a member to take them out.</p>
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
                        <mat-checkbox [checked]="isTicked(m)" [disabled]="!canPick(m)"
                                      (change)="tickMember(m)">
                          <span class="member-name">{{ m.fullName }}</span>
                          <span class="member-email">{{ m.email || 'No email address' }}</span>
                        </mat-checkbox>
                        @if (isAdded(m)) {
                          <span class="added" [class.removing]="!isTicked(m)">
                            {{ isTicked(m) ? 'Already added' : 'Will be removed' }}
                          </span>
                        }
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
      <button type="button" mat-flat-button class="add" (click)="confirm()" [disabled]="!hasChanges()">
        {{ confirmLabel() }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [
    `
      .hint { margin: 0 0 12px; color: #64748b; font-size: 13.5px; }
      .state { display: flex; align-items: center; gap: 10px; padding: 18px 4px; color: #475569; }
      .teams { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; }
      .teams li { border: 1px solid #e2e8f0; border-radius: 12px; overflow: hidden; }
      .teams li.open { border-color: #9dbfe8; }
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
      .added { color: var(--pg-blue-deep); font-size: 12px; font-weight: 600; white-space: nowrap; }
      .added.removing { color: #b45309; }
      .muted { margin: 8px 0 4px; color: #64748b; font-size: 13px; }
    `,
  ],
})
export class InviteTeamsDialogComponent {
  readonly data = inject<InviteTeamsDialogData>(MAT_DIALOG_DATA);
  readonly ref = inject(MatDialogRef<InviteTeamsDialogComponent, TeamInviteChanges>);
  private api = inject(ApiService);

  teams = signal<TeamDto[]>([]);
  loading = signal(true);
  error = signal(false);
  // Members per team id, loaded the first time a team is opened or ticked.
  members = signal<Record<number, TeamMemberDto[]>>({});
  failed = signal<ReadonlySet<number>>(new Set());
  private opened = signal<ReadonlySet<number>>(new Set());
  private loadingMembers = new Set<number>();
  // Teams ticked before their members arrived, with the box to bring in step once they do.
  private tickWhenLoaded = new Map<number, MatCheckbox>();
  // Ticked addresses. Starts as everyone already in To, so unticking one of them removes them.
  private selected = signal<ReadonlySet<string>>(new Set(this.data.alreadyInTo));
  private changes = computed(() => selectionChanges(this.selected(), this.data.alreadyInTo));
  hasChanges = computed(() => this.changes().add.length + this.changes().remove.length > 0);
  confirmLabel = computed(() => {
    const { add, remove } = this.changes();
    if (add.length && remove.length) return `Add ${add.length}, remove ${remove.length}`;
    if (remove.length) return `Remove ${remove.length}`;
    return add.length ? `Add ${add.length}` : 'Add';
  });

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
    return teamCheckState(this.members()[team.id] ?? [], this.selected());
  }

  // A team can be ticked while it may have someone to invite.
  canTick(team: TeamDto): boolean {
    const list = this.members()[team.id];
    return list ? selectableEmails(list).length > 0 : team.memberCount > 0;
  }

  tickTeam(team: TeamDto, change: MatCheckboxChange): void {
    const list = this.members()[team.id];
    if (list) {
      this.selected.set(toggleTeam(list, this.selected()));
      // Keep the box in step with the members (a team with no usable emails stays unticked).
      change.source.checked = this.state(team) === 'all';
      return;
    }
    // Not loaded yet: remember the tick (or its undoing) and apply it when the members arrive,
    // whether this click or an earlier one started the load.
    if (change.checked) this.tickWhenLoaded.set(team.id, change.source);
    else this.tickWhenLoaded.delete(team.id);
    this.loadMembers(team);
  }

  loadMembers(team: TeamDto): void {
    if (this.members()[team.id] || this.loadingMembers.has(team.id)) return;
    this.loadingMembers.add(team.id);
    this.setFailed(team.id, false);
    this.api.getTeam(team.id).subscribe({
      next: (detail) => {
        this.loadingMembers.delete(team.id);
        this.members.set({ ...this.members(), [team.id]: detail.members });
        const box = this.tickWhenLoaded.get(team.id);
        this.tickWhenLoaded.delete(team.id);
        if (!box) return;
        // Tick everyone. Members already in To stay; nothing is unticked unseen.
        this.selected.set(new Set([...this.selected(), ...selectableEmails(detail.members)]));
        box.checked = this.state(team) === 'all';
      },
      error: () => {
        this.loadingMembers.delete(team.id);
        this.setFailed(team.id, true);
        const box = this.tickWhenLoaded.get(team.id);
        this.tickWhenLoaded.delete(team.id);
        if (!box) return;
        // A failed load leaves the box unticked and opens the team to show the retry.
        box.checked = false;
        this.opened.set(new Set(this.opened()).add(team.id));
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
    return !!address && this.data.alreadyInTo.includes(address);
  }

  isTicked(member: TeamMemberDto): boolean {
    const address = memberAddress(member);
    return !!address && this.selected().has(address);
  }

  canPick(member: TeamMemberDto): boolean {
    return !!memberAddress(member);
  }

  tickMember(member: TeamMemberDto): void {
    const address = memberAddress(member);
    if (address) this.selected.set(toggleMember(address, this.selected()));
  }

  confirm(): void {
    if (this.hasChanges()) this.ref.close(this.changes());
  }
}

export function openInviteTeamsDialog(dialog: MatDialog, data: InviteTeamsDialogData): Observable<TeamInviteChanges | undefined> {
  return dialog.open<InviteTeamsDialogComponent, InviteTeamsDialogData, TeamInviteChanges>(InviteTeamsDialogComponent, {
    data,
    width: '520px',
    maxWidth: '95vw',
    autoFocus: false,
  }).afterClosed();
}
