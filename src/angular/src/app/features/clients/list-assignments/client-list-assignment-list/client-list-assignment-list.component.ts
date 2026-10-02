import { Component, inject, signal } from '@angular/core';
import { CdkTableModule } from '@angular/cdk/table';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  ButtonDirective,
  SpinnerComponent,
  TableDirective,
  TooltipDirective,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { firstValueFrom } from 'rxjs';
import {
  ClientListAssignmentDetail,
  ClientListAssignmentsService,
  ClientsService,
  FilterListDetail,
  FilterListsService,
} from '../../../../api-client';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../../../shared/confirm-dialog/confirm-dialog.component';
import {
  CoreuiPaginationComponent,
  PageChangeEvent,
} from '../../../../shared/coreui-pagination/coreui-pagination.component';
import { EmptyStateComponent } from '../../../../shared/empty-state/empty-state.component';
import { extractErrorMessage } from '../../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../../shared/if-permission/if-permission.directive';
import { AuthService } from '../../../../core/auth/auth.service';

/** The page size the list starts with, as on the other list pages. */
const DEFAULT_PAGE_SIZE = 25;

/** This interface pairs one raw assignment with the filter list name resolved for display. */
interface AssignmentRow {
  readonly assignment: ClientListAssignmentDetail;
  readonly listName: string;
}

/**
 * This component is a client's filter list assignments page
 * (`/clients/:clientId/list-assignments`, `clients:read`). It works like
 * `ClientGroupListAssignmentListComponent`, against `/api/clients/{clientId}/list-assignments`
 * and with `clients:write` for adding and removing.
 *
 * <p>These direct assignments are added to the lists the client's group contributes; they do not
 * replace them (`DnsFilterEngine.ResolveApplicableListsAsync`). List names come from loading the
 * filter lists once, or the ids without `filterlists:read`.
 */
@Component({
  selector: 'app-client-list-assignment-list',
  imports: [
    IfPermissionDirective,
    RouterLink,
    CdkTableModule,
    ButtonDirective,
    SpinnerComponent,
    TableDirective,
    TooltipDirective,
    IconDirective,
    CoreuiPaginationComponent,
    ConfirmDialogComponent,
    EmptyStateComponent,
  ],
  templateUrl: './client-list-assignment-list.component.html',
  styleUrl: './client-list-assignment-list.component.scss',
})
export class ClientListAssignmentListComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly listAssignmentsApi = inject(ClientListAssignmentsService);
  private readonly clientsApi = inject(ClientsService);
  private readonly filterListsApi = inject(FilterListsService);
  private readonly authService = inject(AuthService);
  private readonly notification = inject(NotificationService);

  /** Whether the remove confirmation dialog is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Settles the pending {@link confirm} promise, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly clientId = this.route.snapshot.paramMap.get('clientId') ?? '';

  readonly displayedColumns = ['listName', 'actions'];

  readonly clientLabel = signal<string | null>(null);
  /** Whether filter list names couldn't be loaded (usually no `filterlists:read`). */
  readonly listNamesUnavailable = signal(false);

  readonly rows = signal<AssignmentRow[]>([]);
  readonly totalCount = signal(0);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly pageIndex = signal(0);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  constructor() {
    void this.loadClientLabel();
    void this.loadPage();
  }

  /** This method loads the page the pagination bar asks for. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method asks for confirmation, then removes an assignment from the client. */
  async deleteAssignment(row: AssignmentRow): Promise<void> {
    if (!row.assignment.id) return;

    const confirmed = await this.confirm({
      title: 'Remove this filter list assignment?',
      message: `This stops '${row.listName}' from applying directly to this client. This cannot be undone.`,
      confirmLabel: 'Remove',
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(
        this.listAssignmentsApi.apiClientsClientIdListAssignmentsIdDelete({
          clientId: this.clientId,
          id: row.assignment.id,
        }),
      );
      this.notification.success('Filter list assignment removed.');
      await this.loadPage();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not remove this assignment.'));
    }
  }

  /**
   * This method shows {@link ConfirmDialogComponent} with the given text and resolves once the
   * operator confirms or dismisses it.
   */
  private confirm(data: ConfirmDialogData): Promise<boolean> {
    this.confirmDialogData.set(data);
    this.confirmDialogVisible.set(true);
    return new Promise<boolean>((resolve) => {
      this.confirmResolve = resolve;
    });
  }

  /** This method hides the confirmation dialog and settles the pending {@link confirm} promise. */
  onConfirmDialogResolved(confirmed: boolean): void {
    this.confirmDialogVisible.set(false);
    this.confirmResolve?.(confirmed);
    this.confirmResolve = null;
  }

  private async loadClientLabel(): Promise<void> {
    try {
      const client = await firstValueFrom(this.clientsApi.apiClientsIdGet({ id: this.clientId }));
      this.clientLabel.set(client.displayName ?? client.addresses?.[0]?.clientIdentifier ?? null);
    } catch {
      // The label is only for the heading; if the client is missing, loadPage() shows the error.
    }
  }

  /**
   * This method fetches the filter lists (up to 500) to name each assignment's list. It returns
   * `null` without `filterlists:read` (the API would refuse) or when the request fails; the
   * assignments load either way.
   */
  private async loadFilterLists(): Promise<FilterListDetail[] | null> {
    if (!this.authService.hasPermission('filterlists:read')) return null;

    try {
      const allLists = await firstValueFrom(
        this.filterListsApi.apiFilterListsGet({ skip: 0, take: 500 }),
      );
      return allLists.items ?? [];
    } catch {
      return null;
    }
  }

  private async loadPage(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const [page, allLists] = await Promise.all([
        firstValueFrom(
          this.listAssignmentsApi.apiClientsClientIdListAssignmentsGet({
            clientId: this.clientId,
            skip: this.pageIndex() * this.pageSize(),
            take: this.pageSize(),
          }),
        ),
        this.loadFilterLists(),
      ]);

      this.listNamesUnavailable.set(allLists === null);
      const listNamesById = new Map(
        (allLists ?? []).map((list) => [list.id, list.name ?? list.id ?? '']),
      );

      this.rows.set(
        (page.items ?? []).map((assignment) => ({
          assignment,
          // Without the filter lists' names, the list's ID is the best this page can show.
          listName:
            allLists === null
              ? (assignment.listId ?? '')
              : (listNamesById.get(assignment.listId ?? '') ?? '(unknown list)'),
        })),
      );
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load filter list assignments.'));
    } finally {
      this.loading.set(false);
    }
  }
}
