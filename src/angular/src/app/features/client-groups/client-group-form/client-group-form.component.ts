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
 * This component is both the "create a client group" and "edit a client group" page -- one
 * component for both, distinguished by whether the route supplies a `groupId` (see
 * `app.routes.ts`). A group's own networks and filter-list assignments are managed separately,
 * through `ClientGroupNetworkListComponent`/`ClientGroupListAssignmentListComponent`.
 *
 * Checking "Default fallback group" here shows a hint explaining that `ClientGroupsController`'s
 * own real server-side behavior will demote whichever other group currently holds that status --
 * so an operator is never surprised by it happening silently.
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

  /** The route's own `groupId`, or `null` when creating a new group -- also the id `BlockedServicesComponent`/`SafeSearchComponent` scope their own panel to below. */
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

  /** This method creates (`POST`) or fully replaces (`PUT`) the client group, per {@link isEditMode}. */
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
