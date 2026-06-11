import { vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { retryInterceptor } from './retry.interceptor';

describe('retryInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([retryInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('retries an idempotent GET on 503 (2 retries, exp backoff) then succeeds', async () => {
    vi.useFakeTimers();
    let result: unknown;
    let errored = false;
    http.get<number[]>('/api/quotes').subscribe({
      next: (r) => (result = r),
      error: () => (errored = true),
    });

    // Attempt 1 -> 503
    httpMock.expectOne('/api/quotes').flush(null, { status: 503, statusText: 'Service Unavailable' });

    // After 300ms backoff -> attempt 2 -> 503
    await vi.advanceTimersByTimeAsync(300);
    httpMock.expectOne('/api/quotes').flush(null, { status: 503, statusText: 'Service Unavailable' });

    // After 600ms backoff -> attempt 3 -> success
    await vi.advanceTimersByTimeAsync(600);
    httpMock.expectOne('/api/quotes').flush([1, 2, 3]);

    expect(errored).toBe(false);
    expect(result).toEqual([1, 2, 3]);
  });

  it('gives up after 2 retries (3 attempts total) if the GET keeps failing', async () => {
    vi.useFakeTimers();
    let errorStatus: number | undefined;
    http.get('/api/quotes').subscribe({ error: (e) => (errorStatus = e.status) });

    httpMock.expectOne('/api/quotes').flush(null, { status: 500, statusText: 'Server Error' });
    await vi.advanceTimersByTimeAsync(300);
    httpMock.expectOne('/api/quotes').flush(null, { status: 500, statusText: 'Server Error' });
    await vi.advanceTimersByTimeAsync(600);
    httpMock.expectOne('/api/quotes').flush(null, { status: 500, statusText: 'Server Error' });

    // No 4th attempt — the next expectOne would fail verify() if one were made.
    expect(errorStatus).toBe(500);
  });

  it('does NOT retry a 4xx (deterministic client error) — fails on first attempt', () => {
    let errorStatus: number | undefined;
    http.get('/api/quotes').subscribe({ error: (e) => (errorStatus = e.status) });

    httpMock.expectOne('/api/quotes').flush(null, { status: 400, statusText: 'Bad Request' });
    // If a retry were scheduled, httpMock.verify() in afterEach would still pass,
    // but there'd be a pending timer; the key assertion is the error surfaces now.
    expect(errorStatus).toBe(400);
    httpMock.verify(); // no second request outstanding
  });

  it('does NOT retry a non-idempotent POST even on 503', () => {
    let errorStatus: number | undefined;
    http.post('/api/quotes', { author: 'x', text: 'y' }).subscribe({
      error: (e) => (errorStatus = e.status),
    });

    httpMock.expectOne('/api/quotes').flush(null, { status: 503, statusText: 'Service Unavailable' });
    expect(errorStatus).toBe(503); // surfaced immediately, no retry
    httpMock.verify();
  });
});
