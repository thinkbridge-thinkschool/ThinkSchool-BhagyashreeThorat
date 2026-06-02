import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';

import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    // Explicit zoneless change detection — there is no zone.js dependency or
    // polyfill in this project, so signals/effects drive change detection.
    provideZonelessChangeDetection(),
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(),
    provideRouter(routes),
  ],
};
