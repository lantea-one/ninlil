import { Component, effect, inject, input, output, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  AlertComponent,
  ButtonDirective,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  ModalBodyComponent,
  ModalComponent,
  ModalFooterComponent,
  ModalHeaderComponent,
  ModalTitleDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import { ClusterNodeDetail, ClusterService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';

/**
 * This component is the "edit peer" dialog `ClusterComponent` opens from its own peer table,
 * wired to `PATCH /api/cluster/nodes/{id}` (`ClusterController.Patch`). It follows the same
 * dedicated-sibling-dialog shape `ResetPasswordDialogComponent` already establishes for a small,
 * low-field-count admin action reached from a list page: a `visible`/`node` input pair, a
 * `visibleChange` output for both an explicit cancel and the modal's own backdrop/Escape
 * dismissal, and a `saved` output the parent reloads its list from.
 *
 * Only `Name` and `Comments` are editable here -- every other field on {@link ClusterNodeDetail}
 * (address, port, TSIG key, pinned certificate thumbprint, heartbeat status) is fixed once a peer
 * row exists, learned once during the join handshake, exactly as `ClusterNodeWriteRequest`'s own
 * backend doc comment explains.
 */
@Component({
  selector: 'app-edit-node-dialog',
  imports: [
    ReactiveFormsModule,
    AlertComponent,
    ButtonDirective,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    ModalBodyComponent,
    ModalComponent,
    ModalFooterComponent,
    ModalHeaderComponent,
    ModalTitleDirective,
    SpinnerComponent,
  ],
  templateUrl: './edit-node-dialog.component.html',
})
export class EditNodeDialogComponent {
  private readonly clusterApi = inject(ClusterService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  /** Whether this dialog is currently shown -- a caller drives this with its own signal. */
  readonly visible = input<boolean>(false);

  /** Which peer this dialog instance edits the name/notes of. */
  readonly node = input<ClusterNodeDetail | null>(null);

  /** Emits this dialog's own new visibility -- `false` on every close, whether cancelled, a save failure the operator gave up on, or a completed save. */
  readonly visibleChange = output<boolean>();

  /** Emits once a save has actually succeeded, so the caller can reload its own peer list. */
  readonly saved = output<void>();

  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly editForm = this.formBuilder.group({
    name: ['', [Validators.required]],
    comments: [''],
  });

  constructor() {
    // Re-seed the form every time a fresh peer is handed in (a new `node` input value), so
    // reopening this same dialog instance for a different row -- or the same row after an
    // external reload changed its own comments -- never shows stale values left over from a
    // previous edit.
    effect(() => {
      const current = this.node();
      this.editForm.reset({
        name: current?.name ?? '',
        comments: current?.comments ?? '',
      });
      this.errorMessage.set(null);
    });
  }

  /** This method submits the edited name/notes against `PATCH /api/cluster/nodes/{id}`. */
  async submit(): Promise<void> {
    const target = this.node();
    if (!target?.id || this.editForm.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.editForm.getRawValue();

    try {
      await firstValueFrom(
        this.clusterApi.apiClusterNodesIdPatch({
          id: target.id,
          clusterNodeWriteRequest: {
            name: value.name,
            comments: value.comments || null,
          },
        }),
      );
      this.notification.success(`Saved changes to ${value.name}.`);
      this.visibleChange.emit(false);
      this.saved.emit();
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save changes to this peer.'));
    } finally {
      this.submitting.set(false);
    }
  }

  /** This method runs when the modal reports a new visibility on its own (a backdrop click or Escape). */
  onModalVisibleChange(visible: boolean): void {
    if (!visible) {
      this.errorMessage.set(null);
      this.visibleChange.emit(false);
    }
  }
}
