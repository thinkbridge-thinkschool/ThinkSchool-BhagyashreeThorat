import { Routes } from '@angular/router';
import { Quotes } from '../pages/quotes/quotes';
import { Login } from '../pages/login/login';
import { Admin } from '../pages/admin/admin';
import { CreateQuotePage } from '../pages/create-quote/create-quote';
import { authGuard } from '../guards/auth.guard';

export const routes: Routes = [
  // Public homepage: list + search + detail.
  { path: '', component: Quotes },
  // Public login page.
  { path: 'login', component: Login },
  // Protected: only reachable with a valid token (guard redirects otherwise).
  { path: 'admin', component: Admin, canActivate: [authGuard] },
  // Protected: create-a-quote reactive form (POST /api/quotes is JWT-only).
  { path: 'quotes/new', component: CreateQuotePage, canActivate: [authGuard] },
  // Unknown paths fall back to the homepage.
  { path: '**', redirectTo: '' },
];
