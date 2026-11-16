import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService } from './auth.service';
import { AuthResponse } from './models';

// A signed-in session whose token never expires (exp far in the future).
function session(role = 'Participant'): AuthResponse {
  const payload = btoa(JSON.stringify({ exp: 4102444800 }));
  return { token: `h.${payload}.s`, role } as AuthResponse;
}

describe("the signed-in player's team category", () => {
  let http: HttpTestingController;

  function signInAs(role: string): AuthService {
    localStorage.setItem('evt_auth', JSON.stringify(session(role)));
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    return TestBed.inject(AuthService);
  }

  afterEach(() => {
    http.verify();
    localStorage.clear();
  });

  it.each(['Participant', 'Captain'])("is the category of a %s's team", async (role) => {
    const auth = signInAs(role);
    const category = auth.myTeamCategory();
    http.expectOne((r) => r.url.endsWith('/api/teams/mine/category')).flush({ category: 'Senior' });

    expect(await category).toBe('Senior');
  });

  it('is null for a player who is not on a team', async () => {
    const auth = signInAs('Participant');
    const category = auth.myTeamCategory();
    http
      .expectOne((r) => r.url.endsWith('/api/teams/mine/category'))
      .flush(null, { status: 204, statusText: 'No Content' });

    expect(await category).toBeNull();
  });

  it('is null when the team cannot be loaded', async () => {
    const auth = signInAs('Participant');
    const category = auth.myTeamCategory();
    http
      .expectOne((r) => r.url.endsWith('/api/teams/mine/category'))
      .flush(null, { status: 0, statusText: 'offline' });

    expect(await category).toBeNull();
  });

  it('is null without a request for roles that have no team', async () => {
    const auth = signInAs('Admin');

    expect(await auth.myTeamCategory()).toBeNull();
  });

  it('is null without a request when nobody is signed in', async () => {
    const auth = signInAs('Participant');
    auth.logout();

    expect(await auth.myTeamCategory()).toBeNull();
  });
});
