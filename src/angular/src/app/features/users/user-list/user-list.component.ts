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

/** How many accounts one page of this list shows -- matches `ZoneListComponent`'s own default. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component lists every user account on this server, backed by `GET /api/users`'s own real
 * `skip`/`take` pagination (`UsersController`, `UserSummaryPagedResult`). A row's own
 * enabled/disabled toggle uses a full `PUT` (`UsersController.Update`'s own `UpdateUserRequest`),
 * not a merge patch -- unlike `TsigKeyListComponent`'s own `PATCH`-based toggle, `UsersController`
 * exposes no `PATCH` action at all, so this sends the row's own already-loaded
 * `displayName`/`email` back unchanged alongside the flipped `isDisabled` flag.
 */
@Component({
  selector: 'app-user-list',
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

  /** Whether {@link ConfirmDialogComponent} is currently shown for this list's pending delete. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  /** Whether {@link ResetPasswordDialogComponent} is currently shown for this list's pending administrator-driven password reset. */
  readonly resetPasswordDialogVisible = signal(false);

  /** Which account {@link ResetPasswordDialogComponent} is currently resetting the password for. */
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

  /** This method reloads the current page after a {@link CoreuiPaginationComponent} page/size change. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method toggles a user account's own enabled/disabled state via a full `PUT`. */
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

  /** This method opens {@link ResetPasswordDialogComponent} for a given account -- built for a locked-out account that cannot use the self-service change-password flow on `AccountProfileComponent`'s own Profile page at all, since that flow requires already knowing the account's current password. */
  resetPassword(user: UserSummary): void {
    if (!user.id) return;

    this.resetPasswordDialogTarget.set({
      id: user.id,
      userName: user.userName ?? '',
      email: user.email,
    });
    this.resetPasswordDialogVisible.set(true);
  }

  /** This method confirms, then permanently deletes, a user account. */
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
