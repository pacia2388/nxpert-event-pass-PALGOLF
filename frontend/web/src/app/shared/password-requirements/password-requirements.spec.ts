import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PasswordRequirementsComponent } from './password-requirements';

describe('Password requirements', () => {
  let fixture: ComponentFixture<PasswordRequirementsComponent>;

  beforeEach(() => {
    fixture = TestBed.createComponent(PasswordRequirementsComponent);
  });

  function show(password: string, email?: string): void {
    fixture.componentRef.setInput('password', password);
    if (email !== undefined) fixture.componentRef.setInput('email', email);
    fixture.detectChanges();
  }

  // Each rule as the player reads it, with whether it is met.
  const rules = () =>
    [...(fixture.nativeElement as HTMLElement).querySelectorAll('li')].map((li) => ({
      text: li.textContent!.replace(/^\s*\S+\s+/, '').trim(),
      met: li.classList.contains('met'),
    }));

  it('lists every rule as unmet before anything is typed', () => {
    show('');
    expect(rules()).toEqual([
      { text: '8 to 128 characters', met: false },
      { text: 'A letter', met: false },
      { text: 'A number', met: false },
      { text: 'A symbol, like ! @ # or ?', met: false },
    ]);
  });

  it('ticks each rule as the password meets it', () => {
    show('Passw0rd1');
    expect(rules().map((r) => r.met)).toEqual([true, true, true, false]);

    show('Passw0rd!');
    expect(rules().every((r) => r.met)).toBe(true);
  });

  it('does not count a space as the symbol', () => {
    show('Pass w0rd1');
    expect(rules()[3].met).toBe(false);
  });

  it('counts an accented letter as a letter, not as the symbol', () => {
    show('ñññññññ1');
    expect(rules().map((r) => r.met)).toEqual([true, true, true, false]);
  });

  it('adds the email rule when the email is known', () => {
    show('me@x.com1!', 'me@x.com1!');
    expect(rules().at(-1)).toEqual({ text: 'Not your email', met: false });

    show('Passw0rd!', 'ME@X.COM');
    expect(rules().at(-1)).toEqual({ text: 'Not your email', met: true });
  });
});
