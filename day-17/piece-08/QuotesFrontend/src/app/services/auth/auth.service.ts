import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { LoginRequest, LoginResponse } from '../../models/auth.model';
import { environment } from '../../../environments/environment';

const ACCESS_TOKEN_KEY = 'quotes.accessToken';
const REFRESH_TOKEN_KEY = 'quotes.refreshToken';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBase}/api/auth`;

  private readonly accessToken = signal<string | null>(
    localStorage.getItem(ACCESS_TOKEN_KEY),
  );
  private readonly refreshToken = signal<string | null>(
    localStorage.getItem(REFRESH_TOKEN_KEY),
  );

  readonly isAuthenticated = computed(() => this.accessToken() !== null);

  getAccessToken(): string | null {
    return this.accessToken();
  }

  login(credentials: LoginRequest): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(`${this.baseUrl}/login`, credentials)
      .pipe(tap((response) => this.setSession(response)));
  }

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
