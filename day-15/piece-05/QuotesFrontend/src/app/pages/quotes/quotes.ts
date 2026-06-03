import {
  Component,
  ElementRef,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import {
  Observable,
  Subject,
  catchError,
  debounceTime,
  distinctUntilChanged,
  map,
  merge,
  of,
  switchMap,
  tap,
} from 'rxjs';
import { QuoteService } from '../../services/quote/quote.service';
import { Quote, QuoteDetail } from '../../models/quote.model';
import { isAppError } from '../../models/app-error.model';
import { AdminLoginModal } from '../../components/admin-login-modal/admin-login-modal';

@Component({
  selector: 'app-quotes',
  standalone: true,
  imports: [AdminLoginModal],
  templateUrl: './quotes.html',
  styleUrl: './quotes.css',
})
export class Quotes {
  // inject() everywhere — no constructor injection.
  private readonly quoteService = inject(QuoteService);
  private readonly router = inject(Router);

  // --- ADMIN LOGIN MODAL ---
  // The Admin button opens an in-page dialog instead of navigating to a route.
  protected readonly showLogin = signal(false);
  // Native ref to the Admin button so focus returns there when the modal closes
  // (a11y: focus should not get lost on the page body).
  private readonly adminButton =
    viewChild<ElementRef<HTMLButtonElement>>('adminButton');

  protected openLogin(): void {
    this.showLogin.set(true);
  }

  protected closeLogin(): void {
    this.showLogin.set(false);
    this.adminButton()?.nativeElement.focus();
  }

  protected onLoggedIn(): void {
    // Tokens are already stored by AuthService; close the popup and go to /admin.
    this.showLogin.set(false);
    this.router.navigate(['/admin']);
  }

  // --- PAGINATION ---
  // The list is paged; search results respect the same page/size. (No page UI
  // existed before; these stay constants so the contract is preserved and a
  // pager can be wired into the stream later without touching the service.)
  private readonly page = 1;
  private readonly size = 50;

  // --- LIST state ---
  protected readonly quotes = signal<Quote[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  // --- SEARCH state ---
  // The term drives a debounced SERVER request (no in-memory filtering).
  protected readonly searchTerm = signal('');

  // --- DETAIL state ---
  protected readonly selectedQuote = signal<QuoteDetail | null>(null);
  protected readonly selectedQuoteLoading = signal(false);
  protected readonly selectedQuoteError = signal<string | null>(null);

  protected readonly selectedQuoteId = computed(
    () => this.selectedQuote()?.id ?? null,
  );

  // Empty = no rows AND no active search (genuinely nothing to show).
  protected readonly isEmpty = computed(
    () =>
      !this.loading() &&
      !this.error() &&
      this.quotes().length === 0 &&
      this.searchTerm().trim().length === 0,
  );

  // No matches = no rows but a search IS active (backend found nothing).
  protected readonly noMatches = computed(
    () =>
      !this.loading() &&
      !this.error() &&
      this.quotes().length === 0 &&
      this.searchTerm().trim().length > 0,
  );

  // Manual re-fetch trigger (e.g. the Retry button) — re-runs the current term.
  private readonly reload$ = new Subject<void>();

  // --- STALE-RESPONSE RACE GUARD (detail panel) ---
  private detailRequestToken = 0;

  constructor() {
    // Single source of quote loading. Typing is debounced + de-duped; switchMap
    // cancels any in-flight request when a newer term arrives, so only the
    // latest result lands. Empty term => normal (unfiltered) page.
    const typed$ = toObservable(this.searchTerm).pipe(
      debounceTime(300),
      distinctUntilChanged(),
    );
    const retry$ = this.reload$.pipe(map(() => this.searchTerm()));

    merge(typed$, retry$)
      .pipe(
        tap(() => {
          this.loading.set(true);
          this.error.set(null);
        }),
        switchMap((term) => this.fetchQuotes(term)),
        takeUntilDestroyed(),
      )
      .subscribe((data) => {
        this.quotes.set(data);
        this.loading.set(false);
      });
  }

  // The ONE place that calls the quote list API. Used by initial load, search,
  // and retry alike — no duplicated loading logic.
  private fetchQuotes(term: string): Observable<Quote[]> {
    const trimmed = term.trim();
    return this.quoteService
      .getQuotes(this.page, this.size, trimmed || undefined)
      .pipe(
        catchError((err: unknown) => {
          // Surface the interceptor's friendly AppError.message; fall back to a
          // generic line only if a non-AppError ever slips through.
          this.error.set(isAppError(err) ? err.message : 'Failed to load quotes.');
          this.loading.set(false);
          return of<Quote[]>([]);
        }),
      );
  }

  retry(): void {
    this.reload$.next();
  }

  selectQuote(id: number): void {
    const token = ++this.detailRequestToken;

    this.selectedQuoteLoading.set(true);
    this.selectedQuoteError.set(null);

    this.quoteService.getQuoteById(id).subscribe({
      next: (detail) => {
        if (token !== this.detailRequestToken) {
          return;
        }
        this.selectedQuote.set(detail);
        this.selectedQuoteLoading.set(false);
      },
      error: (err: unknown) => {
        if (token !== this.detailRequestToken) {
          return;
        }
        this.selectedQuote.set(null);
        this.selectedQuoteError.set(
          isAppError(err) ? err.message : 'Failed to load quote detail.',
        );
        this.selectedQuoteLoading.set(false);
      },
    });
  }

  deleteQuote(id: number): void {
    this.quoteService.deleteQuote(id).subscribe({
      next: () => {
        this.quotes.update((list) => list.filter((q) => q.id !== id));
        if (this.selectedQuote()?.id === id) {
          this.detailRequestToken++;
          this.selectedQuote.set(null);
          this.selectedQuoteLoading.set(false);
          this.selectedQuoteError.set(null);
        }
      },
      error: (err: unknown) =>
        this.error.set(isAppError(err) ? err.message : 'Failed to delete quote.'),
    });
  }
}
