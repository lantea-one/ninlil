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
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';
import { HasPermissionPipe } from '../../../shared/if-permission/has-permission.pipe';

/** The page size the list starts with, as on the other list pages. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component is the Client Groups page (`/client-groups`, `clientgroups:read`). It lists
 * groups a page at a time from `GET /api/client-groups` (`skip`/`take`) and links to each
 * group's networks, list assignments and schedules. With `clientgroups:write` it offers create,
 * edit, delete, "make default" and an enable/disable toggle (`PATCH /api/client-groups/{id}`);
 * without it the status buttons are disabled and the other write actions hidden.
 */
@Component({
  selector: 'app-client-group-list',
  imports: [
    IfPermissionDirective,
    HasPermissionPipe,
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

  /** Whether the confirmation dialog for "make default" or delete is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Settles the pending {@link confirm} promise, if any. */
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

  /** This method loads the page the pagination bar asks for. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method flips a group's `isDisabled` with a one-field merge patch (RFC 7396). */
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
   * This method makes a group the default fallback with a one-field merge patch. The backend
   * takes the status from the current default group, so the page asks for confirmation first.
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

  /** This method asks for confirmation, then deletes a group; the backend also deletes its networks, schedules, list assignments and Safe Search overrides. */
  async deleteGroup(group: ClientGroupDetail): Promise<void> {
    if (!group.id) return;

    const confirmed = await this.confirm({
      title: `Delete ${group.name}?`,
      message:
        'This permanently deletes the group, its networks, schedules and filter-list assignments, and its Safe Search overrides. Member clients fall back to network matching. This cannot be undone.',
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
