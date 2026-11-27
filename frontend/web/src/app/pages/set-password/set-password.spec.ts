import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { capsLockOn } from '../../shared/caps-lock';
import { SetPasswordComponent } from './set-password';

describe('Set password page', () => {
  let fixture: ComponentFixture<SetPasswordComponent>;

  beforeEach(async () => {
    capsLockOn.set(false);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ token: 'link-token' }) } },
        },
        {
          provide: AuthService,
          useValue: {
            setPasswordInfo: () =>
              of({ valid: true, name: 'Tess Ting', email: 'tess@example.com' }),
          },
        },
        { provide: ToastService, useValue: { success: () => {}, error: () => {} } },
      ],
    });
    fixture = TestBed.createComponent(SetPasswordComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  const root = () => fixture.nativeElement as HTMLElement;

  it('warns while Caps Lock is on, in the field being typed in', () => {
    const password = root().querySelector<HTMLInputElement>('input[name="password"]')!;
    const confirm = root().querySelector<HTMLInputElement>('input[name="confirm"]')!;
    const event = (type: string, capsLock: boolean) =>
      new KeyboardEvent(type, { key: 'A', modifierCapsLock: capsLock });
    const warnings = () => root().querySelectorAll('.caps-warning').length;

    expect(warnings()).toBe(0);

    // Caps Lock on before the field was focused: the key press that got there carried the state,
    // so the warning shows the moment the field takes focus, before anything is typed.
    document.body.dispatchEvent(event('keydown', true));
    password.focus();
    fixture.detectChanges();
    expect(warnings()).toBe(1);
    expect(
      password.closest('mat-form-field')!.nextElementSibling?.classList.contains('caps-warning'),
    ).toBe(true);

    // Only the field with focus carries the warning.
    confirm.focus();
    fixture.detectChanges();
    expect(warnings()).toBe(1);
    expect(
      confirm.closest('mat-form-field')!.nextElementSibling?.classList.contains('caps-warning'),
    ).toBe(true);

    confirm.dispatchEvent(event('keyup', false));
    fixture.detectChanges();
    expect(warnings()).toBe(0);

    // Leaving the field clears it, even with Caps Lock still on.
    password.focus();
    password.dispatchEvent(event('keydown', true));
    fixture.detectChanges();
    expect(warnings()).toBe(1);
    password.blur();
    fixture.detectChanges();
    expect(warnings()).toBe(0);
  });

  it('keeps the show-password eye out of the tab order', () => {
    const eye = root().querySelector<HTMLButtonElement>('button[matSuffix]')!;
    expect(eye.tabIndex).toBe(-1);
  });
});
