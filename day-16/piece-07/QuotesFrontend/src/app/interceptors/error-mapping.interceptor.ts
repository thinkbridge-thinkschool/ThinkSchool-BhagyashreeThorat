import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { AppError, AppErrorType } from '../models/app-error.model';

// Translates every transport failure into a typed AppError (see app-error.model).
// Sits OUTSIDE the retry interceptor in the chain, so it only maps the FINAL
// error after retries are exhausted — it never converts an error that is still
// going to be retried.
//
// The friendly `message` is what the UI shows. For domain 400s the backend's
// own `detail` is already user-meaningful ("Author is required"), so we surface
// it; for everything else we use a safe generic line and keep the raw body in
// `details` for logging.

interface ProblemDetailsLike {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>; // ValidationProblemDetails
  error?: string; // legacy POST /api/quotes shape: { error: "..." }
}

function classify(status: number): AppErrorType {
  if (status === 0) return 'network';
  if (status >= 500) return 'server';
  switch (status) {
    case 401:
      return 'unauthorized';
    case 403:
      return 'forbidden';
    case 404:
      return 'notFound';
    case 409:
      return 'conflict';
    case 400:
    case 422:
      return 'validation';
    default:
      return status >= 400 ? 'client' : 'unknown';
  }
}

// Pull the most user-actionable message we can out of whatever the server sent,
// WITHOUT trusting any field to exist (handles unknown/empty bodies safely).
function friendlyMessage(type: AppErrorType, body: ProblemDetailsLike | null): string {
  // 1) Field validation errors (ValidationProblemDetails) -> show the first one.
  if (body?.errors) {
    const firstField = Object.keys(body.errors)[0];
    const firstMsg = firstField ? body.errors[firstField]?.[0] : undefined;
    if (firstMsg) return firstMsg;
  }
  // 2) Domain ProblemDetails `detail`, or legacy `{ error }` — both user-facing.
  if (typeof body?.detail === 'string' && body.detail.trim()) return body.detail;
  if (typeof body?.error === 'string' && body.error.trim()) return body.error;

  // 3) Fall back to a safe message keyed off the category.
  switch (type) {
    case 'network':
      return "Can't reach the server. Please check your connection and try again.";
    case 'unauthorized':
      return 'Your session has expired. Please sign in again.';
    case 'forbidden':
      return "You don't have permission to do that.";
    case 'notFound':
      return "We couldn't find what you were looking for.";
    case 'conflict':
      return 'That action conflicts with the current state. Please refresh and retry.';
    case 'validation':
    case 'client':
      return 'The request was invalid. Please check your input and try again.';
    case 'server':
      return 'Something went wrong on our end. Please try again shortly.';
    default:
      return 'An unexpected error occurred.';
  }
}

export const errorMappingInterceptor: HttpInterceptorFn = (req, next) => {
  return next(req).pipe(
    catchError((error: unknown) => {
      // Only HttpErrorResponse comes from the transport. Anything else (e.g. a
      // bug thrown in another interceptor) is wrapped as 'unknown' rather than
      // leaked raw to the component.
      if (!(error instanceof HttpErrorResponse)) {
        const appError: AppError = {
          type: 'unknown',
          message: 'An unexpected error occurred.',
          status: 0,
          details: error,
        };
        return throwError(() => appError);
      }

      const type = classify(error.status);
      // error.error is the parsed body (object for problem+json, string for text,
      // or a ProgressEvent for network failures). Only treat objects as a body.
      const body =
        error.error && typeof error.error === 'object' && !(error.error instanceof ProgressEvent)
          ? (error.error as ProblemDetailsLike)
          : null;

      const appError: AppError = {
        type,
        message: friendlyMessage(type, body),
        status: error.status,
        details: error.error,
      };
      return throwError(() => appError);
    }),
  );
};
