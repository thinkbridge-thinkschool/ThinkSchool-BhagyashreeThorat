import {
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { QuoteService } from '../quote.service';
import { CreateQuote, Quote } from '../quote.model';

const SEARCH_KEY = 'quotes.searchTerm';

@Component({
  selector: 'app-quotes',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './quotes.html',
  styleUrl: './quotes.css',
})
export class Quotes {
  private readonly quoteService = inject(QuoteService);

  // --- SIGNAL #1: the source-of-truth list, loaded from the real API ---
  protected readonly quotes = signal<Quote[]>([]);

  // --- SIGNAL #2: the live search box value ---
  // Seeded from localStorage so a reload keeps the user's last search.
  protected readonly searchTerm = signal(
    localStorage.getItem(SEARCH_KEY) ?? '',
  );

  // UI status signals.
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  // --- COMPUTED: derived purely from the two signals above ---
  // Recomputes automatically whenever quotes() OR searchTerm() change.
  protected readonly filteredQuotes = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    const all = this.quotes();
    if (!term) {
      return all;
    }
    return all.filter(
      (q) =>
        q.text.toLowerCase().includes(term) ||
        q.author.toLowerCase().includes(term),
    );
  });

  constructor() {
    // --- EFFECT (meaningful): bridge a signal to an external system ---
    // Persists the search term to localStorage every time it changes. An effect
    // is the right tool because we are reacting to signal state by performing a
    // side-effect on something OUTSIDE Angular (the browser storage API) — that
    // is exactly what effects are for. It must NOT live in computed() (computed
    // is for pure derived values, side-effects there are a bug).
    effect(() => {
      localStorage.setItem(SEARCH_KEY, this.searchTerm());
    });

    // Kick off the initial load.
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

  addQuote(text: string, author: string): void {
    const payload: CreateQuote = {
      text: text.trim(),
      author: author.trim(),
    };
    if (!payload.text || !payload.author) {
      return;
    }
    this.quoteService.addQuote(payload).subscribe({
      next: (created) => this.quotes.update((list) => [...list, created]),
      error: () => this.error.set('Failed to add quote.'),
    });
  }

  removeQuote(id: number): void {
    this.quoteService.deleteQuote(id).subscribe({
      next: () => this.quotes.update((list) => list.filter((q) => q.id !== id)),
      error: () => this.error.set('Failed to delete quote.'),
    });
  }
}
