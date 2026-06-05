import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { LoginRequest, LoginResponse } from '../../models/auth.model';
import { environment } from '../../../environments/environment';

// localStorage keys — namespaced to avoid clashes with other apps on the host.
const ACCESS_TOKEN_KEY = 'quotes.accessToken';
const REFRESH_TOKEN_KEY = 'quotes.refreshToken';

// Owns ALL auth state. The tokens are the single source of truth; the
// interceptor and the route guard read from here, so no component touches
// localStorage or builds Authorization headers itself.
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBase}/api/auth`;

  // Seeded from localStorage so a page refresh keeps the user signed in.
  private readonly accessToken = signal<string | null>(
    localStorage.getItem(ACCESS_TOKEN_KEY),
  );
  private readonly refreshToken = signal<string | null>(
    localStorage.getItem(REFRESH_TOKEN_KEY),
  );

  // Public, reactive auth flag — used by the guard and any UI that cares.
  readonly isAuthenticated = computed(() => this.accessToken() !== null);

  // Plain read for the interceptor (no reactive context there).
  getAccessToken(): string | null {
    return this.accessToken();
  }

  // POST /api/auth/login -> persists the session as a side effect, returns the
  // response so the caller can react (navigate) or surface an error.
  login(credentials: LoginRequest): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(`${this.baseUrl}/login`, credentials)
      .pipe(tap((response) => this.setSession(response)));
  }

  // Clears tokens from both signal state and storage.
  logout(): void {
    this.accessToken.set(null);
    this.refreshToken.set(null);
    localStorage.removeItem(ACCESS_TOKEN_KEY);
    localStorage.removeItem(REFRESH_TOKEN_KEY);
  }

  private setSession(response: LoginResponse): void {
    this.accessToken.set(response.accessToken);
    this.refreshToken.set(response.refreshToken);
    localStorage.setItem(ACCESS_TOKEN_KEY, response.accessToken);
    localStorage.setItem(REFRESH_TOKEN_KEY, response.refreshToken);
  }
}
