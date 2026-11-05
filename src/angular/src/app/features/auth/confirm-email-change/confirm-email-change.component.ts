import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  AlertComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardHeaderComponent,
  SpinnerComponent,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { AuthService } from '../../../core/auth/auth.service';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';

/**
 * This component is the public page the self-service e-mail change link opens
 * (`/confirm-email-change?userId=...&email=...&token=...`). It posts the three query values,
 * unchanged, to the anonymous `POST /api/auth/confirm-email-change`. The token is Base64Url and
 * must not be decoded or re-encoded.
 *
 * <p>The post starts once, from the constructor, because opening the link is the user's
 * confirmation and there is no form. A flag guards it so no re-render, under zoneless change
 * detection or otherwise, can send it twice: the link works only once, so a second post would
 * turn a success into an error. Missing query values show an error and send nothing.
 *
 * <p>A confirmed change has ended every session of the account on the server. If this browser is
 * signed in locally, the page clears that session too, as sign-out does, so the user is not left
 * with a token the server rejects.
 */
@Component({
  selector: 'app-confirm-email-change',
  imports: [
    RouterLink,
    AlertComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardHeaderComponent,
    IconDirective,
    SpinnerComponent,
  ],
  templateUrl: './confirm-email-change.component.html',
  styleUrl: './confirm-email-change.component.scss',
})
export class ConfirmEmailChangeComponent {
  private readonly authService = inject(AuthService);
  private readonly route = inject(ActivatedRoute);

  /** Set when the post starts; the guard that keeps it to one post per page. */
  private started = false;

  /** Whether the post is in flight. */
  readonly confirming = signal(false);

  /** Whether the server accepted the link. */
  readonly completed = signal(false);

  /** The message to show when the link is incomplete or the server rejected it. */
  readonly errorMessage = signal<string | null>(null);

  constructor() {
    void this.confirm();
  }

  private async confirm(): Promise<void> {
    if (this.started) return;
    this.started = true;

    const query = this.route.snapshot.queryParamMap;
    const userId = query.get('userId');
    const email = query.get('email');
    const token = query.get('token');

    if (!userId || !email || !token) {
      this.errorMessage.set(
        'This confirmation link is missing or incomplete. Open the link from the e-mail again, or change your e-mail address again from your profile.',
      );
      return;
    }

    this.confirming.set(true);
    try {
      await this.authService.confirmEmailChange(userId, email, token);
      if (this.authService.isAuthenticated()) {
        await this.authService.logout();
      }
      this.completed.set(true);
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(
          error,
          'This confirmation link is not valid, has expired or was already used. Change your e-mail address again from your profile.',
        ),
      );
    } finally {
      this.confirming.set(false);
    }
  }
}
