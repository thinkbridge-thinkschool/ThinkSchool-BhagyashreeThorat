import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, map, of, startWith, switchMap } from 'rxjs';
import { QuoteService } from '../../services/quote/quote.service';
import { QuoteDetail as QuoteDetailModel } from '../../models/quote.model';
import { isAppError } from '../../models/app-error.model';

// Mutually-exclusive view states for the detail page. A discriminated union
// (instead of several boolean signals) makes "exactly one thing is shown"
// provable and keeps the template a single @switch.
type DetailState =
  | { kind: 'loading' }
  | { kind: 'invalid'; raw: string | null } // param missing / not a positive int
  | { kind: 'notFound'; id: number } // API returned 404
  | { kind: 'error'; message: string } // any other transport failure
  | { kind: 'loaded'; quote: QuoteDetailModel };

@Component({
  selector: 'app-quote-detail',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './quote-detail.html',
  styleUrl: './quote-detail.css',
})
export class QuoteDetail {
  private readonly route = inject(ActivatedRoute);
  private readonly quoteService = inject(QuoteService);

  protected readonly state = signal<DetailState>({ kind: 'loading' });

  // Template @switch branches on state().kind but Angular can't narrow the union
  // across @case, so these typed casts hand the right shape to each branch.
  protected asLoaded(s: DetailState) {
    return s as Extract<DetailState, { kind: 'loaded' }>;
  }
  protected asError(s: DetailState) {
    return s as Extract<DetailState, { kind: 'error' }>;
  }

  constructor() {
    // React to the :id param. switchMap means navigating /quotes/1 -> /quotes/2
    // (same component reused) cancels the in-flight request for the old id, so a
    // late response can't overwrite the new one — the same race guard the list
    // uses, expressed declaratively here.
    this.route.paramMap
      .pipe(
        map((pm) => pm.get('id')),
        switchMap((raw) => {
          // The REAL identifier is a positive integer (backend route is
          // {id:int}, Quote.Id is int). Validate BEFORE any API call so bad or
          // missing params never hit the network.
          const id = Number(raw);
          const valid =
            raw !== null && raw.trim() !== '' && Number.isInteger(id) && id > 0;

          if (!valid) {
            return of<DetailState>({ kind: 'invalid', raw });
          }

          return this.quoteService.getQuoteById(id).pipe(
            map((quote) => ({ kind: 'loaded', quote }) as DetailState),
            catchError((err: unknown) => {
              // 404 from the contract -> dedicated not-found view; the
              // interceptor has already classified it as type 'notFound'.
              if (isAppError(err) && err.type === 'notFound') {
                return of<DetailState>({ kind: 'notFound', id });
              }
              return of<DetailState>({
                kind: 'error',
                message: isAppError(err)
                  ? err.message
                  : 'Failed to load quote detail.',
              });
            }),
            startWith<DetailState>({ kind: 'loading' }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((next) => this.state.set(next));
  }
}
