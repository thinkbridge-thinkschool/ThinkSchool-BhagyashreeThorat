import { Injectable, computed, inject, signal } from '@angular/core';
import { QuoteService } from './quote.service';
import { Quote } from '../../models/quote.model';
import { isAppError } from '../../models/app-error.model';

// ============================================================================
// QuotesStore — SIGNAL-FIRST state for the quotes LIST feature.
//
// This extracts the list/search/loading/error state that currently lives
// inline in pages/quotes/quotes.ts into a reusable, testable store. It uses
// signals + computed ONLY — no NgRx, no @ngrx/signals signalStore. (See the
// recommendation in the PR notes for when this project should graduate.)
//
// Grounded in the REAL Week-1 contract (verified by reading the backend):
//   • GET /api/quotes?page=N&size=N[&search=]  (EndpointExtensions.cs)
//   • Returns a BARE ARRAY of { id, author, text } — NO {items,total} envelope
//       (GetQuotesHandler.cs -> Results.Ok(List<QuoteListItemDto>))
//   • search runs server-side as SQL LIKE (case-insensitive by collation);
//     a whole-number term ALSO matches the quote Id (GetQuotesHandler.cs:54)
//   • soft-deleted rows are excluded server-side (WHERE !IsDeleted)
// Because the response has NO total, this store CANNOT expose a page count or
// "hasMore" — that would be inventing a field the contract doesn't return.
// ============================================================================

export type QuotesStatus = 'idle' | 'loading' | 'success' | 'error';

@Injectable({ providedIn: 'root' })
export class QuotesStore {
  private readonly quoteService = inject(QuoteService);

  // Mirrors the constants the existing Quotes page used. Kept private; the
  // store owns paging so callers don't have to thread page/size around.
  private readonly page = 1;
  private readonly size = 50;

  // ---- private writable state (only the store mutates these) ----
  private readonly _quotes = signal<Quote[]>([]);
  private readonly _status = signal<QuotesStatus>('idle');
  private readonly _error = signal<string | null>(null);
  private readonly _term = signal('');

  // Monotonic request id. A response is only allowed to commit if its token
  // still equals the latest load()'s token — this is the STALE-RESPONSE guard
  // for out-of-order/concurrent requests.
  private requestToken = 0;

  // ---- public read-only views ----
  readonly quotes = this._quotes.asReadonly();
  readonly status = this._status.asReadonly();
  readonly error = this._error.asReadonly();
  readonly term = this._term.asReadonly();

  // INITIAL load: a request is in flight AND there is nothing on screen yet.
  readonly loading = computed(
    () => this._status() === 'loading' && this._quotes().length === 0,
  );

  // REFRESH: a request is in flight but we ALREADY have rows — keep them
  // visible (no content flash) and show a subtle "refreshing" hint instead.
  readonly refreshing = computed(
    () => this._status() === 'loading' && this._quotes().length > 0,
  );

  readonly isError = computed(() => this._status() === 'error');

  // EMPTY: settled success, zero rows, and NO active search => "no quotes yet".
  readonly isEmpty = computed(
    () =>
      this._status() === 'success' &&
      this._quotes().length === 0 &&
      this._term().trim().length === 0,
  );

  // NO MATCHES: settled success, zero rows, but a search IS active => the
  // backend simply found nothing. Semantically different from EMPTY.
  readonly noMatches = computed(
    () =>
      this._status() === 'success' &&
      this._quotes().length === 0 &&
      this._term().trim().length > 0,
  );

  /**
   * Load (or reload) the list for the given term. Defaults to the current term
   * so reload() / retry() re-run the active search.
   */
  load(term: string = this._term()): void {
    this._term.set(term);
    const token = ++this.requestToken;

    this._status.set('loading');
    this._error.set(null);

    // NOTE: we deliberately do NOT clear _quotes here. Wiping the list on every
    // load makes a refresh/search flash an empty screen and breaks refreshing()
    // (the store would report loading() instead). Existing rows stay visible
    // until the new response commits in next(); the stale-guard guarantees only
    // the latest response replaces them. See the "wrong assumption" PR note.

    const trimmed = term.trim();
    this.quoteService.getQuotes(this.page, this.size, trimmed || undefined).subscribe({
      next: (data) => {
        if (token !== this.requestToken) return; // superseded by a newer load()
        this._quotes.set(data);
        this._status.set('success');
      },
      error: (err: unknown) => {
        if (token !== this.requestToken) return; // stale failure — ignore
        this._error.set(
          isAppError(err) ? err.message : 'Failed to load quotes.',
        );
        this._status.set('error');
      },
    });
  }

  /** Re-run the current term (Retry button / pull-to-refresh). */
  reload(): void {
    this.load(this._term());
  }

  /** Set a new search term and load. */
  search(term: string): void {
    this.load(term);
  }

  /**
   * Optimistic local removal after a successful DELETE elsewhere — mirrors the
   * existing page's delete success path so the row disappears without a refetch.
   */
  removeLocally(id: number): void {
    this._quotes.update((list) => list.filter((q) => q.id !== id));
  }
}
