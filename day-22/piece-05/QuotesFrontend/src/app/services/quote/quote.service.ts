import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { CreateQuote, Quote, QuoteDetail } from '../../models/quote.model';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class QuoteService {
  private readonly http = inject(HttpClient);

  private readonly baseUrl = `${environment.apiBase}/api/quotes`;

  getQuotes(page = 1, size = 50, search?: string): Observable<Quote[]> {
    let params = new HttpParams().set('page', page).set('size', size);
    if (search) {
      params = params.set('search', search);
    }
    return this.http.get<Quote[]>(this.baseUrl, { params });
  }

  getQuoteById(id: number): Observable<QuoteDetail> {
    return this.http.get<QuoteDetail>(`${this.baseUrl}/${id}`);
  }

  createQuote(payload: CreateQuote): Observable<Quote> {
    return this.http.post<Quote>(this.baseUrl, payload);
  }

  deleteQuote(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
