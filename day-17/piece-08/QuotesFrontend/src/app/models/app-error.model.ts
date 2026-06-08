export type AppErrorType =
  | 'validation'
  | 'unauthorized'
  | 'forbidden'
  | 'notFound'
  | 'conflict'
  | 'client'
  | 'server'
  | 'network'
  | 'unknown';

export interface AppError {
  readonly type: AppErrorType;
  readonly message: string;
  readonly status: number;
  readonly details?: unknown;
}

export function isAppError(value: unknown): value is AppError {
  return (
    typeof value === 'object' &&
    value !== null &&
    'type' in value &&
    'message' in value &&
    'status' in value
  );
}
