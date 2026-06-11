import { Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import {
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { Router } from '@angular/router';
import { QuoteService } from '../../services/quote/quote.service';
import { AuthService } from '../../services/auth/auth.service';
import { CreateQuote, Quote } from '../../models/quote.model';

interface CreateQuoteForm {
  author: FormControl<string>;
  text: FormControl<string>;
}
type FieldName = keyof CreateQuoteForm;

@Component({
  selector: 'app-admin',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './admin.html',
  styleUrl: './admin.css',
})
export class Admin {
  private readonly quoteService = inject(QuoteService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  private readonly authorInput =
    viewChild<ElementRef<HTMLInputElement>>('authorInput');
  private readonly textInput =
    viewChild<ElementRef<HTMLTextAreaElement>>('textInput');

  protected readonly submitting = signal(false);
  protected readonly serverError = signal<string | null>(null);
  protected readonly createdQuote = signal<Quote | null>(null);

  protected readonly form = new FormGroup<CreateQuoteForm>({
    author: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
    text: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required , Validators.maxLength(100)],
    }),
  });

  private readonly focusTargets: ReadonlyArray<{
    name: FieldName;
    el: () => ElementRef<HTMLElement> | undefined;
  }> = [
    { name: 'author', el: this.authorInput },
    { name: 'text', el: this.textInput },
  ];

  protected isInvalid(name: FieldName): boolean {
    const control = this.form.controls[name];
    return control.invalid && control.touched;
  }

  protected describedBy(name: FieldName): string | null {
    return this.isInvalid(name) ? `${name}-error` : null;
  }

  protected createQuote(): void {
    if (this.submitting()) {
      return;
    }
    this.serverError.set(null);
    this.createdQuote.set(null);

    this.form.setValue({
      author: this.form.controls.author.value.trim(),
      text: this.form.controls.text.value.trim(),
    });

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.focusFirstInvalid();
      return;
    }

    this.submitting.set(true);
    this.form.disable();

    const payload: CreateQuote = this.form.getRawValue();

    this.quoteService.createQuote(payload).subscribe({
      next: (created) => {
        this.createdQuote.set(created);
        this.submitting.set(false);
        this.form.enable();
        this.form.reset();
      },
      error: () => {
        this.serverError.set('Could not create quote. Please try again.');
        this.submitting.set(false);
        this.form.enable();
      },
    });
  }

  protected logout(): void {
    this.auth.logout();
    this.router.navigate(['/']);
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
