import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { AuthService } from './auth.service';
import { LoginResponse } from '../../models/auth.model';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  it('starts unauthenticated with no token', () => {
    expect(service.isAuthenticated()).toBe(false);
    expect(service.getAccessToken()).toBeNull();
  });

  it('stores tokens and becomes authenticated after login', () => {
    const response: LoginResponse = {
      accessToken: 'access-123',
      refreshToken: 'refresh-456',
      expiresIn: 3600,
    };

    service
      .login({ email: 'admin@quotes.com', password: 'Password123!' })
      .subscribe();

    const req = httpMock.expectOne((r) => r.url.endsWith('/api/auth/login'));
    expect(req.request.method).toBe('POST');
    req.flush(response);

    expect(service.isAuthenticated()).toBe(true);
    expect(service.getAccessToken()).toBe('access-123');
    expect(localStorage.getItem('quotes.accessToken')).toBe('access-123');
  });

  it('clears tokens and auth state on logout', () => {
    service
      .login({ email: 'admin@quotes.com', password: 'Password123!' })
      .subscribe();
    httpMock
      .expectOne((r) => r.url.endsWith('/api/auth/login'))
      .flush({
        accessToken: 'a',
        refreshToken: 'b',
        expiresIn: 1,
      } satisfies LoginResponse);

    service.logout();

    expect(service.isAuthenticated()).toBe(false);
    expect(service.getAccessToken()).toBeNull();
    expect(localStorage.getItem('quotes.accessToken')).toBeNull();
  });
});
