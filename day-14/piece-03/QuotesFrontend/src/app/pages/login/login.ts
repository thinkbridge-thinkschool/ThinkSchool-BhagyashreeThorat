import { Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import {
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../services/auth/auth.service';
import { LoginRequest } from '../../models/auth.model';

// Strongly-typed reactive login form. Non-nullable string controls, so
// `getRawValue()` resolves to exactly the `LoginRequest` contract — no `any`.
interface LoginForm {
  email: FormControl<string>;
  password: FormControl<string>;
}
type FieldName = keyof LoginForm;

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './login.html',
  styleUrl: './login.css',
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  // Native refs so a failed submit moves focus to the first invalid control.
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

  private readonly focusTargets: ReadonlyArray<{
    name: FieldName;
    el: () => ElementRef<HTMLElement> | undefined;
  }> = [
    { name: 'email', el: this.emailInput },
    { name: 'password', el: this.passwordInput },
  ];

  protected isInvalid(name: FieldName): boolean {
    const control = this.form.controls[name];
    return control.invalid && control.touched;
  }

  protected describedBy(name: FieldName): string | null {
    return this.isInvalid(name) ? `${name}-error` : null;
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
        // AuthService has already stored the tokens via tap().
        this.router.navigate(['/admin']);
      },
      error: () => {
        this.loginError.set('Login failed. Check your email and password.');
        this.submitting.set(false);
        this.form.enable();
      },
    });
  }

  private focusFirstInvalid(): void {
    for (const target of this.focusTargets) {
      if (this.form.controls[target.name].invalid) {
        target.el()?.nativeElement.focus();
        return;
      }
    }
  }
}
