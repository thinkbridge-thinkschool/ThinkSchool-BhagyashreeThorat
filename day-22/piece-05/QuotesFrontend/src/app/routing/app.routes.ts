import { Routes } from '@angular/router';
import { Quotes } from '../pages/quotes/quotes';
import { Admin } from '../pages/admin/admin';
import { CreateQuoteSignal } from '../pages/create-quote-signal/create-quote-signal';
import { authGuard } from '../guards/auth.guard';

export const routes: Routes = [
  { path: '', component: Quotes },
  { path: 'admin', component: Admin, canActivate: [authGuard] },
  {
    path: 'admin/signal',
    component: CreateQuoteSignal,
    canActivate: [authGuard],
  },
  {
    path: 'quotes/:id',
    loadComponent: () =>
      import('../pages/quote-detail/quote-detail').then((m) => m.QuoteDetail),
  },
  { path: '**', redirectTo: '' },
];
