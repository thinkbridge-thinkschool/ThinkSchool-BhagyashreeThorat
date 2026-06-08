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

interface LoginForm {
  email: FormControl<string>;
  password: FormControl<string>;
}
type FieldName = keyof LoginForm;

@Component({
  selector: 'app-admin-login-modal',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './admin-login-modal.html',
  styleUrl: './admin-login-modal.css',
})
export class AdminLoginModal {
  private readonly auth = inject(AuthService);

  readonly loggedIn = output<void>();
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
    afterNextRender(() => this.emailInput()?.nativeElement.focus());
  }

  protected isInvalid(name: FieldName): boolean {
    const control = this.form.controls[name];
    return control.invalid && control.touched;
  }

  protected describedBy(name: FieldName): string | null {
    return this.isInvalid(name) ? `${name}-error` : null;
  }

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
