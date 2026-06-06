import { Routes } from '@angular/router';
import { Quotes } from '../pages/quotes/quotes';
import { Admin } from '../pages/admin/admin';
import { CreateQuoteSignal } from '../pages/create-quote-signal/create-quote-signal';
import { authGuard } from '../guards/auth.guard';

export const routes: Routes = [
  // Public homepage: list + search + detail. Admin login is a popup here, not a
  // route — clicking "Admin" opens <app-admin-login-modal> in place.
  { path: '', component: Quotes },
  // Protected: only reachable with a valid token (guard redirects to '/').
  // Holds the create-quote form + logout.
  { path: 'admin', component: Admin, canActivate: [authGuard] },
  // Same Create Quote feature, rebuilt on the Signal Forms preview API. Shares
  // the same QuoteService, models, and auth guard as the reactive `admin` page.
  {
    path: 'admin/signal',
    component: CreateQuoteSignal,
    canActivate: [authGuard],
  },
  // Public, deep-linkable quote detail. LAZY: the component is in its own bundle
  // (loadComponent => dynamic import) so it isn't part of the initial chunk and
  // only downloads when this route is first hit. The param is the REAL backend
  // identifier — `id` (a positive int; backend route is /api/quotes/{id:int}).
  // No guard: the backend GET is anonymous, so the detail page is public too.
  {
    path: 'quotes/:id',
    loadComponent: () =>
      import('../pages/quote-detail/quote-detail').then((m) => m.QuoteDetail),
  },
  // Unknown paths fall back to the homepage.
  { path: '**', redirectTo: '' },
];
