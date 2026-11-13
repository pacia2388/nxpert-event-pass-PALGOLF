import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService } from './auth.service';
import { AuthResponse } from './models';

// A signed-in session whose token never expires (exp far in the future).
function session(): AuthResponse {
  const payload = btoa(JSON.stringify({ exp: 4102444800 }));
  return { token: `h.${payload}.s` } as AuthResponse;
}

describe("the signed-in player's birthdate", () => {
  let auth: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.setItem('evt_auth', JSON.stringify(session()));
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
  });

  it('is fetched once per session', async () => {
    const first = auth.myBirthdate();
    http.expectOne((r) => r.url.endsWith('/api/auth/me')).flush({ birthdate: '1990-01-31' });

    expect(await first).toBe('1990-01-31');
    expect(await auth.myBirthdate()).toBe('1990-01-31');
    http.expectNone((r) => r.url.endsWith('/api/auth/me'));
  });

  it('is fetched again after a failed attempt', async () => {
    const failed = auth.myBirthdate();
    http.expectOne((r) => r.url.endsWith('/api/auth/me')).flush(null, { status: 0, statusText: 'offline' });
    expect(await failed).toBeNull();

    const retried = auth.myBirthdate();
    http.expectOne((r) => r.url.endsWith('/api/auth/me')).flush({ birthdate: '1950-01-02' });
    expect(await retried).toBe('1950-01-02');
  });

  it('is fetched again after the profile changes', async () => {
    const before = auth.myBirthdate();
    http.expectOne((r) => r.url.endsWith('/api/auth/me')).flush({ birthdate: '1990-01-31' });
    await before;

    auth.applyProfileUpdate(session());

    const after = auth.myBirthdate();
    http.expectOne((r) => r.url.endsWith('/api/auth/me')).flush({ birthdate: '1960-03-01' });
    expect(await after).toBe('1960-03-01');
  });

  it('is null without a request when nobody is signed in', async () => {
    auth.logout();

    expect(await auth.myBirthdate()).toBeNull();
  });
});
