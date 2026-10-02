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
 * This component is the public Forgot Password page (`/forgot-password`), linked from the login
 * page. It sends an e-mail address to the anonymous `POST /api/auth/forgot-password` through
 * {@link AuthService.forgotPassword}; the backend mails a reset link if the address has an
 * account.
 *
 * <p>**The page must never reveal whether the address has an account.** The backend returns the
 * same message either way, and this page shows that message after any successful submit. The only
 * other outcome is a request that failed (network or server error), which says nothing about the
 * address.
 *
 * <p>After submitting, the page links back to `/login`. It does not link to `/reset-password`,
 * which needs the token from the e-mail.
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

  /** The backend's confirmation message, shown unchanged after a successful submit. */
  readonly confirmationMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    email: ['', [Validators.required, Validators.email]],
  });

  /** This method submits the e-mail address to `POST /api/auth/forgot-password`. */
  async submit(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    try {
      const message = await this.authService.forgotPassword(this.form.getRawValue().email);
      this.confirmationMessage.set(message);
      this.completed.set(true);
    } catch {
      // A failed request. The backend never answers "no such address", so this reveals
      // nothing about the account.
      this.errorMessage.set(
        'Something went wrong submitting this request. Please try again in a moment.',
      );
    } finally {
      this.submitting.set(false);
    }
  }
}
