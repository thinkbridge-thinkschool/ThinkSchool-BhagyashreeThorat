import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
// Signal Forms PREVIEW API. Everything below is imported from the real
// '@angular/forms/signals' entry point shipped with @angular/forms@21.2.x and
// marked @experimental 21.0.0 — no API is invented.
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

// REAL contract constraints (POST /api/quotes). Only the two writable fields
// exist on the contract, and these are the only advertised length limits.
const AUTHOR_MAX_LENGTH = 200;
const TEXT_MAX_LENGTH = 1000;

// The only two fields the form edits, mirroring the existing reactive `Admin`
// component. Used to drive per-field accessibility + focus helpers without
// stringly-typed lookups leaking `any`.
type FieldName = keyof CreateQuote;

@Component({
  selector: 'app-create-quote-signal',
  standalone: true,
  // Only the FormField directive (selector `[formField]`) is needed to bind a
  // signal field to a native control. We intentionally do NOT use the
  // `FormRoot` directive: its built-in submit handler calls `submit(field)`
  // with no `action`, so it cannot run our HTTP POST. We own the submit event
  // instead (see `onSubmit`).
  imports: [FormField],
  templateUrl: './create-quote-signal.html',
  styleUrl: './create-quote-signal.css',
})
export class CreateQuoteSignal {
  private readonly quoteService = inject(QuoteService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  // The data model is a plain writable signal of the EXACT contract shape.
  // Signal Forms wraps this signal — there is no FormGroup/FormControl tree.
  private readonly model = signal<CreateQuote>({ author: '', text: '' });

  // Bridges our imperative submit to the declarative `disabled()` rules below.
  // It is flipped to `true` ONLY for the HTTP window (inside the submit
  // `action`, after validation has already passed) — NOT for the whole submit.
  // Reason: a disabled signal field is excluded from its parent's validation,
  // so disabling the fields *before* validation would make the form falsely
  // valid. The whole-submit "submitting" state is read from the built-in
  // `quoteForm().submitting()` instead.
  private readonly persisting = signal(false);

  // UI-only state that Signal Forms does not model: the created row + a
  // success flag for resetting. Server *errors* are modelled as validation
  // errors on the field (see `serverError`), not as ad-hoc signals.
  protected readonly createdQuote = signal<Quote | null>(null);

  // Schema = where all logic (validators + disabled) is declared against paths,
  // before the form exists. `required` + `maxLength` are the ONLY validators
  // the API contract justifies. Custom user-facing messages are passed via the
  // documented `message` option.
  private readonly quoteSchema = schema<CreateQuote>((path) => {
    required(path.author, { message: 'Author is required.' });
    maxLength(path.author, AUTHOR_MAX_LENGTH, {
      message: `Author must be ${AUTHOR_MAX_LENGTH} characters or fewer.`,
    });

    required(path.text, { message: 'Text is required.' });
    maxLength(path.text, TEXT_MAX_LENGTH, {
      message: `Text must be ${TEXT_MAX_LENGTH} characters or fewer.`,
    });

    // Declaratively disable both inputs while the POST is in flight. FormField
    // reflects the field's disabled state onto the native element.
    disabled(path.author, () => this.persisting());
    disabled(path.text, () => this.persisting());
  });

  // The field tree. Call it as a function to read root state
  // (`quoteForm()`), index into it for child fields (`quoteForm.author`).
  protected readonly quoteForm = form(this.model, this.quoteSchema);

  // Surfaces a server-side failure that was reported as a field-less
  // ValidationError of kind 'server' from the submit `action`. Such errors
  // attach to the submitted (root) field, so we read them off the root.
  protected readonly serverError = computed<string | null>(() => {
    const error = this.quoteForm().errors().find((e) => e.kind === 'server');
    return error?.message ?? null;
  });

  // --- per-field accessibility helpers ---------------------------------------

  private fieldState(name: FieldName): FieldState<string> {
    // `quoteForm[name]` is a callable FieldTree<string>; invoking it yields the
    // live FieldState for that field.
    return this.quoteForm[name]();
  }

  // Show an error only once the user has interacted (touched) AND it is
  // actually invalid — matches the reactive component's UX.
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

  // --- submission ------------------------------------------------------------

  protected async onSubmit(event: Event): Promise<void> {
    // We own the native submit; stop the browser's default navigation.
    event.preventDefault();

    // Prevent duplicate submits: the built-in `submitting()` is `true` for the
    // entire `submit()` call (validation + action), so it is the correct
    // re-entry guard.
    if (this.quoteForm().submitting()) {
      return;
    }
    this.createdQuote.set(null);

    // Trim BEFORE validating so whitespace-only input collapses to '' and fails
    // `required`. This mirrors the server, which also rejects whitespace-only.
    this.model.update((value) => ({
      author: value.author.trim(),
      text: value.text.trim(),
    }));

    await submit(this.quoteForm, {
      // Runs ONLY when the form is valid. Returning nothing = success;
      // returning ValidationError(s) reports them on the submitted field.
      action: async (formField) => {
        // Strongly typed: root value is the CreateQuote contract.
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
      // Fired when submit is attempted but validation fails. `submit()` has
      // already marked the fields touched, so we only move focus.
      onInvalid: () => this.focusFirstInvalid(),
    });

    // Success path: reset both the data model and the touched/dirty flags in a
    // single call (`reset(value)` clears state AND sets the value).
    if (this.createdQuote()) {
      this.quoteForm().reset({ author: '', text: '' });
    }
  }

  private focusFirstInvalid(): void {
    for (const name of ['author', 'text'] as const) {
      const state = this.fieldState(name);
      if (state.invalid()) {
        // Focuses the native control bound to this field via FormField.
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
