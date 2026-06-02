import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth/auth.service';

// Protects /admin. There is no /login route anymore — login happens in a popup
// on the homepage — so unauthenticated users are sent back to '/' where they can
// open it. Returns a UrlTree so the redirect is part of the same navigation
// (no flash of the protected page).
export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    return true;
  }
  return router.createUrlTree(['/']);
};
