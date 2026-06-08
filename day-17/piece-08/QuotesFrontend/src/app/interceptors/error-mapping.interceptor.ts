import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { AppError, AppErrorType } from '../models/app-error.model';

interface ProblemDetailsLike {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
  error?: string;
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

function friendlyMessage(type: AppErrorType, body: ProblemDetailsLike | null): string {
  if (body?.errors) {
    const firstField = Object.keys(body.errors)[0];
    const firstMsg = firstField ? body.errors[firstField]?.[0] : undefined;
    if (firstMsg) return firstMsg;
  }
  if (typeof body?.detail === 'string' && body.detail.trim()) return body.detail;
  if (typeof body?.error === 'string' && body.error.trim()) return body.error;

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
