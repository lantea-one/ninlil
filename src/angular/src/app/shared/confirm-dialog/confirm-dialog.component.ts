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
  /** The dialog's own title, e.g. `"Delete zone?"`. */
  title: string;

  /** The body text explaining what confirming will do, including anything irreversible. */
  message: string;

  /** The confirming button's own label. Defaults to `"Delete"` if not supplied. */
  confirmLabel?: string;
}

/**
 * This component is a small, reusable yes/no confirmation dialog -- used by every list/detail
 * component across this app before it calls a destructive backend endpoint (a `DELETE`, or a
 * service lifecycle action like stop/restart), since none of those actions are undoable or safe
 * to run by accident. Kept in `shared/` rather than any one feature folder specifically because
 * nothing about it is feature-specific -- every later feature reuses this component rather than
 * writing a second one.
 *
 * Unlike its Material `MatDialog`-based predecessor, this component is a plain, declaratively
 * embedded child -- a caller places `<app-confirm-dialog [visible]="..." [data]="..."
 * (resolved)="..." />` directly in its own template and drives visibility with a signal, rather
 * than imperatively calling `MatDialog.open(...).afterClosed()`. It wraps CoreUI's own `c-modal`,
 * and emits exactly one {@link resolved} event per open/close cycle -- `true` if the operator
 * clicked the confirm button, `false` for every other way the dialog closes (the Cancel button,
 * a backdrop click, or the Escape key) -- so a caller can treat a dismissal the same way
 * `MatDialogRef.afterClosed()` emitting `false`/`undefined` used to be treated: "do nothing."
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
  /** Whether this dialog is currently shown -- a caller drives this with its own signal. */
  readonly visible = input<boolean>(false);

  /** The text this dialog instance shows while {@link visible}. `null` renders an empty prompt (kept out of the DOM by the modal's own closed state). */
  readonly data = input<ConfirmDialogData | null>(null);

  /** Emits exactly once per open/close cycle: `true` if the operator confirmed, `false` for a Cancel click, a backdrop click, or Escape. */
  readonly resolved = output<boolean>();

  /** This method runs when the operator clicks the dialog's own destructive confirm button. */
  onConfirm(): void {
    this.resolved.emit(true);
  }

  /** This method runs when the operator clicks the dialog's own Cancel button. */
  onCancel(): void {
    this.resolved.emit(false);
  }

  /**
   * This method runs whenever the underlying CoreUI modal reports a new visibility on its own --
   * a backdrop click or an Escape press, neither of which routes through {@link onConfirm}/
   * {@link onCancel}. Both count as a cancel.
   */
  onModalVisibleChange(visible: boolean): void {
    if (!visible) this.resolved.emit(false);
  }
}
