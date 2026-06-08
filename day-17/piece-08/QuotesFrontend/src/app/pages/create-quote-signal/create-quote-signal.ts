import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  disabled,
  form,
  FormField,
  maxLength,
  required,
  schema,
  submit,
  type FieldState,
  type ValidationError,
} from '@angular/forms/signals';
import { QuoteService } from '../../services/quote/quote.service';
import { AuthService } from '../../services/auth/auth.service';
import { CreateQuote, Quote } from '../../models/quote.model';

const AUTHOR_MAX_LENGTH = 200;
const TEXT_MAX_LENGTH = 1000;

type FieldName = keyof CreateQuote;

@Component({
  selector: 'app-create-quote-signal',
  standalone: true,
  imports: [FormField],
  templateUrl: './create-quote-signal.html',
  styleUrl: './create-quote-signal.css',
})
export class CreateQuoteSignal {
  private readonly quoteService = inject(QuoteService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  private readonly model = signal<CreateQuote>({ author: '', text: '' });

  private readonly persisting = signal(false);

  protected readonly createdQuote = signal<Quote | null>(null);

  private readonly quoteSchema = schema<CreateQuote>((path) => {
    required(path.author, { message: 'Author is required.' });
    maxLength(path.author, AUTHOR_MAX_LENGTH, {
      message: `Author must be ${AUTHOR_MAX_LENGTH} characters or fewer.`,
    });

    required(path.text, { message: 'Text is required.' });
    maxLength(path.text, TEXT_MAX_LENGTH, {
      message: `Text must be ${TEXT_MAX_LENGTH} characters or fewer.`,
    });

    disabled(path.author, () => this.persisting());
    disabled(path.text, () => this.persisting());
  });

  protected readonly quoteForm = form(this.model, this.quoteSchema);

  protected readonly serverError = computed<string | null>(() => {
    const error = this.quoteForm().errors().find((e) => e.kind === 'server');
    return error?.message ?? null;
  });

  private fieldState(name: FieldName): FieldState<string> {
    return this.quoteForm[name]();
  }

  protected showError(name: FieldName): boolean {
    const state = this.fieldState(name);
    return state.invalid() && state.touched();
  }

  protected errorMessage(name: FieldName): string | null {
    if (!this.showError(name)) {
      return null;
    }
    return this.fieldState(name).errors()[0]?.message ?? 'Invalid value.';
  }

  protected describedBy(name: FieldName): string | null {
    return this.showError(name) ? `${name}-error` : null;
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();

    if (this.quoteForm().submitting()) {
      return;
    }
    this.createdQuote.set(null);

    this.model.update((value) => ({
      author: value.author.trim(),
      text: value.text.trim(),
    }));

    await submit(this.quoteForm, {
      action: async (formField) => {
        const payload: CreateQuote = formField().value();

        this.persisting.set(true);
        try {
          const created = await firstValueFrom(
            this.quoteService.createQuote(payload),
          );
          this.createdQuote.set(created);
          return undefined;
        } catch {
          const error: ValidationError = {
            kind: 'server',
            message: 'Could not create quote. Please try again.',
          };
          return error;
        } finally {
          this.persisting.set(false);
        }
      },
      onInvalid: () => this.focusFirstInvalid(),
    });

    if (this.createdQuote()) {
      this.quoteForm().reset({ author: '', text: '' });
    }
  }

  private focusFirstInvalid(): void {
    for (const name of ['author', 'text'] as const) {
      const state = this.fieldState(name);
      if (state.invalid()) {
        state.focusBoundControl();
        return;
      }
    }
  }

  protected logout(): void {
    this.auth.logout();
    this.router.navigate(['/']);
  }
}
