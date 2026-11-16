import { ChangeDetectorRef, Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule, NgModel } from '@angular/forms';
import { By } from '@angular/platform-browser';
import { PasswordRuleDirective } from './password-rule.directive';

@Component({
  imports: [FormsModule, PasswordRuleDirective],
  template: `<input name="password" [(ngModel)]="password" [appPasswordRule]="email" />`,
})
class HostComponent {
  password = '';
  email: string | null = null;
}

describe('Password rule on a form field', () => {
  let fixture: ComponentFixture<HostComponent>;

  beforeEach(() => {
    fixture = TestBed.createComponent(HostComponent);
  });

  async function enter(password: string, email: string | null = null): Promise<NgModel> {
    fixture.componentInstance.password = password;
    fixture.componentInstance.email = email;
    fixture.componentRef.injector.get(ChangeDetectorRef).markForCheck();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.debugElement.query(By.directive(NgModel)).injector.get(NgModel);
  }

  it('leaves an empty field to the required check', async () => {
    expect((await enter('')).valid).toBe(true);
  });

  it('accepts a password that meets every rule', async () => {
    expect((await enter('Passw0rd!', 'bob@example.com')).valid).toBe(true);
  });

  it('marks a password that misses a rule', async () => {
    expect((await enter('Passw0rd1')).hasError('passwordRule')).toBe(true);
  });

  it("marks a password that is the user's email", async () => {
    expect((await enter('bob1@example.com', 'Bob1@example.com')).hasError('passwordRule')).toBe(
      true,
    );
  });

  it('checks again when the email changes', async () => {
    await enter('bob1@example.com', 'someone@example.com');
    const model = await enter('bob1@example.com', 'bob1@example.com');
    expect(model.hasError('passwordRule')).toBe(true);
  });
});
