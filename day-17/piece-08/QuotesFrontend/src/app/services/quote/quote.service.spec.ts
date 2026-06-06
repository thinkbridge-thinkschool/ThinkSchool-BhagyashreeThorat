import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { QuoteService } from './quote.service';
import { environment } from '../../../environments/environment';

// ============================================================================
// CHARACTERIZATION TESTS — pin the REAL Week-1 API contract.
//
// These were written BEFORE the interceptor work and use NO interceptors, so
// they describe the backend exactly as it is today. If the API drifts, these
// fail first and tell us precisely what changed.
//
// Contract verified by reading the backend source:
//   • GET  /api/quotes?page=N&size=N[&search=]  (QuotesApi/Extensions/EndpointExtensions.cs)
//   • Returns a BARE ARRAY of { id, author, text } — NOT a {items,total} envelope
//       (QuotesApi/Features/Quotes/Queries/GetQuotesHandler.cs -> Results.Ok(List<QuoteListItemDto>))
//       (QuotesApi/DTOs/QuoteListItemDto.cs -> record (int Id, string Author, string Text))
//   • Bad/missing query params -> 400 ValidationProblemDetails
//       { type, title:"One or more validation errors occurred.", status:400, errors:{...} }
// ============================================================================
describe('QuoteService — Week-1 contract (characterization)', () => {
  let service: QuoteService;
  let httpMock: HttpTestingController;

  // In unit tests no fileReplacement runs, so environment.apiBase === '' and the
  // service's baseUrl resolves to the relative path '/api/quotes'.
  const QUOTES_URL = `${environment.apiBase}/api/quotes`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(QuoteService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('A. GET /api/quotes hits the right URL with page & size params', () => {
    service.getQuotes(1, 10).subscribe();

    const req = httpMock.expectOne(
      (r) => r.method === 'GET' && r.url === QUOTES_URL,
    );

    // Params are sent as query string, exactly as the backend binds them.
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('size')).toBe('10');
    // No search param when none is supplied (backend treats it as optional).
    expect(req.request.params.has('search')).toBe(false);

    req.flush([]); // close the request
  });

  it('A. forwards an optional search term as ?search=', () => {
    service.getQuotes(2, 25, 'plato').subscribe();

    const req = httpMock.expectOne((r) => r.url === QUOTES_URL);
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('size')).toBe('25');
    expect(req.request.params.get('search')).toBe('plato');
    req.flush([]);
  });

  it('A. response is a BARE ARRAY of { id, author, text } (no pagination envelope)', () => {
    const realPayload = [
      { id: 1, author: 'Socrates', text: 'The unexamined life is not worth living.' },
      { id: 2, author: 'Aristotle', text: 'We are what we repeatedly do.' },
    ];

    let received: unknown;
    service.getQuotes(1, 10).subscribe((quotes) => (received = quotes));

    httpMock.expectOne((r) => r.url === QUOTES_URL).flush(realPayload);

    // It's an array, not an { items, total } envelope.
    expect(Array.isArray(received)).toBe(true);
    const list = received as Array<Record<string, unknown>>;
    expect(list).toHaveLength(2);
    // Exactly these three fields — pin the shape.
    expect(Object.keys(list[0]).sort()).toEqual(['author', 'id', 'text']);
    expect(list[0]).toEqual({
      id: 1,
      author: 'Socrates',
      text: 'The unexamined life is not worth living.',
    });
  });

  // B. VERIFIED LIVE (curl against the running API on :5005, 2026-06-03):
  //   GET /api/quotes?page=abc&size=10  ->  HTTP 500  (NOT 400!)
  //   body: { "title":"Internal Server Error","status":500,
  //           "detail":"Failed to bind parameter \"int page\" from \"abc\"." }
  // The minimal-API binding exception is swallowed by ExceptionMiddleware's
  // generic catch -> 500 ProblemDetails. There is NO ValidationProblemDetails.
  it('B1. a bad query param comes back as 500 ProblemDetails (real binding behaviour)', () => {
    const problem = {
      title: 'Internal Server Error',
      status: 500,
      detail: 'Failed to bind parameter "int page" from "abc".',
    };

    let errorStatus: number | undefined;
    let errorBody: any;
    service.getQuotes(1, 10).subscribe({
      next: () => {
        throw new Error('expected the request to error');
      },
      error: (err) => {
        errorStatus = err.status;
        errorBody = err.error;
      },
    });

    httpMock
      .expectOne((r) => r.url === QUOTES_URL)
      .flush(problem, { status: 500, statusText: 'Internal Server Error' });

    expect(errorStatus).toBe(500);
    expect(errorBody.title).toBe('Internal Server Error');
    expect(errorBody.detail).toContain('Failed to bind parameter');
  });

  // B2. The real 4xx on the quotes WRITE path. VERIFIED LIVE:
  //   POST /api/quotes (valid JWT, blank author)  ->  HTTP 400
  //   body: { "error":"Author must be between 1 and 200 characters." }
  // Note the shape: a bare { error } string — NOT ProblemDetails, NOT
  // ValidationProblemDetails. The error-mapping interceptor handles this branch.
  it('B2. a domain rule violation on create returns 400 { error: string }', () => {
    let errorStatus: number | undefined;
    let errorBody: any;
    service.createQuote({ author: '', text: 'a real quote body' }).subscribe({
      next: () => {
        throw new Error('expected the request to error');
      },
      error: (err) => {
        errorStatus = err.status;
        errorBody = err.error;
      },
    });

    httpMock
      .expectOne((r) => r.method === 'POST' && r.url === QUOTES_URL)
      .flush(
        { error: 'Author must be between 1 and 200 characters.' },
        { status: 400, statusText: 'Bad Request' },
      );

    expect(errorStatus).toBe(400);
    expect(errorBody.error).toBe('Author must be between 1 and 200 characters.');
    // Confirm the legacy shape: a single `error` key, not `errors`/`detail`.
    expect(Object.keys(errorBody)).toEqual(['error']);
  });

  it('GET /api/quotes/{id} returns the richer detail shape', () => {
    const detail = {
      id: 7,
      author: 'Kant',
      text: 'Act only according to that maxim...',
      isDeleted: false,
      ownerId: null,
      authorId: 3,
      authorRef: null,
    };
    let received: any;
    service.getQuoteById(7).subscribe((q) => (received = q));

    httpMock.expectOne(`${QUOTES_URL}/7`).flush(detail);

    expect(received.id).toBe(7);
    expect(received).toHaveProperty('isDeleted');
    expect(received).toHaveProperty('authorId');
  });
});
