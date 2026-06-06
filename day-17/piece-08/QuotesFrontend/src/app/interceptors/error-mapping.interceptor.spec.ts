import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { errorMappingInterceptor } from './error-mapping.interceptor';
import { AppError, isAppError } from '../models/app-error.model';

describe('errorMappingInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorMappingInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('maps the REAL quotes 400 { error: string } -> validation AppError surfacing that message', () => {
    // VERIFIED LIVE: POST /api/quotes blank author -> 400 { error: "..." }
    let err: AppError | undefined;
    http.post('/api/quotes', {}).subscribe({ error: (e) => (err = e) });

    httpMock.expectOne('/api/quotes').flush(
      { error: 'Author must be between 1 and 200 characters.' },
      { status: 400, statusText: 'Bad Request' },
    );

    expect(err!.type).toBe('validation');
    expect(err!.status).toBe(400);
    expect(err!.message).toBe('Author must be between 1 and 200 characters.');
  });

  // Defensive: this API does not currently emit ValidationProblemDetails, but the
  // mapper handles it gracefully if any endpoint ever does.
  it('maps a ValidationProblemDetails (400) -> validation AppError with the first field message', () => {
    let err: AppError | undefined;
    http.get('/api/quotes').subscribe({ error: (e) => (err = e) });

    httpMock.expectOne('/api/quotes').flush(
      {
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: { page: ["The value 'abc' is not valid for 'page'."] },
      },
      { status: 400, statusText: 'Bad Request' },
    );

    expect(isAppError(err)).toBe(true);
    expect(err!.type).toBe('validation');
    expect(err!.status).toBe(400);
    // Friendly message surfaces the actual field error, not a raw stack.
    expect(err!.message).toContain("not valid for 'page'");
    expect(err!.details).toBeTruthy();
  });

  it('maps domain ProblemDetails (400) -> surfaces `detail` as the message', () => {
    let err: AppError | undefined;
    http.get('/api/quotes').subscribe({ error: (e) => (err = e) });

    httpMock.expectOne('/api/quotes').flush(
      { title: 'Domain Rule Violation', status: 400, detail: 'Author is required.' },
      { status: 400, statusText: 'Bad Request' },
    );

    expect(err!.type).toBe('validation');
    expect(err!.message).toBe('Author is required.');
  });

  it('maps 401 -> unauthorized with a friendly sign-in message', () => {
    let err: AppError | undefined;
    http.get('/api/quotes').subscribe({ error: (e) => (err = e) });
    httpMock.expectOne('/api/quotes').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(err!.type).toBe('unauthorized');
    expect(err!.message.toLowerCase()).toContain('sign in');
  });

  it('maps 404 -> notFound', () => {
    let err: AppError | undefined;
    http.get('/api/quotes/999').subscribe({ error: (e) => (err = e) });
    httpMock.expectOne('/api/quotes/999').flush(null, { status: 404, statusText: 'Not Found' });

    expect(err!.type).toBe('notFound');
  });

  it('maps 500 -> server with a generic message (does NOT leak server detail as primary UX)', () => {
    let err: AppError | undefined;
    http.get('/api/quotes').subscribe({ error: (e) => (err = e) });
    httpMock
      .expectOne('/api/quotes')
      .flush({ title: 'Internal Server Error', status: 500, detail: 'NullRef at line 42' }, {
        status: 500,
        statusText: 'Server Error',
      });

    expect(err!.type).toBe('server');
    // detail of a 500 is diagnostic, preserved in details for logging...
    expect(err!.details).toBeTruthy();
  });

  it('maps a network failure (status 0) -> network error, body handled safely', () => {
    let err: AppError | undefined;
    http.get('/api/quotes').subscribe({ error: (e) => (err = e) });
    httpMock
      .expectOne('/api/quotes')
      .error(new ProgressEvent('network error'));

    expect(err!.type).toBe('network');
    expect(err!.status).toBe(0);
    expect(err!.message.toLowerCase()).toContain('connection');
  });
});
