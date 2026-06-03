import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';

import { routes } from './routing/app.routes';
import { authInterceptor } from './interceptors/auth.interceptor';
import { retryInterceptor } from './interceptors/retry.interceptor';
import { errorMappingInterceptor } from './interceptors/error-mapping.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    // Explicit zoneless change detection — there is no zone.js dependency or
    // polyfill in this project, so signals/effects drive change detection.
    provideZonelessChangeDetection(),
    provideBrowserGlobalErrorListeners(),
    // Interceptor ORDER matters. Request flows top->bottom; the response/error
    // bubbles back bottom->top:
    //   1. authInterceptor          - attach Bearer (skips /api/auth/*)
    //   2. errorMappingInterceptor  - maps the FINAL error -> typed AppError
    //   3. retryInterceptor         - innermost; retries transient GETs, so the
    //      error-mapper only sees the error AFTER retries are exhausted.
    provideHttpClient(
      withInterceptors([authInterceptor, errorMappingInterceptor, retryInterceptor]),
    ),
    provideRouter(routes),
  ],
};
