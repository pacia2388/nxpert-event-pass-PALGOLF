import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { EventDto } from '../../core/models';
import { TeamInviteChanges } from '../../core/team-invite.util';
import { InvitationComposerComponent } from './invitation-composer';
import { InviteTeamsDialogComponent } from './invite-teams-dialog';

describe('Invitation composer: Invite teams', () => {
  let fixture: ComponentFixture<InvitationComposerComponent>;
  let opened: { component: unknown; data: unknown }[];
  // What the Invite teams picker closes with.
  let picked: TeamInviteChanges | undefined;

  // Opens the composer for an event.
  function compose(pickerResult: TeamInviteChanges | undefined): void {
    picked = pickerResult;
    localStorage.clear();
    localStorage.setItem('evt_auth', JSON.stringify({ token: 't', userId: 1, fullName: 'Demo Admin', role: 'Admin' }));
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    opened = [];
    vi.spyOn(TestBed.inject(MatDialog), 'open').mockImplementation(((component: unknown, config: { data: unknown }) => {
      opened.push({ component, data: config.data });
      return { afterClosed: () => of(picked) };
    }) as never);

    fixture = TestBed.createComponent(InvitationComposerComponent);
    fixture.componentRef.setInput('event', {
      id: 7, title: 'Q3 Product Seminar', venue: 'N-Pax HQ', startAt: '2026-11-18T01:00:00Z', endAt: '2026-11-18T04:00:00Z',
    } as unknown as EventDto);
    fixture.detectChanges();
    // The composer loads this event's email history when it opens.
    TestBed.inject(HttpTestingController).match(() => true).forEach((r) => r.flush([]));
    fixture.detectChanges();
  }

  const root = () => fixture.nativeElement as HTMLElement;
  const fieldOf = (label: 'To recipients' | 'CC recipients') =>
    root().querySelector(`[aria-label="${label}"]`)!.closest('mat-form-field') as HTMLElement;
  const inviteTeamsButton = (field: HTMLElement) =>
    Array.from(field.querySelectorAll('button')).find((b) => b.textContent?.includes('Invite teams'));
  const chips = (field: HTMLElement) =>
    Array.from(field.querySelectorAll('mat-chip-row')).map((c) => c.textContent?.replace('cancel', '').trim());

  function clickInviteTeams(): void {
    inviteTeamsButton(fieldOf('To recipients'))!.click();
    fixture.detectChanges();
  }

  afterEach(() => vi.restoreAllMocks());

  it('shows the Invite teams button in the To field only', () => {
    compose(undefined);
    expect(inviteTeamsButton(fieldOf('To recipients'))).toBeTruthy();
    expect(inviteTeamsButton(fieldOf('CC recipients'))).toBeUndefined();
  });

  it('adds the picked team members to To', () => {
    compose({ add: ['ana@example.com', 'ben@example.com'], remove: [] });
    clickInviteTeams();
    expect(opened[0].component).toBe(InviteTeamsDialogComponent);
    expect(chips(fieldOf('To recipients'))).toEqual(['ana@example.com', 'ben@example.com']);
    expect(chips(fieldOf('CC recipients'))).toEqual([]);
  });

  it('tells the picker who is already in To and does not duplicate them', () => {
    compose({ add: ['ana@example.com'], remove: [] });
    clickInviteTeams();
    picked = { add: ['ana@example.com', 'ben@example.com'], remove: [] };
    clickInviteTeams();
    expect(opened[1].data).toEqual({ alreadyInTo: ['ana@example.com'] });
    expect(chips(fieldOf('To recipients'))).toEqual(['ana@example.com', 'ben@example.com']);
  });

  it('takes members unticked in the picker out of To', () => {
    compose({ add: ['ana@example.com', 'ben@example.com'], remove: [] });
    clickInviteTeams();
    picked = { add: ['cy@example.com'], remove: ['ana@example.com'] };
    clickInviteTeams();
    expect(chips(fieldOf('To recipients'))).toEqual(['ben@example.com', 'cy@example.com']);
  });

  it('leaves To unchanged when the picker is cancelled', () => {
    compose(undefined);
    clickInviteTeams();
    expect(chips(fieldOf('To recipients'))).toEqual([]);
  });

  it('disables the button while the invitation is sending', () => {
    compose(undefined);
    fixture.componentInstance.sending.set(true);
    fixture.detectChanges();
    expect(inviteTeamsButton(fieldOf('To recipients'))!.disabled).toBe(true);
  });
});
