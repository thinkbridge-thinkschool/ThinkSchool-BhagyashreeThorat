import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth/auth.service';

const AUTH_EXCLUDED = ['/api/auth/'];

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (AUTH_EXCLUDED.some((path) => req.url.includes(path))) {
    return next(req);
  }
  const token = inject(AuthService).getAccessToken();
  if (!token) {
    return next(req);
  }
  const authReq = req.clone({
    setHeaders: { Authorization: `Bearer ${token}` },
  });
  return next(authReq);
};
