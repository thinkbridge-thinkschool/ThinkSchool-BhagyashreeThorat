import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { CreateQuote, Quote } from './quote.model';
import { environment } from '../environments/environment';

// Thin HTTP wrapper over the REAL Week 1 endpoints. It does NOT hold state —
// state lives as signals in the component. The service only does I/O and hands
// back the raw Observable for a single request, which the component subscribes
// to once and immediately funnels into signals.
@Injectable({ providedIn: 'root' })
export class QuoteService {
  private readonly http = inject(HttpClient);

  // apiBase comes from the environment: '' in prod (same-origin) or
  // 'http://localhost:5005' in dev. Path matches your contract: /api/quotes.
  private readonly baseUrl = `${environment.apiBase}/api/quotes`;

  // GET /api/quotes REQUIRES `page` and `size` query params (the API 500s
  // without them). Returns a raw array of quotes.
  getQuotes(page = 1, size = 50): Observable<Quote[]> {
    const params = new HttpParams()
      .set('page', page)
      .set('size', size);
    return this.http.get<Quote[]>(this.baseUrl, { params });
  }

  addQuote(payload: CreateQuote): Observable<Quote> {
    return this.http.post<Quote>(this.baseUrl, payload);
  }

  deleteQuote(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
