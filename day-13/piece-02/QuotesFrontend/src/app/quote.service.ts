import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { CreateQuote, Quote, QuoteDetail } from './quote.model';
import { environment } from '../environments/environment';

// Thin HTTP layer over the REAL Week-1 endpoints.
// IMPORTANT: this service holds NO state — no signals, no fields caching data.
// It only performs I/O and hands back a cold Observable per request. All state
// lives as signals in the component.
@Injectable({ providedIn: 'root' })
export class QuoteService {
  private readonly http = inject(HttpClient);

  // apiBase: 'http://localhost:5005' in dev, '' (same-origin) in prod.
  private readonly baseUrl = `${environment.apiBase}/api/quotes`;

  // GET /api/quotes?page=1&size=50  -> Quote[]
  getQuotes(page = 1, size = 50): Observable<Quote[]> {
    const params = new HttpParams().set('page', page).set('size', size);
    return this.http.get<Quote[]>(this.baseUrl, { params });
  }

  // GET /api/quotes/{id} -> QuoteDetail
  getQuoteById(id: number): Observable<QuoteDetail> {
    return this.http.get<QuoteDetail>(`${this.baseUrl}/${id}`);
  }

  // POST /api/quotes  body { author, text } -> Quote
  createQuote(payload: CreateQuote): Observable<Quote> {
    return this.http.post<Quote>(this.baseUrl, payload);
  }

  // DELETE /api/quotes/{id} -> 204 No Content
  deleteQuote(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
