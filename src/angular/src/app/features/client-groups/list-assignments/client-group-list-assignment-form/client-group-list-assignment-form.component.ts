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
import { AuthService } from '../../../../core/auth/auth.service';

/**
 * This component is the page that assigns a filter list to a client group
 * (`/client-groups/:groupId/list-assignments/new`, `clientgroups:write`). It saves with
 * `POST /api/client-groups/{groupId}/list-assignments`. There is no edit mode: the controller has
 * no `PUT` or `PATCH`, so changing the list means removing the assignment and adding another.
 *
 * <p>The select offers only lists not yet assigned to the group (from `GET /api/filter-lists`),
 * so the backend's 409 for a duplicate cannot happen. Without `filterlists:read` the page says no
 * lists can be offered.
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
  private readonly authService = inject(AuthService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly groupId = this.route.snapshot.paramMap.get('groupId') ?? '';

  /** The filter lists not yet assigned to this group, offered in the select. */
  readonly availableLists = signal<FilterListDetail[]>([]);

  /** Whether this session can't read filter lists (no `filterlists:read`), so has none to pick. */
  readonly listsUnavailable = signal(false);

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
    // Picking a list needs the filter lists themselves; without permission to read them, say so
    // (see listsUnavailable) instead of failing the page on a request the API would refuse.
    if (!this.authService.hasPermission('filterlists:read')) {
      this.listsUnavailable.set(true);
      this.loading.set(false);
      return;
    }

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
