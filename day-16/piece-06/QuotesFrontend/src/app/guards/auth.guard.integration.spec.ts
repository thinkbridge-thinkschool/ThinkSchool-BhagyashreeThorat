import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { routes } from '../routing/app.routes';
import { AuthService } from '../services/auth/auth.service';

// INTEGRATION proof for the guard — drives the REAL route table through a real
// Router.navigate and records the URL before/after, so the redirect is observed
// end-to-end (not just the guard's return value).
describe('authGuard — real navigation through the route table', () => {
  function setup(isAuthed: boolean) {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(routes),
        { provide: AuthService, useValue: { isAuthenticated: () => isAuthed } },
      ],
    });
    return TestBed.inject(Router);
  }

  it('UNAUTHENTICATED: navigating to /admin redirects to / (public home)', async () => {
    const router = setup(false);

    const urlBefore = router.url; // '/' — nothing navigated yet
    // Guard returns a UrlTree, so the navigation REDIRECTS (and that redirect
    // succeeds -> resolves true); the proof is the final URL, not the boolean.
    const succeeded = await router.navigateByUrl('/admin');

    expect(succeeded).toBe(true); // redirect navigation itself completed
    expect(router.url).toBe('/'); // landed on the public home, NOT /admin
    expect(urlBefore).toBe('/');
  });

  it('AUTHENTICATED: navigating to /admin is allowed', async () => {
    const router = setup(true);

    const succeeded = await router.navigateByUrl('/admin');

    expect(succeeded).toBe(true);
    expect(router.url).toBe('/admin');
  });
});
