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
  ClientListAssignmentsService,
  FilterListDetail,
  FilterListsService,
} from '../../../../api-client';
import { extractErrorMessage } from '../../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { AuthService } from '../../../../core/auth/auth.service';

/**
 * This component is the page that assigns a filter list directly to a client
 * (`/clients/:clientId/list-assignments/new`, `clients:write`). It saves with
 * `POST /api/clients/{clientId}/list-assignments` and works like
 * `ClientGroupListAssignmentFormComponent`: no edit mode, since the controller has no `PUT` or
 * `PATCH`, and the select offers only lists not yet assigned to the client. Without
 * `filterlists:read` the page says no lists can be offered.
 */
@Component({
  selector: 'app-client-list-assignment-form',
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
  templateUrl: './client-list-assignment-form.component.html',
  styleUrl: './client-list-assignment-form.component.scss',
})
export class ClientListAssignmentFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly listAssignmentsApi = inject(ClientListAssignmentsService);
  private readonly filterListsApi = inject(FilterListsService);
  private readonly authService = inject(AuthService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly clientId = this.route.snapshot.paramMap.get('clientId') ?? '';

  /** The filter lists not yet assigned to this client, offered in the select. */
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

  /** This method assigns the selected filter list directly to this client. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    try {
      await firstValueFrom(
        this.listAssignmentsApi.apiClientsClientIdListAssignmentsPost({
          clientId: this.clientId,
          clientListAssignmentWriteRequest: this.form.getRawValue(),
        }),
      );
      this.notification.success('Filter list assigned.');
      await this.router.navigate(['/clients', this.clientId, 'list-assignments']);
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
          this.listAssignmentsApi.apiClientsClientIdListAssignmentsGet({
            clientId: this.clientId,
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
