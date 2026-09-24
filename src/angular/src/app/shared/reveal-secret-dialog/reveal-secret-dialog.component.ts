import { Component, inject, input, output, signal } from '@angular/core';
import { Clipboard } from '@angular/cdk/clipboard';
import {
  ButtonDirective,
  ModalBodyComponent,
  ModalComponent,
  ModalFooterComponent,
  ModalHeaderComponent,
  ModalTitleDirective,
  TooltipDirective,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';

/** This interface describes one labeled secret value {@link RevealSecretDialogComponent} shows. */
export interface RevealSecretDialogSecret {
  /** The field's own human-readable label, e.g. `"Shared secret"` or `"Access Key ID"`. */
  label: string;

  /** The plaintext value itself, exactly as this session's own create/rotate response returned it. */
  value: string;
}

/** This interface describes the text and secret values {@link RevealSecretDialogComponent} renders for one prompt. */
export interface RevealSecretDialogData {
  /** The dialog's own title, e.g. `"TSIG key created"`. */
  title: string;

  /**
   * The body text explaining that this is the only time these values will ever be shown again --
   * every caller of this dialog should word this around its own specific entity (a TSIG key's
   * shared secret, a cloud credential's API token), not a generic phrase, so an operator
   * understands exactly what they are about to lose access to viewing.
   */
  message: string;

  /**
   * The secret value(s) to reveal, one row per entry -- a single-element array for a TSIG key's
   * own one shared secret, or several for a cloud provider credential set whose own shape needs
   * more than one field (e.g. Route 53's `AccessKeyId` + `SecretAccessKey`).
   */
  secrets: RevealSecretDialogSecret[];
}

/**
 * This component is the one, shared reveal-once secret dialog every feature that hands a caller a
 * plaintext secret uses -- `TsigKeyFormComponent`/`CloudProviderCredentialFormComponent`/
 * `AccountSecurityComponent`/`ApiTokenFormComponent`/`WebhookFormComponent` each embed it
 * unchanged. It exists because this project's backend
 * follows a uniform "reveal exactly once, in the create/rotate response, then never again"
 * exposure policy for every secret-bearing entity type -- once this dialog is dismissed, there is
 * no way to see the same value again through this UI, matching the backend's own real behavior (a
 * later `GET` genuinely omits it, not just this dialog choosing not to show it again).
 *
 * Unlike its Material `MatDialog`-based predecessor, this component is a plain, declaratively
 * embedded child -- a caller places `<app-reveal-secret-dialog [visible]="..." [data]="..."
 * (closed)="..." />` directly in its own template and drives visibility with a signal, rather than
 * imperatively calling `MatDialog.open(...).afterClosed()`. It wraps CoreUI's own `c-modal` with
 * `[backdrop]="'static'"` and `[keyboard]="false"` so neither a backdrop click nor an Escape press
 * can dismiss it -- an accidental early dismissal before an operator has actually copied the value
 * down would be a genuine, unrecoverable paper cut, unlike {@link ConfirmDialogComponent}'s own
 * yes/no prompt, where dismissing early just means "try again." The only way to close this dialog
 * is its own explicit "I've saved this -- close" button, which emits both {@link visibleChange}
 * and {@link closed} so a caller can drive its own visibility signal and run whatever follow-up
 * action (typically navigating away) belongs after the operator has confirmed they saved the value.
 */
@Component({
  selector: 'app-reveal-secret-dialog',
  imports: [
    ButtonDirective,
    ModalBodyComponent,
    ModalComponent,
    ModalFooterComponent,
    ModalHeaderComponent,
    ModalTitleDirective,
    TooltipDirective,
    IconDirective,
  ],
  templateUrl: './reveal-secret-dialog.component.html',
  styleUrl: './reveal-secret-dialog.component.scss',
})
export class RevealSecretDialogComponent {
  /** Whether this dialog is currently shown -- a caller drives this with its own signal. */
  readonly visible = input<boolean>(false);

  /** The text and secret values this dialog instance shows while {@link visible}. `null` renders nothing (kept out of the DOM by the modal's own closed state). */
  readonly data = input<RevealSecretDialogData | null>(null);

  /** Emits the dialog's own new visibility -- always `false`, since this dialog can only ever be dismissed, never opened, from inside itself. */
  readonly visibleChange = output<boolean>();

  /** Emits once the operator has explicitly dismissed the dialog via its own close button -- the declarative analogue of the old `MatDialogRef.afterClosed()` completion. */
  readonly closed = output<void>();

  private readonly clipboard = inject(Clipboard);

  /**
   * The label of whichever secret row was most recently copied, briefly, so the template can show
   * a "Copied!" confirmation next to the specific button that was clicked rather than a single,
   * ambiguous app-wide toast -- `null` once the brief confirmation window has elapsed or nothing
   * has been copied yet this dialog instance.
   */
  readonly copiedLabel = signal<string | null>(null);

  /** This method copies one secret row's own value to the clipboard and briefly confirms it. */
  copy(secret: RevealSecretDialogSecret): void {
    this.clipboard.copy(secret.value);
    this.copiedLabel.set(secret.label);
    setTimeout(() => {
      if (this.copiedLabel() === secret.label) this.copiedLabel.set(null);
    }, 2000);
  }

  /** This method dismisses the dialog -- the one and only way it can ever close. */
  dismiss(): void {
    this.visibleChange.emit(false);
    this.closed.emit();
  }
}
