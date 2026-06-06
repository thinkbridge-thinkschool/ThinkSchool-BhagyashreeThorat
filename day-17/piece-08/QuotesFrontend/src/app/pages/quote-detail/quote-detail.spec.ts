import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { ActivatedRoute, convertToParamMap, ParamMap } from '@angular/router';
import { ReplaySubject } from 'rxjs';
import { QuoteDetail } from './quote-detail';
import { errorMappingInterceptor } from '../../interceptors/error-mapping.interceptor';
import { environment } from '../../../environments/environment';

// Route-param proof for the lazy detail page. The real errorMappingInterceptor
// is wired in (as app.config does) so a 404 reaches the component as a typed
// AppError end-to-end — no hand-faked error shapes.
describe('QuoteDetail page — route params', () => {
  let httpMock: HttpTestingController;
  let paramMap$: ReplaySubject<ParamMap>;
  const QUOTES_URL = `${environment.apiBase}/api/quotes`;

  function setParam(id: string | null) {
    paramMap$.next(convertToParamMap(id === null ? {} : { id }));
  }

  function createWith(id: string | null) {
    paramMap$ = new ReplaySubject<ParamMap>(1);
    setParam(id);
    TestBed.configureTestingModule({
      imports: [QuoteDetail],
      providers: [
        provideHttpClient(withInterceptors([errorMappingInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap: paramMap$.asObservable() } },
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(QuoteDetail);
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => httpMock.verify());

  it('VALID id: calls GET /api/quotes/{id} and renders the quote', () => {
    const fixture = createWith('7');

    const req = httpMock.expectOne(`${QUOTES_URL}/7`);
    expect(req.request.method).toBe('GET');
    req.flush({
      id: 7,
      author: 'Socrates',
      text: 'Know thyself.',
      isDeleted: false,
      ownerId: 3,
      authorId: 2,
      authorRef: null,
    });
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    // Explicit element-level DOM assertions (not just textContent.contains):
    // the quote TEXT is in the <blockquote>, the AUTHOR is in the <cite>.
    const article = el.querySelector('article.detail');
    expect(article).not.toBeNull();
    expect(el.querySelector('article.detail blockquote')?.textContent?.trim())
      .toBe('Know thyself.');
    expect(el.querySelector('article.detail cite')?.textContent)
      .toContain('Socrates');
    // Real detail fields rendered into the <dl>.
    const dd = Array.from(el.querySelectorAll('article.detail dd')).map(
      (n) => n.textContent?.trim(),
    );
    expect(dd).toContain('7'); // ID
    expect(dd).toContain('2'); // Author ID
  });

  it('INVALID id (non-numeric): renders "Invalid quote id." and makes NO API call', () => {
    const fixture = createWith('abc');

    // No request must be issued for a param that can't be the real int id.
    httpMock.expectNone(() => true);

    expect(fixture.nativeElement.textContent).toContain('Invalid quote id.');
  });

  it('MISSING param: renders "Invalid quote id." and makes NO API call', () => {
    const fixture = createWith(null);

    httpMock.expectNone(() => true);

    expect(fixture.nativeElement.textContent).toContain('Invalid quote id.');
  });

  it('NOT-FOUND (API 404): renders "Quote not found."', () => {
    const fixture = createWith('999');

    const req = httpMock.expectOne(`${QUOTES_URL}/999`);
    // Real contract: 404 with empty body.
    req.flush(null, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Quote not found.');
  });
});
