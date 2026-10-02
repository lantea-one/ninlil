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
 * This component is the "edit peer" dialog that `ClusterComponent` opens from its peer table. It
 * saves a peer's name and notes with `PATCH /api/cluster/nodes/{id}` (`clusternodes:write`). The
 * parent drives it with the `visible` and `node` inputs, hides it on `visibleChange(false)` and
 * reloads its list on `saved`.
 *
 * <p>Only the name and comments can change. The address, port, TSIG key and pinned certificate
 * are learned during the join, and the heartbeat status is the server's.
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

  /** Whether the dialog is shown; the caller drives this with a signal. */
  readonly visible = input<boolean>(false);

  /** The peer whose name and notes are edited. */
  readonly node = input<ClusterNodeDetail | null>(null);

  /** Emits `false` whenever the dialog closes: a cancel, a backdrop click or Escape, or a successful save. */
  readonly visibleChange = output<boolean>();

  /** Emits after a successful save, so the caller can reload its peer list. */
  readonly saved = output<void>();

  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly editForm = this.formBuilder.group({
    name: ['', [Validators.required]],
    comments: [''],
  });

  constructor() {
    // Refill the form whenever a new `node` arrives, so reopening the dialog for another peer, or
    // for the same peer after a reload, never shows values from an earlier edit.
    effect(() => {
      const current = this.node();
      this.editForm.reset({
        name: current?.name ?? '',
        comments: current?.comments ?? '',
      });
      this.errorMessage.set(null);
    });
  }

  /** This method saves the name and notes; empty notes are sent as `null`. */
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

  /** This method handles the modal closing itself (a backdrop click or Escape) as a cancel. */
  onModalVisibleChange(visible: boolean): void {
    if (!visible) {
      this.errorMessage.set(null);
      this.visibleChange.emit(false);
    }
  }
}
