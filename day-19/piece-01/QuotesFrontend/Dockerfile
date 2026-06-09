# ---------- Stage 1: build the Angular app ----------
# Pinned to Node 24 (same major used locally) so the build is reproducible and
# Oryx/Angular 21 picks a compatible toolchain.
FROM node:24-alpine AS build
WORKDIR /app

# Copy only manifests first so `npm ci` is cached unless dependencies change.
COPY package.json package-lock.json ./
RUN npm ci

# Copy the rest of the source and produce the optimized production bundle.
# Output lands in dist/QuotesFrontend/browser (Angular 21 application builder).
COPY . .
RUN npm run build -- --configuration production

# ---------- Stage 2: serve the static bundle with nginx ----------
FROM nginx:1.27-alpine

# Our SPA-aware config (history fallback + gzip + cache headers) replaces the
# stock default server block.
COPY nginx.conf /etc/nginx/conf.d/default.conf

# Ship only the compiled browser assets — no node_modules, no source.
COPY --from=build /app/dist/QuotesFrontend/browser /usr/share/nginx/html

# Container Apps ingress targets port 80.
EXPOSE 80
