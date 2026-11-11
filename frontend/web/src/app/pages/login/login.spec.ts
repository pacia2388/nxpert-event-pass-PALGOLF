import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { LoginComponent } from './login';

describe('Login page', () => {
  let fixture: ComponentFixture<LoginComponent>;

  beforeEach(async () => {
    localStorage.clear();
    // The toolbar colour index.html ships for the rest of the app.
    document.querySelector('meta[name="theme-color"]')?.remove();
    const meta = document.createElement('meta');
    meta.name = 'theme-color';
    meta.content = '#051f63';
    document.head.appendChild(meta);
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  const root = () => fixture.nativeElement as HTMLElement;
  // Visible wording, with line breaks and indentation collapsed to single spaces.
  const text = () => (root().textContent ?? '').replace(/\s+/g, ' ');

  it('is headed Sign in', () => {
    expect(root().querySelector('h1')?.textContent?.trim()).toBe('Sign in');
    expect(text()).not.toContain('NXPass');
  });

  it('shows the tournament title, both division dates and the venue', () => {
    expect(text()).toContain('78th PAL Interclub Golf Tournament 2027');
    expect(text()).toContain('Seniors Division: Feb 17–24, 2027');
    expect(text()).toContain('Men’s/Regular Division: Feb 25–Mar 4, 2027');
    expect(text()).toContain('Cagayan de Oro | Bukidnon');
  });

  it('leaves the "Powered by" line to the app footer', () => {
    expect(text()).not.toContain('Powered by');
  });

  it('asks for a username or email and a password', () => {
    const user = root().querySelector<HTMLInputElement>('input[name="email"]');
    const password = root().querySelector<HTMLInputElement>('input[name="password"]');
    expect(user?.autocomplete).toBe('username');
    expect(password?.type).toBe('password');
    expect(password?.autocomplete).toBe('current-password');
  });

  it('keeps Sign in disabled until both fields are filled', async () => {
    const submit = root().querySelector<HTMLButtonElement>('button[type="submit"]')!;
    expect(submit.textContent).toContain('Sign in');
    expect(submit.disabled).toBe(true);

    for (const [name, value] of [['email', 'admin'], ['password', 'secret-1']]) {
      const input = root().querySelector<HTMLInputElement>(`input[name="${name}"]`)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(submit.disabled).toBe(false);
  });

  it('shows Sign in as working, not refused, while signing in', () => {
    fixture.componentInstance.loading.set(true);
    fixture.detectChanges();

    const submit = root().querySelector<HTMLButtonElement>('button[type="submit"]')!;
    expect(submit.textContent).toContain('Signing in');
    expect(submit.classList.contains('is-loading')).toBe(true);
  });

  it('tells screen readers when signing in starts', () => {
    const status = root().querySelector('[role="status"]')!;
    expect(status.textContent?.trim()).toBe('');

    fixture.componentInstance.loading.set(true);
    fixture.detectChanges();

    expect(status.textContent?.trim()).toBe('Signing in…');
  });

  it('colours the phone toolbar like the page, and puts it back on leaving', () => {
    const toolbar = () => document.querySelector('meta[name="theme-color"]')?.getAttribute('content');
    expect(toolbar()).toBe('#1a5aa8');

    fixture.destroy();

    expect(toolbar()).toBe('#051f63');
  });

  it('links to participant account creation', () => {
    const link = root().querySelector<HTMLAnchorElement>('a[href="/register"]');
    expect(link?.textContent).toContain('Create a participant account');
  });
});
