import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { TeamDetailDto, TeamDto } from '../../core/models';
import { InviteTeamsDialogComponent } from './invite-teams-dialog';

function team(id: number, description: string, memberCount: number): TeamDto {
  return { id, description, category: 'Regular', teamLeadUserId: 100 + id, teamLeadName: `Lead ${id}`,
    memberCount, maxMembers: 10, createdAt: '2026-11-10T00:00:00Z' };
}

function detail(base: TeamDto, emails: string[]): TeamDetailDto {
  return { ...base, members: emails.map((email, i) => ({ userId: base.id * 10 + i, fullName: `Member ${email.split('@')[0]}`,
    email, addedAt: '2026-11-10T00:00:00Z' })) };
}

const eagles = team(1, 'Eagles', 2);
const birdies = team(2, 'Birdies', 1);
const empty = team(3, 'Rookies', 0);

describe('Invite teams picker', () => {
  let fixture: ComponentFixture<InviteTeamsDialogComponent>;
  let http: HttpTestingController;
  let closedWith: unknown;
  let closed: boolean;

  function open(alreadyInTo: string[] = []): void {
    closed = false;
    closedWith = undefined;
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { alreadyInTo } },
        { provide: MatDialogRef, useValue: { close: (value?: unknown) => { closed = true; closedWith = value; } } },
      ],
    });
    fixture = TestBed.createComponent(InviteTeamsDialogComponent);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  }

  const root = () => fixture.nativeElement as HTMLElement;
  const text = () => root().textContent ?? '';
  const settle = () => fixture.detectChanges();

  function serveTeams(teams: TeamDto[]): void {
    http.expectOne((r) => r.url.endsWith('/api/teams')).flush(teams);
    settle();
  }

  function serveMembers(d: TeamDetailDto): void {
    http.expectOne((r) => r.url.endsWith(`/api/teams/${d.id}`)).flush(d);
    settle();
  }

  function teamRow(name: string): HTMLElement {
    const row = Array.from(root().querySelectorAll<HTMLElement>('[data-team]')).find((el) => el.textContent?.includes(name));
    if (!row) throw new Error(`No team row for ${name}`);
    return row;
  }

  function expand(name: string): void {
    teamRow(name).querySelector<HTMLButtonElement>('button.expand')!.click();
    settle();
  }

  const teamBox = (name: string) => teamRow(name).querySelector<HTMLInputElement>('.team-head input[type=checkbox]')!;

  function memberBox(email: string): HTMLInputElement {
    const row = Array.from(root().querySelectorAll<HTMLElement>('[data-member]')).find((el) => el.textContent?.includes(email));
    if (!row) throw new Error(`No member row for ${email}`);
    return row.querySelector<HTMLInputElement>('input[type=checkbox]')!;
  }

  function tick(box: HTMLInputElement): void {
    box.click();
    settle();
  }

  const addButton = () => root().querySelector<HTMLButtonElement>('button.add')!;

  afterEach(() => http.verify());

  it('lists the teams and loads a team\'s members only when it is opened', () => {
    open();
    serveTeams([eagles, birdies]);
    expect(text()).toContain('Eagles');
    expect(text()).toContain('Birdies');

    expand('Eagles');
    serveMembers(detail(eagles, ['ana@example.com', 'ben@example.com']));
    expect(text()).toContain('Member ana');
    expect(text()).toContain('ben@example.com');
  });

  it('adds the ticked members to To', () => {
    open();
    serveTeams([eagles]);
    expand('Eagles');
    serveMembers(detail(eagles, ['ana@example.com', 'ben@example.com']));
    expect(addButton().disabled).toBe(true);

    tick(memberBox('ben@example.com'));
    expect(addButton().textContent).toContain('Add 1 to To');
    expect(teamBox('Eagles').indeterminate).toBe(true);

    addButton().click();
    expect(closedWith).toEqual(['ben@example.com']);
  });

  it('ticking a team picks all its members, loading them first', () => {
    open();
    serveTeams([eagles, birdies]);
    tick(teamBox('Eagles'));
    serveMembers(detail(eagles, ['ana@example.com', 'ben@example.com']));

    expect(teamBox('Eagles').checked).toBe(true);
    expect(addButton().textContent).toContain('Add 2 to To');
    addButton().click();
    expect(closedWith).toEqual(['ana@example.com', 'ben@example.com']);
  });

  it('unticking a team clears its members', () => {
    open();
    serveTeams([eagles]);
    tick(teamBox('Eagles'));
    serveMembers(detail(eagles, ['ana@example.com', 'ben@example.com']));
    tick(teamBox('Eagles'));
    expect(teamBox('Eagles').checked).toBe(false);
    expect(addButton().disabled).toBe(true);
  });

  it('shows members already in To as added and does not add them again', () => {
    open(['ana@example.com']);
    serveTeams([eagles]);
    tick(teamBox('Eagles'));
    serveMembers(detail(eagles, ['Ana@Example.com', 'ben@example.com']));
    expand('Eagles');

    expect(memberBox('Ana@Example.com').disabled).toBe(true);
    expect(text()).toContain('Already added');
    addButton().click();
    expect(closedWith).toEqual(['ben@example.com']);
  });

  it('leaves a team unticked when all its members are already in To', () => {
    open(['ana@example.com', 'ben@example.com']);
    serveTeams([eagles]);
    tick(teamBox('Eagles'));
    serveMembers(detail(eagles, ['ana@example.com', 'ben@example.com']));

    expect(teamBox('Eagles').checked).toBe(false);
    expect(teamBox('Eagles').disabled).toBe(true);
    expect(addButton().disabled).toBe(true);
  });

  it('cannot tick a team with no members', () => {
    open();
    serveTeams([empty]);
    expect(teamBox('Rookies').disabled).toBe(true);
    expect(text()).toContain('No members');
  });

  it('says so when there are no teams', () => {
    open();
    serveTeams([]);
    expect(text()).toContain('No teams yet');
    expect(addButton().disabled).toBe(true);
  });

  it('offers a retry when the teams cannot be loaded', () => {
    open();
    http.expectOne((r) => r.url.endsWith('/api/teams')).flush('', { status: 500, statusText: 'Server Error' });
    settle();
    expect(text()).toContain('Could not load teams');

    root().querySelector<HTMLButtonElement>('button.retry')!.click();
    settle();
    serveTeams([eagles]);
    expect(text()).toContain('Eagles');
  });

  it('closes without a result on Cancel', () => {
    open();
    serveTeams([eagles]);
    root().querySelector<HTMLButtonElement>('button.cancel')!.click();
    expect(closed).toBe(true);
    expect(closedWith).toBeUndefined();
  });
});
