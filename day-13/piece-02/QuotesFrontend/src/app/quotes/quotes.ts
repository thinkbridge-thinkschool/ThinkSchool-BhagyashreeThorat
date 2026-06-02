import { Component, computed, inject, signal } from '@angular/core';
import { QuoteService } from '../quote.service';
import { CreateQuote, Quote, QuoteDetail } from '../quote.model';

@Component({
  selector: 'app-quotes',
  standalone: true,
  imports: [],
  templateUrl: './quotes.html',
  styleUrl: './quotes.css',
})
export class Quotes {
  // inject() everywhere — no constructor injection.
  private readonly quoteService = inject(QuoteService);

  // --- LIST state ---
  protected readonly quotes = signal<Quote[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  // --- DETAIL state ---
  protected readonly selectedQuote = signal<QuoteDetail | null>(null);
  protected readonly selectedQuoteLoading = signal(false);
  protected readonly selectedQuoteError = signal<string | null>(null);

  // Computed: which list row is currently highlighted as selected.
  protected readonly selectedQuoteId = computed(
    () => this.selectedQuote()?.id ?? null,
  );

  // Computed: list is empty AND not loading AND no error -> show empty state.
  protected readonly isEmpty = computed(
    () => !this.loading() && !this.error() && this.quotes().length === 0,
  );

  // --- STALE-RESPONSE RACE GUARD ---
  // A monotonically increasing token. Every detail request captures the token
  // value at dispatch time; when a response arrives we only apply it if its
  // token is still the latest. This is NOT state about the data — it is a
  // local concurrency cursor, so it stays as a plain field (not a signal).
  private detailRequestToken = 0;

  constructor() {
    this.loadQuotes();
  }

  loadQuotes(): void {
    this.loading.set(true);
    this.error.set(null);
    this.quoteService.getQuotes().subscribe({
      next: (data) => {
        this.quotes.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load quotes.');
        this.loading.set(false);
      },
    });
  }

  selectQuote(id: number): void {
    // Bump the token and remember THIS request's token.
    const token = ++this.detailRequestToken;

    this.selectedQuoteLoading.set(true);
    this.selectedQuoteError.set(null);

    this.quoteService.getQuoteById(id).subscribe({
      next: (detail) => {
        // Guard: if another selection happened after we fired, drop this
        // (stale) response so it can't overwrite the newer one.
        if (token !== this.detailRequestToken) {
          return;
        }
        this.selectedQuote.set(detail);
        this.selectedQuoteLoading.set(false);
      },
      error: () => {
        if (token !== this.detailRequestToken) {
          return;
        }
        this.selectedQuote.set(null);
        this.selectedQuoteError.set('Failed to load quote detail.');
        this.selectedQuoteLoading.set(false);
      },
    });
  }

  createQuote(author: string, text: string): void {
    const payload: CreateQuote = { author: author.trim(), text: text.trim() };
    if (!payload.author || !payload.text) {
      return;
    }
    this.quoteService.createQuote(payload).subscribe({
      next: (created) => this.quotes.update((list) => [...list, created]),
      error: () => this.error.set('Failed to create quote.'),
    });
  }

  deleteQuote(id: number): void {
    this.quoteService.deleteQuote(id).subscribe({
      next: () => {
        this.quotes.update((list) => list.filter((q) => q.id !== id));
        // If the open detail panel was for the deleted quote, clear it and
        // invalidate any in-flight detail request for it.
        if (this.selectedQuote()?.id === id) {
          this.detailRequestToken++;
          this.selectedQuote.set(null);
          this.selectedQuoteLoading.set(false);
          this.selectedQuoteError.set(null);
        }
      },
      error: () => this.error.set('Failed to delete quote.'),
    });
  }
}
