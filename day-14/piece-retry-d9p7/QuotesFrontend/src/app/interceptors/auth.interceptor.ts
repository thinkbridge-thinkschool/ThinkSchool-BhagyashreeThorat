import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth/auth.service';

// Single place that attaches the JWT to outgoing requests. Protected endpoints
// (e.g. POST /api/quotes) automatically get `Authorization: Bearer <token>`.
// When no token is present (e.g. the login request itself, or anonymous
// browsing), the request passes through untouched.
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = inject(AuthService).getAccessToken();
  if (!token) {
    return next(req);
  }
  const authReq = req.clone({
    setHeaders: { Authorization: `Bearer ${token}` },
  });
  return next(authReq);
};
