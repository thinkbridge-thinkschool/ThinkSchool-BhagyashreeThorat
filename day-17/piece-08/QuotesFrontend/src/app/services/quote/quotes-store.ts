import { Injectable, computed, inject, signal } from '@angular/core';
import { QuoteService } from './quote.service';
import { Quote } from '../../models/quote.model';
import { isAppError } from '../../models/app-error.model';

export type QuotesStatus = 'idle' | 'loading' | 'success' | 'error';

@Injectable({ providedIn: 'root' })
export class QuotesStore {
  private readonly quoteService = inject(QuoteService);

  private readonly page = 1;
  private readonly size = 50;

  private readonly _quotes = signal<Quote[]>([]);
  private readonly _status = signal<QuotesStatus>('idle');
  private readonly _error = signal<string | null>(null);
  private readonly _term = signal('');

  private requestToken = 0;

  readonly quotes = this._quotes.asReadonly();
  readonly status = this._status.asReadonly();
  readonly error = this._error.asReadonly();
  readonly term = this._term.asReadonly();

  readonly loading = computed(
    () => this._status() === 'loading' && this._quotes().length === 0,
  );

  readonly refreshing = computed(
    () => this._status() === 'loading' && this._quotes().length > 0,
  );

  readonly isError = computed(() => this._status() === 'error');

  readonly isEmpty = computed(
    () =>
      this._status() === 'success' &&
      this._quotes().length === 0 &&
      this._term().trim().length === 0,
  );

  readonly noMatches = computed(
    () =>
      this._status() === 'success' &&
      this._quotes().length === 0 &&
      this._term().trim().length > 0,
  );

  load(term: string = this._term()): void {
    this._term.set(term);
    const token = ++this.requestToken;

    this._status.set('loading');
    this._error.set(null);

    const trimmed = term.trim();
    this.quoteService.getQuotes(this.page, this.size, trimmed || undefined).subscribe({
      next: (data) => {
        if (token !== this.requestToken) return;
        this._quotes.set(data);
        this._status.set('success');
      },
      error: (err: unknown) => {
        if (token !== this.requestToken) return;
        this._error.set(
          isAppError(err) ? err.message : 'Failed to load quotes.',
        );
        this._status.set('error');
      },
    });
  }

  reload(): void {
    this.load(this._term());
  }

  search(term: string): void {
    this.load(term);
  }

  removeLocally(id: number): void {
    this._quotes.update((list) => list.filter((q) => q.id !== id));
  }
}
