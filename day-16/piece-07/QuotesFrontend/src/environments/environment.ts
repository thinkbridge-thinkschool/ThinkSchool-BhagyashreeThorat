// PRODUCTION defaults (used by the default `production` build).
// Empty apiBase => same-origin relative calls ("/api/quotes"), which is the
// usual prod setup where the SPA is served from the same host as the API.
// If your prod API lives on another host, set its origin here.
export const environment = {
  production: true,
  apiBase: '',
};
