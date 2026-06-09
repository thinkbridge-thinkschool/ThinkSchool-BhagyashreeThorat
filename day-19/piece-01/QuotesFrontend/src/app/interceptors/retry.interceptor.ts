import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { retry, timer } from 'rxjs';

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
          throw error;
        }
        const backoffMs = BASE_DELAY_MS * Math.pow(2, retryCount - 1);
        return timer(backoffMs);
      },
    }),
  );
};
