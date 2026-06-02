import {
  Component,
  ElementRef,
  afterNextRender,
  inject,
  output,
  signal,
  viewChild,
} from '@angular/core';
import {
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { AuthService } from '../../services/auth/auth.service';
import { LoginRequest } from '../../models/auth.model';

// Strongly-typed reactive login form. Non-nullable string controls, so
// `getRawValue()` resolves to exactly the `LoginRequest` contract — no `any`.
interface LoginForm {
  email: FormControl<string>;
  password: FormControl<string>;
}
type FieldName = keyof LoginForm;

// Self-contained login dialog. Owns NO auth/token logic — it only collects
// credentials and delegates to AuthService, then signals the parent. The parent
// decides what closing/navigating means, via the two outputs below.
@Component({
  selector: 'app-admin-login-modal',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './admin-login-modal.html',
  styleUrl: './admin-login-modal.css',
})
export class AdminLoginModal {
  private readonly auth = inject(AuthService);

  // Login succeeded — parent should close the modal and navigate to /admin.
  readonly loggedIn = output<void>();
  // User dismissed the dialog (X button, backdrop click, or Escape).
  readonly closed = output<void>();

  private readonly emailInput =
    viewChild<ElementRef<HTMLInputElement>>('emailInput');
  private readonly passwordInput =
    viewChild<ElementRef<HTMLInputElement>>('passwordInput');

  protected readonly submitting = signal(false);
  protected readonly loginError = signal<string | null>(null);

  protected readonly form = new FormGroup<LoginForm>({
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email],
    }),
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
  });

  constructor() {
    // Move focus into the dialog as soon as it renders (a11y: focus the modal,
    // not the page behind it).
    afterNextRender(() => this.emailInput()?.nativeElement.focus());
  }

  protected isInvalid(name: FieldName): boolean {
    const control = this.form.controls[name];
    return control.invalid && control.touched;
  }

  protected describedBy(name: FieldName): string | null {
    return this.isInvalid(name) ? `${name}-error` : null;
  }

  // Don't let the user close mid-request — the form is disabled then anyway.
  protected dismiss(): void {
    if (!this.submitting()) {
      this.closed.emit();
    }
  }

  protected submit(): void {
    if (this.submitting()) {
      return;
    }
    this.loginError.set(null);

    // Trim the email (not the password — spaces can be significant there).
    this.form.controls.email.setValue(this.form.controls.email.value.trim());

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.focusFirstInvalid();
      return;
    }

    this.submitting.set(true);
    this.form.disable();

    const credentials: LoginRequest = this.form.getRawValue();

    this.auth.login(credentials).subscribe({
      next: () => {
        // AuthService has already persisted the tokens via tap().
        this.loggedIn.emit();
      },
      error: () => {
        this.loginError.set('Login failed. Check your email and password.');
        this.submitting.set(false);
        this.form.enable();
      },
    });
  }

  private focusFirstInvalid(): void {
    if (this.form.controls.email.invalid) {
      this.emailInput()?.nativeElement.focus();
      return;
    }
    if (this.form.controls.password.invalid) {
      this.passwordInput()?.nativeElement.focus();
    }
  }
}
