import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { Admin } from './admin';

describe('Admin page', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Admin],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
  });

  it('creates the component', () => {
    const fixture = TestBed.createComponent(Admin);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('renders the create-quote form and a logout button', () => {
    const fixture = TestBed.createComponent(Admin);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelector('label[for="author"]')).not.toBeNull();
    expect(el.querySelector('input#author')).not.toBeNull();
    expect(el.querySelector('label[for="text"]')).not.toBeNull();
    expect(el.querySelector('textarea#text')).not.toBeNull();
    expect(el.querySelector('button.logout')).not.toBeNull();
  });
});
