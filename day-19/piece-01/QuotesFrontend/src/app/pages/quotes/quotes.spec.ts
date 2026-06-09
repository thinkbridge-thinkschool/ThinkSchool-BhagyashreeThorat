import { vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { Quotes } from './quotes';
import { errorMappingInterceptor } from '../../interceptors/error-mapping.interceptor';
import { environment } from '../../../environments/environment';

describe('Quotes page', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Quotes],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
  });

  it('creates the component', () => {
    const fixture = TestBed.createComponent(Quotes);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('renders an Admin button that opens the login popup (no create form on homepage)', () => {
    const fixture = TestBed.createComponent(Quotes);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;

    // Admin is now a button (opens a modal), not a link to /login.
    const adminButton = el.querySelector('button.admin-link') as HTMLButtonElement;
    expect(adminButton).not.toBeNull();

    // The modal is not mounted until the button is clicked.
    expect(el.querySelector('app-admin-login-modal')).toBeNull();
    adminButton.click();
    fixture.detectChanges();
    expect(el.querySelector('app-admin-login-modal')).not.toBeNull();

    // The create form must NOT live on the public homepage.
    expect(el.querySelector('form.create')).toBeNull();
  });

  it('exposes a labelled search input', () => {
    const fixture = TestBed.createComponent(Quotes);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;

    const input = el.querySelector('#quote-search');
    const label = el.querySelector('label[for="quote-search"]');
    expect(input).not.toBeNull();
    expect(label).not.toBeNull();
  });
});

// ============================================================================
// COMPONENT-LEVEL state proof: loading / empty / error are RENDERED in the DOM,
// and a failed request surfaces the interceptor's friendly AppError.message.
// The real errorMappingInterceptor is wired in (as in app.config.ts) so the
// failure reaches the component as a typed AppError end-to-end.
// ============================================================================
describe('Quotes page — list states (loading / empty / error)', () => {
  let httpMock: HttpTestingController;
  const QUOTES_URL = `${environment.apiBase}/api/quotes`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Quotes],
      providers: [
        provideHttpClient(withInterceptors([errorMappingInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
    // Fake timers keep the constructor's 300ms debounced initial load from
    // racing the test; we drive a single deterministic load via retry().
    vi.useFakeTimers();
  });

  afterEach(() => vi.useRealTimers());

  // retry() emits with no debounce -> one synchronous, deterministic GET.
  function triggerLoad(fixture: ReturnType<typeof TestBed.createComponent<Quotes>>) {
    fixture.detectChanges(); // constructor subscribes to the stream
    fixture.componentInstance.retry();
    fixture.detectChanges();
    return httpMock.expectOne((r) => r.url === QUOTES_URL);
  }

  it('LOADING: shows "Loading quotes…" while the request is in flight', () => {
    const fixture = TestBed.createComponent(Quotes);
    const req = triggerLoad(fixture);
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelector('.status')?.textContent).toContain('Loading quotes');

    req.flush([]); // cleanup
  });

  it('EMPTY: renders "No quotes yet." when the API returns []', () => {
    const fixture = TestBed.createComponent(Quotes);
    const req = triggerLoad(fixture);

    req.flush([]); // real contract: bare empty array
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('No quotes yet.');
    expect(el.querySelector('.quote-list')).toBeNull();
  });

  it('ERROR: surfaces the interceptor friendly AppError.message + a Retry button', () => {
    const fixture = TestBed.createComponent(Quotes);
    const req = triggerLoad(fixture);

    // Real quotes 4xx shape (verified live): 400 { error: "<message>" }.
    req.flush(
      { error: 'Author must be between 1 and 200 characters.' },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    const errEl = fixture.nativeElement.querySelector('.error') as HTMLElement;
    expect(errEl).not.toBeNull();
    // The SERVER's friendly message is rendered — not a hardcoded fallback.
    expect(errEl.textContent).toContain('Author must be between 1 and 200 characters.');
    expect(errEl.querySelector('button')?.textContent).toContain('Retry');
  });

  it('SUCCESS: renders the list when rows come back', () => {
    const fixture = TestBed.createComponent(Quotes);
    const req = triggerLoad(fixture);

    req.flush([{ id: 1, author: 'Socrates', text: 'Know thyself.' }]);
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelectorAll('.quote-list .quote')).toHaveLength(1);
    expect(el.textContent).toContain('Know thyself.');
  });
});
