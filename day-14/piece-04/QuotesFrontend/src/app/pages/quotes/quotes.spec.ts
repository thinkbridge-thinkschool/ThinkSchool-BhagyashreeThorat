import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { Quotes } from './quotes';

describe('Quotes page', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Quotes],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
  });

  it('creates the component', () => {
    const fixture = TestBed.createComponent(Quotes);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('renders an Admin button that opens the login popup (no create form on homepage)', () => {
    const fixture = TestBed.createComponent(Quotes);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;

    // Admin is now a button (opens a modal), not a link to /login.
    const adminButton = el.querySelector('button.admin-link') as HTMLButtonElement;
    expect(adminButton).not.toBeNull();

    // The modal is not mounted until the button is clicked.
    expect(el.querySelector('app-admin-login-modal')).toBeNull();
    adminButton.click();
    fixture.detectChanges();
    expect(el.querySelector('app-admin-login-modal')).not.toBeNull();

    // The create form must NOT live on the public homepage.
    expect(el.querySelector('form.create')).toBeNull();
  });

  it('exposes a labelled search input', () => {
    const fixture = TestBed.createComponent(Quotes);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;

    const input = el.querySelector('#quote-search');
    const label = el.querySelector('label[for="quote-search"]');
    expect(input).not.toBeNull();
    expect(label).not.toBeNull();
  });
});
