import { Route } from '@angular/router';
import { routes } from './app.routes';
import { authGuard } from '../guards/auth.guard';

// CHARACTERIZATION + new-feature pins for the route table.
// Presence-based assertions (not array-length) so ADDING the lazy detail route
// keeps the existing pins green.
function find(path: string): Route | undefined {
  return routes.find((r) => r.path === path);
}

describe('app routes', () => {
  // ---- EXISTING behavior (pinned before the change) ----
  it('serves the Quotes page at the empty path with NO guard (public home)', () => {
    const home = find('');
    expect(home).toBeDefined();
    expect(home?.component).toBeDefined();
    expect(home?.canActivate ?? []).toHaveLength(0);
  });

  it('protects /admin and /admin/signal with the functional authGuard', () => {
    expect(find('admin')?.canActivate).toContain(authGuard);
    expect(find('admin/signal')?.canActivate).toContain(authGuard);
  });

  it('falls back unknown paths to the home route', () => {
    const wildcard = find('**');
    expect(wildcard?.redirectTo).toBe('');
  });

  // ---- NEW feature pins ----
  it('exposes a LAZY quote detail route on the real :id param with NO guard', () => {
    const detail = find('quotes/:id');
    expect(detail).toBeDefined();
    // Lazy = loadComponent (dynamic import), not an eager `component`.
    expect(typeof detail?.loadComponent).toBe('function');
    expect(detail?.component).toBeUndefined();
    // Public route — backend GET is anonymous, so no guard here.
    expect(detail?.canActivate ?? []).toHaveLength(0);
  });

  it('keeps the lazy detail route ahead of the wildcard so :id is reachable', () => {
    const detailIdx = routes.findIndex((r) => r.path === 'quotes/:id');
    const wildcardIdx = routes.findIndex((r) => r.path === '**');
    expect(detailIdx).toBeGreaterThanOrEqual(0);
    expect(detailIdx).toBeLessThan(wildcardIdx);
  });
});
