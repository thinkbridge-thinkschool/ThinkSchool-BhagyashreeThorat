import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth/auth.service';

// Protects /admin. Unauthenticated users are redirected to /login instead of
// being allowed through. Returns a UrlTree so the redirect is part of the same
// navigation (no flash of the protected page).
export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    return true;
  }
  return router.createUrlTree(['/login']);
};
