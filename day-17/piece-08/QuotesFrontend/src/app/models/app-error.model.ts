// Typed application error. The HTTP layer (error-mapping interceptor) converts
// every transport-level failure into ONE of these, so components never touch
// HttpErrorResponse, ProblemDetails or ValidationProblemDetails shapes directly.
//
// Grounded in the REAL Week-1 backend — VERIFIED LIVE via curl (2026-06-03):
//   • POST /api/quotes domain fail -> 400 { error: "<message>" }   (legacy shape;
//       NOT ProblemDetails). e.g. { "error":"Author must be between 1 and 200 characters." }
//   • Collection/domain rule violations -> 400 application/problem+json
//       ProblemDetails { status, title:"Domain Rule Violation", detail }  (no `errors`)
//   • 401 Unauthorized (WWW-Authenticate: Bearer) / 404 NotFound -> EMPTY body
//   • Bad/missing query params or unparsable JSON -> 500 ProblemDetails
//       { title:"Internal Server Error", status:500, detail:"Failed to bind ..." }
//       (the minimal-API binding error is swallowed by ExceptionMiddleware's
//        generic catch -> there is NO ValidationProblemDetails from this API today)
// The `errors`-dict branch below is kept as DEFENSIVE handling in case any
// endpoint ever returns a true ValidationProblemDetails, but is not emitted now.

export type AppErrorType =
  | 'validation' // 400 with field errors or a domain message the user can act on
  | 'unauthorized' // 401 - not signed in / token expired
  | 'forbidden' // 403 - signed in but not allowed
  | 'notFound' // 404
  | 'conflict' // 409
  | 'client' // any other 4xx
  | 'server' // 5xx
  | 'network' // status 0 - request never reached the server
  | 'unknown'; // anything we couldn't classify

export interface AppError {
  /** Coarse category the UI can branch on. */
  readonly type: AppErrorType;
  /** Safe, friendly, user-facing message. Never a raw stack/exception string. */
  readonly message: string;
  /** Original HTTP status (0 for network failures). */
  readonly status: number;
  /**
   * Diagnostic payload preserved for logging/debugging — the raw parsed body
   * (ProblemDetails / ValidationProblemDetails / {error} / etc.). NOT shown to users.
   */
  readonly details?: unknown;
}

// Type guard so call-sites can do `if (isAppError(err))` after a failed request.
export function isAppError(value: unknown): value is AppError {
  return (
    typeof value === 'object' &&
    value !== null &&
    'type' in value &&
    'message' in value &&
    'status' in value
  );
}
