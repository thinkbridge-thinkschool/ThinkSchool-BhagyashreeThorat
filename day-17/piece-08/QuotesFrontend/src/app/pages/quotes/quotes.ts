import {
  Component,
  ElementRef,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
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
  imports: [AdminLoginModal, RouterLink],
  templateUrl: './quotes.html',
  styleUrl: './quotes.css',
})
export class Quotes {
  private readonly quoteService = inject(QuoteService);
  private readonly router = inject(Router);

  protected readonly showLogin = signal(false);
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
    this.showLogin.set(false);
    this.router.navigate(['/admin']);
  }

  private readonly page = 1;
  private readonly size = 50;

  protected readonly quotes = signal<Quote[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly searchTerm = signal('');

  protected readonly selectedQuote = signal<QuoteDetail | null>(null);
  protected readonly selectedQuoteLoading = signal(false);
  protected readonly selectedQuoteError = signal<string | null>(null);

  protected readonly selectedQuoteId = computed(
    () => this.selectedQuote()?.id ?? null,
  );

  protected readonly isEmpty = computed(
    () =>
      !this.loading() &&
      !this.error() &&
      this.quotes().length === 0 &&
      this.searchTerm().trim().length === 0,
  );

  protected readonly noMatches = computed(
    () =>
      !this.loading() &&
      !this.error() &&
      this.quotes().length === 0 &&
      this.searchTerm().trim().length > 0,
  );

  private readonly reload$ = new Subject<void>();

  private detailRequestToken = 0;

  constructor() {
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

  private fetchQuotes(term: string): Observable<Quote[]> {
    const trimmed = term.trim();
    return this.quoteService
      .getQuotes(this.page, this.size, trimmed || undefined)
      .pipe(
        catchError((err: unknown) => {
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
