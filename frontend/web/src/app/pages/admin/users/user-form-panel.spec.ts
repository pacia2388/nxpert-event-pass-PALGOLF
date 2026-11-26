import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MatTooltip } from '@angular/material/tooltip';
import { MatSelectHarness } from '@angular/material/select/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { By } from '@angular/platform-browser';
import { UserDto } from '../../../core/models';
import { capsLockOn } from '../../../shared/caps-lock';
import { UserFormPanelComponent } from './user-form-panel';

describe('User form panel', () => {
  let fixture: ComponentFixture<UserFormPanelComponent>;

  beforeEach(async () => {
    capsLockOn.set(false);
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNativeDateAdapter()],
    });
    fixture = TestBed.createComponent(UserFormPanelComponent);
    await settle();
  });

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const root = () => fixture.nativeElement as HTMLElement;
  const requiredMarkers = () => root().querySelectorAll('mat-icon.req').length;
  const input = (name: string) => root().querySelector<HTMLInputElement>(`input[name="${name}"]`)!;
  const field = (name: string) => input(name).closest('mat-form-field')!;
  // The red "!" inside a field, with the message its tooltip gives, or null when there is none.
  const marker = (name: string): string | null => {
    const icon = fixture.debugElement.query(
      By.css(`mat-form-field:has(input[name="${name}"]) mat-icon.req`),
    );
    return icon ? icon.injector.get(MatTooltip).message : null;
  };

  // Types into a field and leaves it, as the admin would.
  function enter(name: string, value: string): void {
    const el = input(name);
    el.dispatchEvent(new Event('focus'));
    el.value = value;
    el.dispatchEvent(new Event('input'));
    el.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
  }

  it('opens a blank new user without required-field markers', () => {
    expect(requiredMarkers()).toBe(0);
  });

  it('marks the empty required fields after a save attempt', async () => {
    fixture.componentInstance.save();
    await settle();

    // First name, last name and email are empty on a new record. A blank password is allowed:
    // the new account gets a set-password link instead.
    expect(requiredMarkers()).toBe(3);
  });

  it('marks an empty required field once the user leaves it', () => {
    enter('firstName', '');

    expect(marker('firstName')).toBe('Required');
  });

  it('marks a name of only spaces, and shows the field as invalid', () => {
    enter('firstName', '   ');

    expect(marker('firstName')).toBe('Required');
    expect(field('firstName').classList).toContain('mat-form-field-invalid');
  });

  // The checklist under the password field, each rule with whether it is met.
  const checklist = () =>
    [...root().querySelectorAll('app-password-requirements li')].map(
      (li) =>
        `${li.classList.contains('met') ? '✓' : '·'} ${li.textContent!.replace(/^\s*\S+\s+/, '').trim()}`,
    );

  it('marks a password that misses a rule and shows which one under the field', () => {
    enter('password', 'Passw0rd1');

    expect(marker('password')).toBe("Doesn't meet the requirements below");
    expect(checklist()).toEqual([
      '✓ 8 to 128 characters',
      '✓ A letter',
      '✓ A number',
      '· A symbol, like ! @ # or ?',
    ]);
  });

  it('marks a password that is the user email', () => {
    enter('email', 'bob1@example.com');
    enter('password', 'bob1@example.com');

    expect(marker('password')).toBe("Doesn't meet the requirements below");
    expect(checklist().at(-1)).toBe('· Not your email');
  });

  it('accepts a password that meets every rule', () => {
    enter('password', 'Passw0rd!');

    expect(marker('password')).toBeNull();
  });

  it('marks an email that is not a valid address', () => {
    enter('email', 'bob');

    expect(marker('email')).toBe('Enter a valid email address');
  });

  it('requires the phone once mobile is chosen and says so after the field is left', async () => {
    const loader = TestbedHarnessEnvironment.loader(fixture);
    const channel = await loader.getHarness(
      MatSelectHarness.with({ selector: '[name="notificationChannel"]' }),
    );
    await channel.open();
    await channel.clickOptions({ text: 'Mobile (Viber)' });
    await settle();

    const phone = root().querySelector<HTMLInputElement>('app-phone-field input[type="tel"]')!;
    expect(phone.required).toBe(true);
    phone.dispatchEvent(new Event('blur'));
    await settle();

    expect(root().querySelector('app-phone-field .phone-error')?.textContent?.trim()).toBe(
      'A phone number is required for mobile.',
    );
  });

  it('warns while Caps Lock is on in the password field', () => {
    const password = input('password');
    const warnings = () => root().querySelectorAll('.caps-warning').length;
    const key = (type: string, capsLock: boolean) =>
      new KeyboardEvent(type, { key: 'Tab', modifierCapsLock: capsLock });

    // Caps Lock on before the field was focused: the key press that got there carried the state.
    document.body.dispatchEvent(key('keydown', true));
    password.focus();
    fixture.detectChanges();
    expect(warnings()).toBe(1);

    document.body.dispatchEvent(key('keyup', false));
    fixture.detectChanges();
    expect(warnings()).toBe(0);

    password.blur();
    fixture.detectChanges();
    expect(warnings()).toBe(0);
  });

  it('clears the markers when another user is opened', async () => {
    fixture.componentInstance.save();
    await settle();

    fixture.componentRef.setInput('user', existingUser);
    await settle();

    expect(requiredMarkers()).toBe(0);
  });
});

const existingUser: UserDto = {
  id: 7,
  fullName: 'Ana Cruz',
  firstName: 'Ana',
  middleInitial: null,
  lastName: 'Cruz',
  senateNo: null,
  level: null,
  email: 'ana@example.test',
  username: 'acruz',
  role: 'Participant',
  organization: null,
  phone: null,
  notificationChannel: 'Email',
  canConfirmRegistrations: false,
  emailVerified: true,
  createdAt: '2026-01-01T00:00:00Z',
  photoUrl: null,
  hasFace: false,
  birthdate: '1990-05-01',
};
