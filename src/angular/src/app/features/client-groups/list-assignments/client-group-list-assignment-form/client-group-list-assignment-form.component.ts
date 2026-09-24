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
  FormDirective,
  FormLabelDirective,
  FormSelectDirective,
  SpinnerComponent,
} from '@coreui/angular';
import {
  ClientGroupListAssignmentsService,
  FilterListDetail,
  FilterListsService,
} from '../../../../api-client';
import { extractErrorMessage } from '../../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../../shared/notification/notification.service';

/**
 * This component assigns one existing filter list to a client group -- there is no edit mode,
 * matching `ClientGroupListAssignmentsController`'s own contract (no `PUT`/`PATCH`; changing
 * which list is assigned means deleting the old assignment and creating a new one). The select's
 * own options exclude a list already assigned to this group, so the backend's own duplicate-
 * assignment `409 Conflict` is avoided by construction rather than only caught after the fact.
 */
@Component({
  selector: 'app-client-group-list-assignment-form',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    AlertComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardFooterComponent,
    CardHeaderComponent,
    FormDirective,
    FormLabelDirective,
    FormSelectDirective,
    SpinnerComponent,
  ],
  templateUrl: './client-group-list-assignment-form.component.html',
  styleUrl: './client-group-list-assignment-form.component.scss',
})
export class ClientGroupListAssignmentFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly listAssignmentsApi = inject(ClientGroupListAssignmentsService);
  private readonly filterListsApi = inject(FilterListsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly groupId = this.route.snapshot.paramMap.get('groupId') ?? '';

  /** The filter lists this group is not yet assigned, offered as the select's own options. */
  readonly availableLists = signal<FilterListDetail[]>([]);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    listId: ['', [Validators.required]],
  });

  ngOnInit(): void {
    void this.loadAvailableLists();
  }

  /** This method assigns the selected filter list to this group. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    try {
      await firstValueFrom(
        this.listAssignmentsApi.apiClientGroupsGroupIdListAssignmentsPost({
          groupId: this.groupId,
          clientGroupListAssignmentWriteRequest: this.form.getRawValue(),
        }),
      );
      this.notification.success('Filter list assigned.');
      await this.router.navigate(['/client-groups', this.groupId, 'list-assignments']);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not assign this filter list.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadAvailableLists(): Promise<void> {
    try {
      const [allLists, existingAssignments] = await Promise.all([
        firstValueFrom(this.filterListsApi.apiFilterListsGet({ skip: 0, take: 500 })),
        firstValueFrom(
          this.listAssignmentsApi.apiClientGroupsGroupIdListAssignmentsGet({
            groupId: this.groupId,
            skip: 0,
            take: 500,
          }),
        ),
      ]);

      const assignedListIds = new Set(
        (existingAssignments.items ?? []).map((assignment) => assignment.listId),
      );

      this.availableLists.set(
        (allLists.items ?? []).filter((list) => !assignedListIds.has(list.id)),
      );
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not load the available filter lists.'),
      );
    } finally {
      this.loading.set(false);
    }
  }
}
