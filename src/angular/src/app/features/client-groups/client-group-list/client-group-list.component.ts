import { Component, inject, signal } from '@angular/core';
import { CdkTableModule } from '@angular/cdk/table';
import { RouterLink } from '@angular/router';
import {
  BadgeComponent,
  ButtonDirective,
  SpinnerComponent,
  TableDirective,
  TooltipDirective,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { firstValueFrom } from 'rxjs';
import { ClientGroupDetail, ClientGroupsService } from '../../../api-client';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../../shared/confirm-dialog/confirm-dialog.component';
import {
  CoreuiPaginationComponent,
  PageChangeEvent,
} from '../../../shared/coreui-pagination/coreui-pagination.component';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';

/** How many client groups one page of this list shows -- matches every other list page's own precedent. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component lists every client group this server knows about, backed by
 * `GET /api/client-groups`'s own real `skip`/`take` pagination, with actions to manage a group's
 * own networks and filter-list assignments, edit its metadata, toggle it disabled (via `PATCH`),
 * and delete it (behind a confirmation dialog, since `ClientGroupsController.Delete` also removes
 * every network and list assignment the group holds).
 */
@Component({
  selector: 'app-client-group-list',
  imports: [
    RouterLink,
    CdkTableModule,
    BadgeComponent,
    ButtonDirective,
    SpinnerComponent,
    TableDirective,
    TooltipDirective,
    IconDirective,
    CoreuiPaginationComponent,
    ConfirmDialogComponent,
    EmptyStateComponent,
  ],
  templateUrl: './client-group-list.component.html',
  styleUrl: './client-group-list.component.scss',
})
export class ClientGroupListComponent {
  private readonly clientGroupsApi = inject(ClientGroupsService);
  private readonly notification = inject(NotificationService);

  /** Whether {@link ConfirmDialogComponent} is currently shown for this list's pending make-default or delete action. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly displayedColumns = ['name', 'default', 'disabled', 'actions'];

  readonly groups = signal<ClientGroupDetail[]>([]);
  readonly totalCount = signal(0);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly pageIndex = signal(0);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  constructor() {
    void this.loadPage();
  }

  /** This method reloads the current page after a {@link CoreuiPaginationComponent} page/size change. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method toggles a client group's own `isDisabled` flag via a single-field RFC 7396 merge patch. */
  async toggleDisabled(group: ClientGroupDetail): Promise<void> {
    if (!group.id) return;

    try {
      await firstValueFrom(
        this.clientGroupsApi.apiClientGroupsIdPatch({
          id: group.id,
          body: { isDisabled: !group.isDisabled },
        }),
      );
      await this.loadPage();
      this.notification.success(
        group.isDisabled ? 'Client group enabled.' : 'Client group disabled.',
      );
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not update this client group.'));
    }
  }

  /**
   * This method promotes a client group to be the default fallback -- via a single-field merge
   * patch, surfacing `ClientGroupsController`'s own real demote-the-existing-default behavior as
   * a clear, explicit confirmation prompt first, so an operator is never surprised by another
   * group silently losing its own default status.
   */
  async makeDefault(group: ClientGroupDetail): Promise<void> {
    if (!group.id || group.isDefault) return;

    const confirmed = await this.confirm({
      title: `Make ${group.name} the default group?`,
      message:
        'Only one client group may be the default fallback at a time. Whichever group currently holds that status will be demoted automatically.',
      confirmLabel: 'Make default',
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(
        this.clientGroupsApi.apiClientGroupsIdPatch({ id: group.id, body: { isDefault: true } }),
      );
      await this.loadPage();
      this.notification.success(`${group.name} is now the default client group.`);
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not update this client group.'));
    }
  }

  /** This method confirms, then deletes, a client group -- including every network and list assignment it holds. */
  async deleteGroup(group: ClientGroupDetail): Promise<void> {
    if (!group.id) return;

    const confirmed = await this.confirm({
      title: `Delete ${group.name}?`,
      message:
        'This permanently deletes the client group, every network it matches, and every filter list assigned to it. This cannot be undone.',
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(this.clientGroupsApi.apiClientGroupsIdDelete({ id: group.id }));
      this.notification.success('Client group deleted.');
      await this.loadPage();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not delete this client group.'));
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

  private async loadPage(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const page = await firstValueFrom(
        this.clientGroupsApi.apiClientGroupsGet({
          skip: this.pageIndex() * this.pageSize(),
          take: this.pageSize(),
        }),
      );
      this.groups.set(page.items ?? []);
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load client groups.'));
    } finally {
      this.loading.set(false);
    }
  }
}
