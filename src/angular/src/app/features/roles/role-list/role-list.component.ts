import { Component, inject, signal } from '@angular/core';
import { CdkTableModule } from '@angular/cdk/table';
import { RouterLink } from '@angular/router';
import {
  ButtonDirective,
  SpinnerComponent,
  TableDirective,
  TooltipDirective,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { firstValueFrom } from 'rxjs';
import { RoleSummary, RolesService } from '../../../api-client';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../../shared/confirm-dialog/confirm-dialog.component';
import {
  CoreuiPaginationComponent,
  PageChangeEvent,
} from '../../../shared/coreui-pagination/coreui-pagination.component';
import { ClusterSyncInfoComponent } from '../../../shared/cluster-sync-info/cluster-sync-info.component';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/** The page size the list starts with, as on the other list pages. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component is the Roles page (`/roles`, `roles:read`). It lists roles a page at a time from
 * `GET /api/roles`, each with how many permissions it grants. With `roles:write` it offers
 * create, edit (where the permissions are chosen) and delete (`DELETE /api/roles/{id}`).
 */
@Component({
  selector: 'app-role-list',
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
    ClusterSyncInfoComponent,
    EmptyStateComponent,
  ],
  templateUrl: './role-list.component.html',
  styleUrl: './role-list.component.scss',
})
export class RoleListComponent {
  private readonly rolesApi = inject(RolesService);
  private readonly notification = inject(NotificationService);

  /** Whether the delete confirmation dialog is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Settles the pending {@link confirm} promise, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly displayedColumns = ['name', 'description', 'permissionCount', 'sync', 'actions'];

  readonly roles = signal<RoleSummary[]>([]);
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

  /** This method asks for confirmation, then deletes a role. */
  async deleteRole(role: RoleSummary): Promise<void> {
    if (!role.id) return;

    const confirmed = await this.confirm({
      title: `Delete ${role.name}?`,
      message:
        'This permanently deletes the role. Any user currently holding it simply stops holding it -- their account itself is not affected. This cannot be undone.',
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(this.rolesApi.apiRolesIdDelete({ id: role.id }));
      this.notification.success('Role deleted.');
      await this.loadPage();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not delete this role.'));
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
        this.rolesApi.apiRolesGet({
          skip: this.pageIndex() * this.pageSize(),
          take: this.pageSize(),
        }),
      );
      this.roles.set(page.items ?? []);
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load roles.'));
    } finally {
      this.loading.set(false);
    }
  }
}
