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

/**
 * This component assigns one existing filter list directly to a known client -- the client-level
 * analogue of `ClientGroupListAssignmentFormComponent`, mirroring its shape exactly. There is no
 * edit mode, matching `ClientListAssignmentsController`'s own contract (no `PUT`/`PATCH`; changing
 * which list is assigned means deleting the old assignment and creating a new one). The select's
 * own options exclude a list already assigned directly to this client, so the backend's own
 * duplicate-assignment `409 Conflict` is avoided by construction rather than only caught after the
 * fact.
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
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly clientId = this.route.snapshot.paramMap.get('clientId') ?? '';

  /** The filter lists this client is not yet directly assigned, offered as the select's own options. */
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
