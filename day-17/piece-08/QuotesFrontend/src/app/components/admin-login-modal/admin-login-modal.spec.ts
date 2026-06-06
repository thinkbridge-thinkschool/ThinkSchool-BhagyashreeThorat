import { vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { AdminLoginModal } from './admin-login-modal';
import { environment } from '../../../environments/environment';

describe('AdminLoginModal', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AdminLoginModal],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
  });

  it('creates the component', () => {
    const fixture = TestBed.createComponent(AdminLoginModal);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('renders an accessible dialog with labelled fields and a close button', () => {
    const fixture = TestBed.createComponent(AdminLoginModal);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;

    const dialog = el.querySelector('[role="dialog"]');
    expect(dialog).not.toBeNull();
    expect(dialog?.getAttribute('aria-modal')).toBe('true');
    expect(dialog?.getAttribute('aria-labelledby')).toBe('admin-login-title');

    expect(el.querySelector('label[for="login-email"]')).not.toBeNull();
    expect(el.querySelector('input#login-email')).not.toBeNull();
    expect(el.querySelector('label[for="login-password"]')).not.toBeNull();
    expect(el.querySelector('input#login-password')).not.toBeNull();
    expect(el.querySelector('button[aria-label="Close login dialog"]')).not.toBeNull();
  });

  it('emits "closed" when dismissed', () => {
    const fixture = TestBed.createComponent(AdminLoginModal);
    const spy = vi.fn();
    fixture.componentInstance.closed.subscribe(spy);
    fixture.detectChanges();

    const closeBtn = fixture.nativeElement.querySelector(
      'button[aria-label="Close login dialog"]',
    ) as HTMLButtonElement;
    closeBtn.click();

    expect(spy).toHaveBeenCalled();
  });

  it('emits "loggedIn" after a successful login', () => {
    const fixture = TestBed.createComponent(AdminLoginModal);
    const http = TestBed.inject(HttpTestingController);
    const spy = vi.fn();
    fixture.componentInstance.loggedIn.subscribe(spy);
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    const email = el.querySelector('#login-email') as HTMLInputElement;
    const password = el.querySelector('#login-password') as HTMLInputElement;

    email.value = 'admin@example.com';
    email.dispatchEvent(new Event('input'));
    password.value = 'secret';
    password.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (el.querySelector('form') as HTMLFormElement).dispatchEvent(
      new Event('submit'),
    );

    const req = http.expectOne(`${environment.apiBase}/api/auth/login`);
    req.flush({ accessToken: 'a', refreshToken: 'r', expiresIn: 3600 });

    expect(spy).toHaveBeenCalled();
    http.verify();
  });
});
