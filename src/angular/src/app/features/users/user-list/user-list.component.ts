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
import { UserSummary, UsersService } from '../../../api-client';
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
import {
  ResetPasswordDialogComponent,
  ResetPasswordDialogTarget,
} from '../reset-password-dialog/reset-password-dialog.component';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';
import { HasPermissionPipe } from '../../../shared/if-permission/has-permission.pipe';

/** The page size the list starts with, as on the other list pages. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component is the Users page (`/users`, `users:read`). It lists accounts a page at a time
 * from `GET /api/users`. With `users:write` it offers create, edit, delete, a password reset
 * ({@link ResetPasswordDialogComponent}) and an enable/disable toggle; without it the toggle is
 * disabled and the rest hidden. The toggle sends a full `PUT` with the row's display name and
 * e-mail unchanged, since the users API has no `PATCH` for an account.
 */
@Component({
  selector: 'app-user-list',
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
    ResetPasswordDialogComponent,
    ClusterSyncInfoComponent,
    EmptyStateComponent,
  ],
  templateUrl: './user-list.component.html',
  styleUrl: './user-list.component.scss',
})
export class UserListComponent {
  private readonly usersApi = inject(UsersService);
  private readonly notification = inject(NotificationService);

  /** Whether the delete confirmation dialog is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Settles the pending {@link confirm} promise, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  /** Whether the password reset dialog is shown. */
  readonly resetPasswordDialogVisible = signal(false);

  /** The account the password reset dialog is for. */
  readonly resetPasswordDialogTarget = signal<ResetPasswordDialogTarget | null>(null);

  readonly displayedColumns = [
    'userName',
    'email',
    'roles',
    'twoFactor',
    'disabled',
    'sync',
    'actions',
  ];

  readonly users = signal<UserSummary[]>([]);
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

  /** This method enables or disables an account with a full `PUT`. */
  async toggleDisabled(user: UserSummary): Promise<void> {
    if (!user.id) return;

    try {
      await firstValueFrom(
        this.usersApi.apiUsersIdPut({
          id: user.id,
          updateUserRequest: {
            displayName: user.displayName ?? '',
            email: user.email ?? '',
            isDisabled: !user.isDisabled,
          },
        }),
      );
      await this.loadPage();
      this.notification.success(user.isDisabled ? 'Account enabled.' : 'Account disabled.');
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not update this account.'));
    }
  }

  /** This method opens the password reset dialog for an account, for a user who cannot change the password on the Profile page because they do not know it. */
  resetPassword(user: UserSummary): void {
    if (!user.id) return;

    this.resetPasswordDialogTarget.set({
      id: user.id,
      userName: user.userName ?? '',
      email: user.email,
    });
    this.resetPasswordDialogVisible.set(true);
  }

  /** This method asks for confirmation, then deletes an account. */
  async deleteUser(user: UserSummary): Promise<void> {
    if (!user.id) return;

    const confirmed = await this.confirm({
      title: `Delete ${user.userName}?`,
      message: 'This permanently deletes the account. This cannot be undone.',
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(this.usersApi.apiUsersIdDelete({ id: user.id }));
      this.notification.success('Account deleted.');
      await this.loadPage();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not delete this account.'));
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
        this.usersApi.apiUsersGet({
          skip: this.pageIndex() * this.pageSize(),
          take: this.pageSize(),
        }),
      );
      this.users.set(page.items ?? []);
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load user accounts.'));
    } finally {
      this.loading.set(false);
    }
  }
}
