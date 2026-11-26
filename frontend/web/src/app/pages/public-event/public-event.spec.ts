import { ComponentFixture, TestBed } from '@angular/core/testing';
import { convertToParamMap } from '@angular/router';
import { ActivatedRoute, Router } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { CategoryWarningService } from '../../core/category-warning.service';
import { MobileChannelService } from '../../core/mobile-channel.service';
import { OfflineService } from '../../core/offline.service';
import { PhoneCountryService } from '../../core/phone-country.service';
import { ToastService } from '../../core/toast.service';
import { PublicEventComponent } from './public-event';

// The invite magic-link: a registered invitee is asked to sign in (never auto-signed in);
// a guest gets the guest form pre-filled with the invited address.
describe('public event invite link', () => {
  const event = {
    id: 7,
    title: 'Golf Day',
    slug: 'golf-day',
    startsAt: '2027-02-17T08:00:00',
    endsAt: '2027-02-17T16:00:00',
    venue: 'Course',
    status: 'Published',
    isFull: false,
    sessions: [],
    formFields: [],
    meals: [],
    format: 'InPerson',
  };

  async function setup(
    resolve: { registered: boolean; email: string; eventSlug: string },
    loggedIn = false,
  ): Promise<{
    fixture: ComponentFixture<PublicEventComponent>;
    forgotPassword: ReturnType<typeof vi.fn>;
    navigate: ReturnType<typeof vi.fn>;
  }> {
    const forgotPassword = vi.fn(() => of({ message: 'ok' }));
    const navigate = vi.fn();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        {
          provide: ApiService,
          useValue: { getPublicEvent: () => of(event), getMobileChannel: () => of({}) },
        },
        {
          provide: AuthService,
          useValue: {
            isLoggedIn: () => loggedIn,
            resolveInvite: () => of(resolve),
            forgotPassword,
          },
        },
        { provide: MobileChannelService, useValue: { name: () => 'Mobile', label: () => 'Mobile' } },
        { provide: OfflineService, useValue: { online: () => true } },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: convertToParamMap({ slug: 'golf-day' }),
              queryParamMap: convertToParamMap({ invite: 'link-token' }),
            },
          },
        },
        { provide: Router, useValue: { navigate } },
        { provide: ToastService, useValue: { error: vi.fn(), warning: vi.fn(), success: vi.fn() } },
        { provide: CategoryWarningService, useValue: { confirm: () => Promise.resolve(true) } },
        { provide: MatDialog, useValue: { open: vi.fn() } },
        {
          provide: PhoneCountryService,
          useValue: { isValid: () => true, supports: () => true },
        },
      ],
    });
    // The invite handling lives in the component class; an empty template keeps the heavy page
    // out of this unit test.
    TestBed.overrideComponent(PublicEventComponent, { set: { template: '' } });
    const fixture = TestBed.createComponent(PublicEventComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    return { fixture, forgotPassword, navigate };
  }

  it('pre-fills the guest email and offers no sign-in for a guest invitee', async () => {
    const { fixture } = await setup({ registered: false, email: 'guest@example.com', eventSlug: 'golf-day' });

    expect(fixture.componentInstance.email).toBe('guest@example.com');
    expect(fixture.componentInstance.inviteEmail()).toBeNull();
  });

  it('asks a registered invitee to sign in with their email, returning to the event', async () => {
    const { fixture, navigate } = await setup({
      registered: true,
      email: 'member@example.com',
      eventSlug: 'golf-day',
    });

    expect(fixture.componentInstance.inviteEmail()).toBe('member@example.com');

    fixture.componentInstance.signInWithInvite();
    expect(navigate).toHaveBeenCalledWith(['/login'], {
      queryParams: { email: 'member@example.com', returnUrl: '/e/golf-day' },
    });
  });

  it('can send a password link to a registered invitee with no password', async () => {
    const { fixture, forgotPassword } = await setup({
      registered: true,
      email: 'member@example.com',
      eventSlug: 'golf-day',
    });

    fixture.componentInstance.sendInvitePasswordLink();

    expect(forgotPassword).toHaveBeenCalledWith('member@example.com');
    expect(fixture.componentInstance.invitePasswordLinkSent()).toBe(true);
  });

  it('ignores an invite link once someone is signed in', async () => {
    const { fixture } = await setup(
      { registered: true, email: 'member@example.com', eventSlug: 'golf-day' },
      true,
    );

    expect(fixture.componentInstance.inviteEmail()).toBeNull();
  });
});
