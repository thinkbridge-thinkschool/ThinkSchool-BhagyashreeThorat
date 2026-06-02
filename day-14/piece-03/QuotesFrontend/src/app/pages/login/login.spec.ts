import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { Login } from './login';

describe('Login page', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Login],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
  });

  it('creates the component', () => {
    const fixture = TestBed.createComponent(Login);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('renders labelled email and password fields and a submit button', () => {
    const fixture = TestBed.createComponent(Login);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelector('label[for="email"]')).not.toBeNull();
    expect(el.querySelector('input#email')).not.toBeNull();
    expect(el.querySelector('label[for="password"]')).not.toBeNull();
    expect(el.querySelector('input#password')).not.toBeNull();
    expect(el.querySelector('button[type="submit"]')).not.toBeNull();
  });
});
