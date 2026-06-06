import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { QuotesStore } from './quotes-store';
import { errorMappingInterceptor } from '../../interceptors/error-mapping.interceptor';
import { environment } from '../../../environments/environment';

// ============================================================================
// QuotesStore behaviour proof. The REAL errorMappingInterceptor is wired in
// (exactly as app.config.ts does) so a failed request reaches the store as a
// typed AppError end-to-end — no hand-rolled error shape.
//
// In unit tests no fileReplacement runs, so environment.apiBase === '' and the
// service baseUrl resolves to the relative path '/api/quotes'.
// ============================================================================
describe('QuotesStore — signal-based list state', () => {
  let store: QuotesStore;
  let httpMock: HttpTestingController;
  const QUOTES_URL = `${environment.apiBase}/api/quotes`;

  const ROWS = [
    { id: 1, author: 'Socrates', text: 'Know thyself.' },
    { id: 2, author: 'Aristotle', text: 'We are what we repeatedly do.' },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorMappingInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    store = TestBed.inject(QuotesStore);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  // --- CHARACTERIZATION: pin the real request shape ----------------------
  it('CONTRACT: load() GETs /api/quotes with page=1&size=50 and no search', () => {
    store.load();

    const req = httpMock.expectOne((r) => r.method === 'GET' && r.url === QUOTES_URL);
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('size')).toBe('50');
    expect(req.request.params.has('search')).toBe(false);

    req.flush([]);
  });

  it('CONTRACT: search(term) forwards ?search= verbatim', () => {
    store.search('plato');

    const req = httpMock.expectOne((r) => r.url === QUOTES_URL);
    expect(req.request.params.get('search')).toBe('plato');

    req.flush([]);
  });

  // --- IDLE ---------------------------------------------------------------
  it('IDLE: starts idle with no data, no error, no flags set', () => {
    expect(store.status()).toBe('idle');
    expect(store.quotes()).toEqual([]);
    expect(store.loading()).toBe(false);
    expect(store.refreshing()).toBe(false);
    expect(store.isEmpty()).toBe(false); // idle != empty; empty needs a settled success
  });

  // --- LOADING (initial) --------------------------------------------------
  it('LOADING: first load with no prior data -> loading()=true, refreshing()=false', () => {
    store.load();
    const req = httpMock.expectOne((r) => r.url === QUOTES_URL);

    expect(store.status()).toBe('loading');
    expect(store.loading()).toBe(true);
    expect(store.refreshing()).toBe(false);

    req.flush([]); // cleanup
  });

  // --- SUCCESS ------------------------------------------------------------
  it('SUCCESS: a bare array commits to quotes() and flips status to success', () => {
    store.load();
    httpMock.expectOne((r) => r.url === QUOTES_URL).flush(ROWS);

    expect(store.status()).toBe('success');
    expect(store.quotes()).toHaveLength(2);
    expect(store.quotes()[0]).toEqual({ id: 1, author: 'Socrates', text: 'Know thyself.' });
    expect(store.loading()).toBe(false);
    expect(store.isEmpty()).toBe(false);
    expect(store.noMatches()).toBe(false);
  });

  // --- EMPTY vs NO-MATCHES ------------------------------------------------
  it('EMPTY: [] with no search term -> isEmpty()=true, noMatches()=false', () => {
    store.load();
    httpMock.expectOne((r) => r.url === QUOTES_URL).flush([]);

    expect(store.isEmpty()).toBe(true);
    expect(store.noMatches()).toBe(false);
  });

  it('NO MATCHES: [] WHILE a search is active -> noMatches()=true, isEmpty()=false', () => {
    store.search('zzzznotfound');
    httpMock.expectOne((r) => r.url === QUOTES_URL).flush([]);

    expect(store.noMatches()).toBe(true);
    expect(store.isEmpty()).toBe(false);
  });

  // --- ERROR (through the real interceptor) -------------------------------
  it('ERROR: a 400 { error } becomes the interceptor friendly message + isError()=true', () => {
    store.load();
    // Real quotes 4xx shape (verified live in the existing characterization spec).
    httpMock
      .expectOne((r) => r.url === QUOTES_URL)
      .flush(
        { error: 'Author must be between 1 and 200 characters.' },
        { status: 400, statusText: 'Bad Request' },
      );

    expect(store.isError()).toBe(true);
    expect(store.status()).toBe('error');
    expect(store.error()).toBe('Author must be between 1 and 200 characters.');
    expect(store.loading()).toBe(false);
  });

  // --- REFRESHING (the bug-catcher) ---------------------------------------
  it('REFRESHING: reloading WITH data already on screen -> refreshing()=true, loading()=false, rows stay visible', () => {
    // First successful load.
    store.load();
    httpMock.expectOne((r) => r.url === QUOTES_URL).flush(ROWS);
    expect(store.quotes()).toHaveLength(2);

    // Now reload — a request is in flight again, but we should NOT flash empty.
    store.reload();
    const req = httpMock.expectOne((r) => r.url === QUOTES_URL);

    expect(store.refreshing()).toBe(true); // in-flight WITH existing rows
    expect(store.loading()).toBe(false); // NOT the initial-load spinner
    expect(store.quotes()).toHaveLength(2); // old rows remain visible

    req.flush([{ id: 9, author: 'Plato', text: 'Wisdom begins in wonder.' }]);
    expect(store.quotes()).toHaveLength(1);
    expect(store.status()).toBe('success');
  });

  // --- STALE / CONCURRENT -------------------------------------------------
  it('STALE: out-of-order responses -> only the LATEST load() commits', () => {
    store.search('a'); // token 1
    store.search('b'); // token 2 (latest)

    const reqs = httpMock.match((r) => r.url === QUOTES_URL);
    expect(reqs).toHaveLength(2);

    // Respond to the LATEST ('b') first, then the stale ('a') arrives late.
    reqs[1].flush([{ id: 2, author: 'B-author', text: 'B quote' }]);
    reqs[0].flush([{ id: 1, author: 'A-author', text: 'A quote (stale)' }]);

    // The stale 'a' response must be DROPPED — we still show 'b'.
    expect(store.quotes()).toHaveLength(1);
    expect(store.quotes()[0].author).toBe('B-author');
    expect(store.term()).toBe('b');
    expect(store.status()).toBe('success');
  });
});
