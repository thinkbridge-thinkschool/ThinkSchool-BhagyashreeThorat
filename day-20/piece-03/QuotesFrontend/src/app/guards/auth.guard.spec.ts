import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';
import { provideRouter } from '@angular/router';
import { authGuard } from './auth.guard';
import { AuthService } from '../services/auth/auth.service';

// CHARACTERIZATION — pins the EXISTING functional guard behavior so refactors
// can't silently change it:
//   authenticated   -> true (route allowed)
//   unauthenticated  -> UrlTree('/') (redirect into the public home, no loop)
// The guard is a CanActivateFn, so it must run inside an injection context.
describe('authGuard (existing behavior)', () => {
  function runGuard(isAuthed: boolean): boolean | UrlTree {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: { isAuthenticated: () => isAuthed },
        },
      ],
    });
    // CanActivateFn reads route/state args; the guard ignores them, so two
    // empty casts are safe and keep the call shape honest.
    // The guard is synchronous (returns boolean | UrlTree), so the cast is safe.
    return TestBed.runInInjectionContext(
      () => authGuard({} as never, {} as never),
    ) as boolean | UrlTree;
  }

  it('allows activation when authenticated', () => {
    expect(runGuard(true)).toBe(true);
  });

  it('redirects to the public home ("/") when unauthenticated', () => {
    const result = runGuard(false);
    expect(result).toBeInstanceOf(UrlTree);

    const router = TestBed.inject(Router);
    // Redirect target is '/', which has NO guard -> cannot loop.
    expect(router.serializeUrl(result as UrlTree)).toBe('/');
  });
});
