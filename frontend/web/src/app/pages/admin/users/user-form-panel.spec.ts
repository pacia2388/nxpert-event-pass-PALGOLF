import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MatTooltip } from '@angular/material/tooltip';
import { MatSelectHarness } from '@angular/material/select/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { By } from '@angular/platform-browser';
import { UserDto } from '../../../core/models';
import { UserFormPanelComponent } from './user-form-panel';

describe('User form panel', () => {
  let fixture: ComponentFixture<UserFormPanelComponent>;

  beforeEach(async () => {
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
    const icon = fixture.debugElement.query(By.css(`mat-form-field:has(input[name="${name}"]) mat-icon.req`));
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

    // First name, last name, email and password are empty on a new record.
    expect(requiredMarkers()).toBe(4);
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

  it('marks a password shorter than 8 characters', () => {
    enter('password', 'abc12');

    expect(marker('password')).toBe('At least 8 characters');
  });

  it('marks an email that is not a valid address', () => {
    enter('email', 'bob');

    expect(marker('email')).toBe('Enter a valid email address');
  });

  it('marks the empty phone as soon as Viber is chosen', async () => {
    const loader = TestbedHarnessEnvironment.loader(fixture);
    const channel = await loader.getHarness(
      MatSelectHarness.with({ selector: '[name="notificationChannel"]' }),
    );
    await channel.open();
    await channel.clickOptions({ text: 'Viber' });
    await settle();

    expect(marker('phone')).toBe('Required for Viber');
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
