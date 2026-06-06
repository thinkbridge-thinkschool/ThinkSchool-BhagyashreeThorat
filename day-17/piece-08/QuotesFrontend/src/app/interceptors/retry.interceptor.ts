import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { retry, timer } from 'rxjs';

// Retry policy for TRANSIENT failures on SAFE requests only.
//
// Why GET-only: GET is the only verb in this API that is idempotent AND has no
// side effects (POST/PUT/PATCH/DELETE all mutate — retrying a POST /api/quotes
// could create duplicate quotes, retrying DELETE could mask a real 404). So we
// retry GET and pass every other verb straight through untouched.
//
// MAX_RETRIES = 2  (=> up to 3 total attempts).
//   Rationale: a single retry rides out the common case (one dropped packet, a
//   brief cold-start 503). A second covers a slightly longer blip. Beyond that,
//   the failure is almost certainly real (server down, bad request) and more
//   attempts just amplify load during an incident, so we stop and surface it.
//
// Backoff: exponential, 300ms * 2^(n-1) => 300ms, then 600ms. Spreads the two
// retries out instead of hammering the server back-to-back.
//
// We ONLY retry transient statuses: 0 (network/CORS/never-reached), 408
// (request timeout), 429 (too many requests), and 5xx. A 4xx is a deterministic
// client error (bad params, validation, auth) — retrying it would fail
// identically every time, so we rethrow immediately without spending a retry.
const MAX_RETRIES = 2;
const BASE_DELAY_MS = 300;

function isTransient(status: number): boolean {
  return status === 0 || status === 408 || status === 429 || status >= 500;
}

export const retryInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method !== 'GET') {
    return next(req);
  }

  return next(req).pipe(
    retry({
      count: MAX_RETRIES,
      delay: (error, retryCount) => {
        const status = error instanceof HttpErrorResponse ? error.status : 0;
        if (!isTransient(status)) {
          // Non-transient (e.g. 400/401/404) — abandon retries, propagate now.
          throw error;
        }
        const backoffMs = BASE_DELAY_MS * Math.pow(2, retryCount - 1);
        return timer(backoffMs);
      },
    }),
  );
};
