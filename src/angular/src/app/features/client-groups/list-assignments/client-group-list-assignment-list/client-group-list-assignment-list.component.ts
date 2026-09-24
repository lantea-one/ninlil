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
  ClientGroupListAssignmentDetail,
  ClientGroupListAssignmentsService,
  ClientGroupsService,
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

/** How many assignments one page of this list shows -- matches every other list page's own precedent. */
const DEFAULT_PAGE_SIZE = 25;

/** This interface pairs one raw assignment with the filter list name resolved for display. */
interface AssignmentRow {
  readonly assignment: ClientGroupListAssignmentDetail;
  readonly listName: string;
}

/**
 * This component lists every filter list currently assigned to one client group, backed by
 * `GET /api/client-groups/{groupId}/list-assignments`'s own real `skip`/`take` pagination. There
 * is no `PUT`/`PATCH` here, matching `ClientGroupListAssignmentsController`'s own contract -- an
 * assignment is just a join with no mutable field of its own; changing which list is assigned
 * means deleting one assignment and creating another.
 *
 * Since `ClientGroupListAssignmentDetail` carries only a raw `listId`, this component resolves
 * each assignment's own list name for display by fetching every filter list once and looking each
 * assignment's `listId` up in that set -- acceptable at this project's own homelab/small-org
 * target scale, the same in-memory-lookup reasoning `ForwardersController`'s own name lookup
 * already applies server-side.
 */
@Component({
  selector: 'app-client-group-list-assignment-list',
  imports: [
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
  templateUrl: './client-group-list-assignment-list.component.html',
  styleUrl: './client-group-list-assignment-list.component.scss',
})
export class ClientGroupListAssignmentListComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly listAssignmentsApi = inject(ClientGroupListAssignmentsService);
  private readonly clientGroupsApi = inject(ClientGroupsService);
  private readonly filterListsApi = inject(FilterListsService);
  private readonly notification = inject(NotificationService);

  /** Whether {@link ConfirmDialogComponent} is currently shown for this list's pending assignment removal. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly groupId = this.route.snapshot.paramMap.get('groupId') ?? '';

  readonly displayedColumns = ['listName', 'actions'];

  readonly groupName = signal<string | null>(null);
  readonly rows = signal<AssignmentRow[]>([]);
  readonly totalCount = signal(0);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly pageIndex = signal(0);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  constructor() {
    void this.loadGroupName();
    void this.loadPage();
  }

  /** This method reloads the current page after a {@link CoreuiPaginationComponent} page/size change. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method confirms, then removes, one filter list assignment from this client group. */
  async deleteAssignment(row: AssignmentRow): Promise<void> {
    if (!row.assignment.id) return;

    const confirmed = await this.confirm({
      title: 'Remove this filter list assignment?',
      message: `This stops '${row.listName}' from applying to this client group. This cannot be undone.`,
      confirmLabel: 'Remove',
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(
        this.listAssignmentsApi.apiClientGroupsGroupIdListAssignmentsIdDelete({
          groupId: this.groupId,
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
   * operator confirms or dismisses it -- the declarative analogue of the old
   * `MatDialog.open(ConfirmDialogComponent, { data }).afterClosed()` this project used before
   * migrating onto CoreUI for Angular.
   */
  private confirm(data: ConfirmDialogData): Promise<boolean> {
    this.confirmDialogData.set(data);
    this.confirmDialogVisible.set(true);
    return new Promise<boolean>((resolve) => {
      this.confirmResolve = resolve;
    });
  }

  /** This method runs once {@link ConfirmDialogComponent} resolves, hiding it and settling the pending {@link confirm} promise. */
  onConfirmDialogResolved(confirmed: boolean): void {
    this.confirmDialogVisible.set(false);
    this.confirmResolve?.(confirmed);
    this.confirmResolve = null;
  }

  private async loadGroupName(): Promise<void> {
    try {
      const group = await firstValueFrom(
        this.clientGroupsApi.apiClientGroupsIdGet({ id: this.groupId }),
      );
      this.groupName.set(group.name ?? null);
    } catch {
      // A missing group name is only cosmetic here -- the assignment list load below surfaces
      // its own real error if the owning group itself doesn't exist.
    }
  }

  private async loadPage(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const [page, allLists] = await Promise.all([
        firstValueFrom(
          this.listAssignmentsApi.apiClientGroupsGroupIdListAssignmentsGet({
            groupId: this.groupId,
            skip: this.pageIndex() * this.pageSize(),
            take: this.pageSize(),
          }),
        ),
        firstValueFrom(this.filterListsApi.apiFilterListsGet({ skip: 0, take: 500 })),
      ]);

      const listNamesById = new Map(
        (allLists.items ?? []).map((list) => [list.id, list.name ?? list.id ?? '']),
      );

      this.rows.set(
        (page.items ?? []).map((assignment) => ({
          assignment,
          listName: listNamesById.get(assignment.listId ?? '') ?? '(unknown list)',
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
