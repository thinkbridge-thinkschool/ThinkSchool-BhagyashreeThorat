import { vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideRouter, Router, withViewTransitions } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from './app.routes';
import { QuoteDetail } from '../pages/quote-detail/quote-detail';
import { AuthService } from '../services/auth/auth.service';
import { errorMappingInterceptor } from '../interceptors/error-mapping.interceptor';
import { environment } from '../../environments/environment';

const QUOTES_URL = `${environment.apiBase}/api/quotes`;

// END-TO-END routing proof: drives the REAL route table (lazy loadComponent +
// withViewTransitions) through a real Router so we observe loading/redirect/
// transition at navigation time — not just static config or isolated units.
describe('Routing integration (real router, real routes)', () => {
  function configure(isAuthed: boolean) {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorMappingInterceptor])),
        provideHttpClientTesting(),
        provideRouter(routes, withViewTransitions()),
        { provide: AuthService, useValue: { isAuthenticated: () => isAuthed } },
      ],
    });
  }

  // ── LAZY LOAD HAPPENS ON NAVIGATION ──────────────────────────────────────
  it('lazy-loads QuoteDetail WHEN navigating to /quotes/7 and renders the quote', async () => {
    configure(true);
    const httpMock = TestBed.inject(HttpTestingController);

    // The 2nd arg makes the harness ASSERT the routed component is QuoteDetail.
    // Reaching this line means the router executed `loadComponent: () =>
    // import(...)` during navigation and instantiated the lazy component.
    const harness = await RouterTestingHarness.create();
    const instance = await harness.navigateByUrl('/quotes/7', QuoteDetail);
    expect(instance).toBeInstanceOf(QuoteDetail); // lazy chunk loaded on nav

    // The GET fired from the lazily-created component's constructor.
    const req = httpMock.expectOne(`${QUOTES_URL}/7`);
    expect(req.request.method).toBe('GET');
    req.flush({
      id: 7, author: 'Socrates', text: 'Know thyself.',
      isDeleted: false, ownerId: 3, authorId: 2, authorRef: null,
    });
    harness.detectChanges();

    const el = harness.routeNativeElement!;
    expect(el.querySelector('article.detail blockquote')?.textContent?.trim())
      .toBe('Know thyself.');
    expect(el.querySelector('article.detail cite')?.textContent)
      .toContain('Socrates');

    httpMock.verify();
  });

  // ── GUARD REDIRECT: protected component is NOT activated ──────────────────
  it('redirects /admin -> / when logged out and does NOT render the Admin component', async () => {
    configure(false);

    const router = TestBed.inject(Router);
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/admin');

    // URL evidence: ended on the public home, not /admin.
    expect(router.url).toBe('/');

    // Component evidence: the Admin create-quote form is ABSENT; the public
    // Quotes page (its Admin-login button) is what rendered instead.
    const el = harness.routeNativeElement!;
    expect(el.querySelector('input#author')).toBeNull(); // Admin form absent
    expect(el.querySelector('button.admin-link')).not.toBeNull(); // home present
  });

  // ── VIEW TRANSITION ACTUALLY RUNS on list -> detail ───────────────────────
  it('invokes document.startViewTransition when navigating list -> detail', async () => {
    // Spy stands in for the browser API (absent in jsdom). withViewTransitions
    // calls it on navigation; calling the callback lets the DOM update proceed.
    const spy = vi.fn((cb: () => unknown) => {
      const done = Promise.resolve(cb());
      return {
        finished: Promise.resolve(),
        ready: Promise.resolve(),
        updateCallbackDone: done,
        skipTransition: () => {},
      };
    });
    (document as unknown as { startViewTransition: unknown }).startViewTransition = spy;

    configure(true);
    const httpMock = TestBed.inject(HttpTestingController);

    const harness = await RouterTestingHarness.create('/'); // quotes LIST
    spy.mockClear(); // ignore the initial navigation

    await harness.navigateByUrl('/quotes/7', QuoteDetail); // -> DETAIL
    // Evidence: the view-transition code path executed for list -> detail.
    expect(spy).toHaveBeenCalled();

    httpMock.expectOne(`${QUOTES_URL}/7`).flush({
      id: 7, author: 'Socrates', text: 'Know thyself.',
      isDeleted: false, ownerId: 3, authorId: 2, authorRef: null,
    });

    delete (document as unknown as { startViewTransition?: unknown }).startViewTransition;
  });
});
