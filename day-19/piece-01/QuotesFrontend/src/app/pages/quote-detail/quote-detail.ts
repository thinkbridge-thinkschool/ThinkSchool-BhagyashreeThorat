import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, map, of, startWith, switchMap } from 'rxjs';
import { QuoteService } from '../../services/quote/quote.service';
import { QuoteDetail as QuoteDetailModel } from '../../models/quote.model';
import { isAppError } from '../../models/app-error.model';

type DetailState =
  | { kind: 'loading' }
  | { kind: 'invalid'; raw: string | null }
  | { kind: 'notFound'; id: number }
  | { kind: 'error'; message: string }
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

  protected asLoaded(s: DetailState) {
    return s as Extract<DetailState, { kind: 'loaded' }>;
  }
  protected asError(s: DetailState) {
    return s as Extract<DetailState, { kind: 'error' }>;
  }

  constructor() {
    this.route.paramMap
      .pipe(
        map((pm) => pm.get('id')),
        switchMap((raw) => {
          const id = Number(raw);
          const valid =
            raw !== null && raw.trim() !== '' && Number.isInteger(id) && id > 0;

          if (!valid) {
            return of<DetailState>({ kind: 'invalid', raw });
          }

          return this.quoteService.getQuoteById(id).pipe(
            map((quote) => ({ kind: 'loaded', quote }) as DetailState),
            catchError((err: unknown) => {
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
