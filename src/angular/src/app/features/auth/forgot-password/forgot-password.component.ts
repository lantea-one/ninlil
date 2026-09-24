import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import {
  AlertComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardHeaderComponent,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { AuthService } from '../../../core/auth/auth.service';

/**
 * This component is Ninlil's public, top-level "Forgot your password?" request page,
 * reachable at `/forgot-password` with no sign-in required (a top-level route sibling to `login`/
 * `reset-password`, mirroring both their route shape exactly -- see `app.routes.ts`). It is the
 * entry point `LoginComponent`'s own "Forgot your password?" link leads to, and it submits to the
 * genuinely anonymous `POST /api/auth/forgot-password` (`AuthController.ForgotPassword`,
 * {@link AuthService.forgotPassword}).
 *
 * **This page must never reveal whether the submitted address actually has an account** -- the
 * whole point of the backend action it calls (see that action's own doc comment for the full
 * account-enumeration reasoning). Concretely, that means this component:
 * - Shows the exact same generic confirmation state after *any* successful submission, using
 *   whatever message string the backend sent back, never a locally-authored "sent"/"not sent"
 *   variant of its own.
 * - Never inspects the response for a "found"/"not found" signal, because the backend's own
 *   response carries none -- there is nothing here for this component to branch on even if it
 *   wanted to.
 * - Only distinguishes a genuine transport/network failure (this request never reached the
 *   server, or the server itself errored) from a normal completion -- that distinction is about
 *   this app's own connectivity, not about the submitted address, so showing it is safe.
 *
 * Once submitted successfully, this page offers a link back to `/login` -- it deliberately does
 * not itself link to `/reset-password`, since reaching that page requires a real token this page
 * never has (only the e-mail itself, once delivered, carries one).
 */
@Component({
  selector: 'app-forgot-password',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    AlertComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardHeaderComponent,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    IconDirective,
    SpinnerComponent,
  ],
  templateUrl: './forgot-password.component.html',
  styleUrl: './forgot-password.component.scss',
})
export class ForgotPasswordComponent {
  private readonly authService = inject(AuthService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly completed = signal(false);

  /** The backend's own generic confirmation message, shown verbatim once submission completes. */
  readonly confirmationMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    email: ['', [Validators.required, Validators.email]],
  });

  /** This method submits the request-reset-e-mail form against `POST /api/auth/forgot-password`. */
  async submit(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    try {
      const message = await this.authService.forgotPassword(this.form.getRawValue().email);
      this.confirmationMessage.set(message);
      this.completed.set(true);
    } catch {
      // A genuine transport/network failure only -- never a "this address doesn't exist"
      // response, since the backend itself never sends one. See this component's own doc
      // comment for why that distinction matters.
      this.errorMessage.set(
        'Something went wrong submitting this request. Please try again in a moment.',
      );
    } finally {
      this.submitting.set(false);
    }
  }
}
