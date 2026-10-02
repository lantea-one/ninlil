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
  /** The label, e.g. `"Shared secret"` or `"Access Key ID"`. */
  label: string;

  /** The plaintext value, as the create or rotate response returned it. */
  value: string;
}

/** This interface describes the text and secret values {@link RevealSecretDialogComponent} renders for one prompt. */
export interface RevealSecretDialogData {
  /** The dialog title, e.g. `"TSIG key created"`. */
  title: string;

  /**
   * The body text saying these values will not be shown again. Callers name the specific thing
   * (a TSIG key's shared secret, a cloud credential's API token) so the operator knows what to
   * save.
   */
  message: string;

  /**
   * The values to reveal, one row each: one for a TSIG key's shared secret, several for a cloud
   * credential with more than one field (Route 53's `AccessKeyId` and `SecretAccessKey`).
   */
  secrets: RevealSecretDialogSecret[];
}

/**
 * This component is the shared dialog that shows a secret once, with a copy button per value. The
 * TSIG key, cloud credential, API token and webhook forms, the cluster invite, the Users page's
 * password reset and two-factor recovery codes on the Security page all use it. The server
 * returns these values only in the create or rotate response; after the dialog closes, they
 * cannot be shown again.
 *
 * <p>A caller puts `<app-reveal-secret-dialog [visible]="..." [data]="..." (closed)="..." />` in
 * its template and drives `visible` with a signal. The CoreUI `c-modal` has a static backdrop and
 * ignores Escape, because closing it by accident would lose the secret. Only the "I've saved
 * this" button closes it, emitting {@link visibleChange} and {@link closed}; callers often
 * navigate away on {@link closed}.
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
  /** Whether the dialog is shown; the caller drives this with a signal. */
  readonly visible = input<boolean>(false);

  /** The text and values to show. `null` renders an empty dialog, which is fine while it is closed. */
  readonly data = input<RevealSecretDialogData | null>(null);

  /** Emits the new visibility; always `false`, since the dialog can only close itself. */
  readonly visibleChange = output<boolean>();

  /** Emits when the operator closes the dialog with its button. */
  readonly closed = output<void>();

  private readonly clipboard = inject(Clipboard);

  /**
   * The label of the row copied in the last two seconds, so that row's button can show "Copied!";
   * `null` otherwise.
   */
  readonly copiedLabel = signal<string | null>(null);

  /** This method copies a value to the clipboard and marks its row as copied for two seconds. */
  copy(secret: RevealSecretDialogSecret): void {
    this.clipboard.copy(secret.value);
    this.copiedLabel.set(secret.label);
    setTimeout(() => {
      if (this.copiedLabel() === secret.label) this.copiedLabel.set(null);
    }, 2000);
  }

  /** This method closes the dialog; it is the only way to close it. */
  dismiss(): void {
    this.visibleChange.emit(false);
    this.closed.emit();
  }
}
