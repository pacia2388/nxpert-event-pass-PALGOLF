import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, firstValueFrom, tap } from 'rxjs';
import { API_BASE } from './config';
import { AuthResponse, InviteResolveResult, VerifyEmailResult } from './models';

const STORAGE_KEY = 'evt_auth';

export interface RegisterBody {
  firstName: string;
  middleInitial?: string;
  lastName: string;
  senateNo: string;
  level: string;
  email: string;
  password: string;
  phone?: string;
  organization?: string;
  photo?: string | null;
  birthdate: string; // yyyy-MM-dd
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private readonly _auth = signal<AuthResponse | null>(this.load());

  readonly auth = this._auth.asReadonly();
  readonly isLoggedIn = computed(() => this._auth() !== null);
  readonly isAdmin = computed(() => {
    const r = this._auth()?.role;
    return r === 'Admin' || r === 'SuperAdmin';
  });
  // A merchant can only scan/redeem meal stubs.
  readonly isMerchant = computed(() => this._auth()?.role === 'Merchant');
  readonly isParticipant = computed(() => this._auth()?.role === 'Participant');
  readonly isCaptain = computed(() => this._auth()?.role === 'Captain');
  // The Voucher page is for Participants and Captains only.
  readonly canUseVoucher = computed(() => {
    const r = this._auth()?.role;
    return r === 'Participant' || r === 'Captain';
  });
  // Can this user confirm event registrations? Admins always can; others need the grant.
  readonly canConfirmRegistrations = computed(() => !!this._auth()?.canConfirmRegistrations);
  readonly canConfirm = computed(() => this.isAdmin() || this.canConfirmRegistrations());
  readonly fullName = computed(() => this._auth()?.fullName ?? '');
  readonly firstName = computed(() => this._auth()?.firstName ?? '');
  readonly middleInitial = computed(() => this._auth()?.middleInitial ?? '');
  readonly lastName = computed(() => this._auth()?.lastName ?? '');
  readonly senateNo = computed(() => this._auth()?.senateNo ?? '');
  readonly level = computed(() => this._auth()?.level ?? '');
  readonly photoUrl = computed(() => this._auth()?.photoUrl ?? null);
  readonly role = computed(() => this._auth()?.role ?? null);
  // True only when verification is switched on server-side AND this user hasn't verified.
  readonly needsVerification = computed(() => {
    const a = this._auth();
    return !!a && a.verificationRequired && !a.emailVerified;
  });

  get token(): string | null {
    return this._auth()?.token ?? null;
  }

  login(email: string, password: string): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${API_BASE}/api/auth/login`, { email, password })
      .pipe(tap((res) => this.store(res)));
  }

  register(body: RegisterBody): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${API_BASE}/api/auth/register`, body)
      .pipe(tap((res) => this.store(res)));
  }

  /** Completes the account a Captain created, from the emailed team registration link. */
  completeTeamRegistration(token: string, body: RegisterBody): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${API_BASE}/api/auth/team-registration/${encodeURIComponent(token)}`, body)
      .pipe(tap((res) => this.store(res)));
  }

  logout(): void {
    localStorage.removeItem(STORAGE_KEY);
    this._auth.set(null);
    this.birthdate = null;
  }

  // The signed-in user's birthdate (yyyy-MM-dd), for the event-category age warning. Fetched once
  // per session; a failed fetch isn't kept, so the next call retries. null = none on file, signed
  // out, or unavailable.
  private birthdate: Promise<string | null> | null = null;

  myBirthdate(): Promise<string | null> {
    if (!this._auth()) return Promise.resolve(null);
    return (this.birthdate ??= firstValueFrom(
      this.http.get<{ birthdate: string | null }>(`${API_BASE}/api/auth/me`),
    ).then(
      (p) => p.birthdate,
      () => {
        this.birthdate = null;
        return null;
      },
    ));
  }

  /** Opens an invitation magic-link. Auto-logs in when the invitee has an account. */
  resolveInvite(token: string): Observable<InviteResolveResult> {
    return this.http
      .post<InviteResolveResult>(`${API_BASE}/api/auth/invite/${token}`, {})
      .pipe(tap((res) => res.auth && this.store(res.auth)));
  }

  verifyEmail(token: string): Observable<VerifyEmailResult> {
    return this.http
      .post<VerifyEmailResult>(`${API_BASE}/api/auth/verify`, { token })
      .pipe(tap((res) => res.success && this.markVerified()));
  }

  resendVerification(): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${API_BASE}/api/auth/resend-verification`, {});
  }

  /** Reflect a successful verification in the locally-stored session. */
  markVerified(): void {
    const a = this._auth();
    if (a) this.store({ ...a, emailVerified: true });
  }

  /** Replace the stored session after a self-service profile update (name/photo/etc.). */
  applyProfileUpdate(res: AuthResponse): void {
    this.store(res);
  }

  private store(res: AuthResponse): void {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(res));
    this._auth.set(res);
    this.birthdate = null; // new session or edited profile
  }

  private load(): AuthResponse | null {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return null;
    try {
      const res = JSON.parse(raw) as AuthResponse;
      // A cached session whose JWT has expired counts as signed out (avoids home -> 401 -> login bounce).
      const exp = JSON.parse(atob(res.token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/'))).exp;
      if (typeof exp === 'number' && exp * 1000 <= Date.now()) {
        localStorage.removeItem(STORAGE_KEY);
        return null;
      }
      return res;
    } catch {
      return null;
    }
  }
}
