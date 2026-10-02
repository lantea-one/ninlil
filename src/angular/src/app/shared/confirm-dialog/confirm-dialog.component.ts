import { Component, input, output } from '@angular/core';
import {
  ButtonDirective,
  ModalBodyComponent,
  ModalComponent,
  ModalFooterComponent,
  ModalHeaderComponent,
  ModalTitleDirective,
} from '@coreui/angular';

/** This interface describes the text {@link ConfirmDialogComponent} renders for one prompt. */
export interface ConfirmDialogData {
  /** The dialog title, e.g. `"Delete zone?"`. */
  title: string;

  /** The body text explaining what confirming will do, including anything irreversible. */
  message: string;

  /** The confirm button's label. Defaults to `"Delete"`. */
  confirmLabel?: string;
}

/**
 * This component is the shared yes/no dialog that pages show before a destructive call: a
 * `DELETE`, or an action such as stopping a service or restoring a backup.
 *
 * <p>A caller puts `<app-confirm-dialog [visible]="..." [data]="..." (resolved)="..." />` in its
 * template and drives `visible` with a signal. The component wraps CoreUI's `c-modal`. It emits
 * {@link resolved} with `true` when the confirm button is clicked and `false` for Cancel, a
 * backdrop click or Escape. The caller hides the dialog in its handler.
 */
@Component({
  selector: 'app-confirm-dialog',
  imports: [
    ButtonDirective,
    ModalBodyComponent,
    ModalComponent,
    ModalFooterComponent,
    ModalHeaderComponent,
    ModalTitleDirective,
  ],
  template: `
    <c-modal
      [visible]="visible()"
      (visibleChange)="onModalVisibleChange($event)"
      alignment="center"
    >
      <c-modal-header>
        <h5 cModalTitle>{{ data()?.title }}</h5>
      </c-modal-header>
      <c-modal-body>{{ data()?.message }}</c-modal-body>
      <c-modal-footer>
        <button cButton color="secondary" type="button" (click)="onCancel()">Cancel</button>
        <button cButton color="danger" type="button" (click)="onConfirm()">
          {{ data()?.confirmLabel ?? 'Delete' }}
        </button>
      </c-modal-footer>
    </c-modal>
  `,
})
export class ConfirmDialogComponent {
  /** Whether the dialog is shown; the caller drives this with a signal. */
  readonly visible = input<boolean>(false);

  /** The text to show. `null` gives an empty prompt, which is fine while the dialog is closed. */
  readonly data = input<ConfirmDialogData | null>(null);

  /** Emits `true` when the operator confirms, `false` for Cancel, a backdrop click or Escape. */
  readonly resolved = output<boolean>();

  /** This method runs when the operator clicks the confirm button. */
  onConfirm(): void {
    this.resolved.emit(true);
  }

  /** This method runs when the operator clicks Cancel. */
  onCancel(): void {
    this.resolved.emit(false);
  }

  /**
   * This method runs when the CoreUI modal closes itself, on a backdrop click or Escape. Both
   * count as a cancel.
   */
  onModalVisibleChange(visible: boolean): void {
    if (!visible) this.resolved.emit(false);
  }
}
