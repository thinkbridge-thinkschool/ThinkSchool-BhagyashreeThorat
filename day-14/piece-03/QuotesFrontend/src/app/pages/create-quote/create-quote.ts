import { Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import {
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { QuoteService } from '../../services/quote/quote.service';
import { CreateQuote } from '../../models/quote.model';

// Strongly-typed reactive create-quote form. Non-nullable string controls, so
// `getRawValue()` resolves to exactly the `CreateQuote` contract — no `any`.
// Limits mirror the REAL Week-1 DTO (QuotesApi/DTOs/CreateQuoteRequest.cs):
//   Author: [Required][MaxLength(200)]
//   Text:   [Required][MaxLength(1000)]
interface CreateQuoteForm {
  author: FormControl<string>;
  text: FormControl<string>;
}
type FieldName = keyof CreateQuoteForm;

const AUTHOR_MAX = 200;
const TEXT_MAX = 1000;

@Component({
  selector: 'app-create-quote',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './create-quote.html',
  styleUrl: './create-quote.css',
})
export class CreateQuotePage {
  private readonly quotes = inject(QuoteService);
  private readonly router = inject(Router);

  // Native refs so a failed submit moves focus to the first invalid control.
  private readonly authorInput =
    viewChild<ElementRef<HTMLInputElement>>('authorInput');
  private readonly textInput =
    viewChild<ElementRef<HTMLTextAreaElement>>('textInput');

  protected readonly authorMax = AUTHOR_MAX;
  protected readonly textMax = TEXT_MAX;

  protected readonly submitting = signal(false);
  protected readonly submitError = signal<string | null>(null);

  protected readonly form = new FormGroup<CreateQuoteForm>({
    author: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(AUTHOR_MAX)],
    }),
    text: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(TEXT_MAX)],
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

  protected submit(): void {
    if (this.submitting()) {
      return;
    }
    this.submitError.set(null);

    // Trim whitespace — the server rejects whitespace-only values
    // (Quote.Create uses string.IsNullOrWhiteSpace), so mirror that here.
    this.form.controls.author.setValue(this.form.controls.author.value.trim());
    this.form.controls.text.setValue(this.form.controls.text.value.trim());

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.focusFirstInvalid();
      return;
    }

    this.submitting.set(true);
    this.form.disable();

    const payload: CreateQuote = this.form.getRawValue();

    this.quotes.createQuote(payload).subscribe({
      next: () => {
        // List + detail live on the homepage; return there after a save.
        this.router.navigate(['/']);
      },
      error: (err: HttpErrorResponse) => {
        this.submitError.set(
          err.status === 401 || err.status === 403
            ? 'You are not authorized to create quotes.'
            : 'Could not save the quote. Please try again.',
        );
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
