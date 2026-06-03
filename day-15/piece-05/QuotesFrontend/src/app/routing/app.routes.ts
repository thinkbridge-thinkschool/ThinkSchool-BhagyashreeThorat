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
  // Unknown paths fall back to the homepage.
  { path: '**', redirectTo: '' },
];
