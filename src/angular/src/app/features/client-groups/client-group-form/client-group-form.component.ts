import { Component, OnInit, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  AlertComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardFooterComponent,
  CardHeaderComponent,
  FormCheckComponent,
  FormCheckInputDirective,
  FormCheckLabelDirective,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { ClientGroupsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { InstantPauseComponent } from '../../../shared/instant-pause/instant-pause.component';
import { NotificationService } from '../../../shared/notification/notification.service';
import { BlockedServicesComponent } from '../../settings/blocked-services/blocked-services.component';
import { SafeSearchComponent } from '../../settings/safe-search/safe-search.component';

/**
 * This component is the create and edit page for a client group (`/client-groups/new` and
 * `/client-groups/:groupId/edit`); edit mode is chosen by the `groupId` route parameter. It saves
 * with `POST /api/client-groups` or `PUT /api/client-groups/{id}` (`clientgroups:write`, which
 * the routes also require).
 *
 * <p>In edit mode the page also shows the instant pause, Blocked Services and Safe Search panels
 * for the group. Networks, list assignments and schedules have their own pages, linked from the
 * group list. Checking "Default fallback group" shows a hint that the backend will take that
 * status from whichever group holds it now.
 */
@Component({
  selector: 'app-client-group-form',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    AlertComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardFooterComponent,
    CardHeaderComponent,
    FormCheckComponent,
    FormCheckInputDirective,
    FormCheckLabelDirective,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    SpinnerComponent,
    BlockedServicesComponent,
    SafeSearchComponent,
    InstantPauseComponent,
  ],
  templateUrl: './client-group-form.component.html',
  styleUrl: './client-group-form.component.scss',
})
export class ClientGroupFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly clientGroupsApi = inject(ClientGroupsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  /** The `groupId` route parameter, or `null` when creating; the edit-mode panels use it too. */
  readonly groupId = this.route.snapshot.paramMap.get('groupId');

  /** Whether this instance is editing an existing client group rather than creating a new one. */
  readonly isEditMode = this.groupId !== null;

  readonly loading = signal(this.isEditMode);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    name: ['', [Validators.required]],
    isDefault: [false],
    isDisabled: [false],
  });

  ngOnInit(): void {
    if (this.isEditMode) void this.loadExistingGroup();
  }

  /** This method creates the group (`POST`) or replaces it (`PUT`), then returns to the list. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const request = this.form.getRawValue();

    try {
      if (this.isEditMode && this.groupId) {
        await firstValueFrom(
          this.clientGroupsApi.apiClientGroupsIdPut({
            id: this.groupId,
            clientGroupWriteRequest: request,
          }),
        );
        this.notification.success('Client group saved.');
      } else {
        await firstValueFrom(
          this.clientGroupsApi.apiClientGroupsPost({ clientGroupWriteRequest: request }),
        );
        this.notification.success('Client group created.');
      }

      await this.router.navigate(['/client-groups']);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save this client group.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadExistingGroup(): Promise<void> {
    if (!this.groupId) return;

    try {
      const group = await firstValueFrom(
        this.clientGroupsApi.apiClientGroupsIdGet({ id: this.groupId }),
      );

      this.form.patchValue({
        name: group.name ?? '',
        isDefault: group.isDefault ?? false,
        isDisabled: group.isDisabled ?? false,
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load this client group.'));
    } finally {
      this.loading.set(false);
    }
  }
}
