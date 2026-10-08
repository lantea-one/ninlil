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
import { TsigKeyDetail, TsigKeysService } from '../../../api-client';
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
 * This component is the TSIG Keys page (`/tsig-keys`, `tsigkeys:read`). It lists keys a page at a
 * time from `GET /api/tsig-keys`; the response never includes the secret, which is shown only once
 * when a key is created. With `tsigkeys:write` it offers create, edit (name, algorithm, disabled
 * flag and comments, not the secret), delete and an enable/disable toggle (`PATCH`); without it
 * the toggle is disabled and the rest hidden. A key clustering created (`isClusterKey`) shows a
 * "Cluster" badge and has no edit or delete button, because the API refuses to rename or delete
 * it; the toggle still works, as an emergency switch.
 */
@Component({
  selector: 'app-tsig-key-list',
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
  templateUrl: './tsig-key-list.component.html',
  styleUrl: './tsig-key-list.component.scss',
})
export class TsigKeyListComponent {
  private readonly tsigKeysApi = inject(TsigKeysService);
  private readonly notification = inject(NotificationService);

  /** Whether the delete confirmation dialog is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Settles the pending {@link confirm} promise, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly displayedColumns = ['name', 'algorithmName', 'disabled', 'actions'];

  readonly keys = signal<TsigKeyDetail[]>([]);
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

  /** This method flips a key's `isDisabled` with a one-field merge patch (RFC 7396). */
  async toggleDisabled(key: TsigKeyDetail): Promise<void> {
    if (!key.id) return;

    try {
      await firstValueFrom(
        this.tsigKeysApi.apiTsigKeysIdPatch({ id: key.id, body: { isDisabled: !key.isDisabled } }),
      );
      await this.loadPage();
      this.notification.success(key.isDisabled ? 'Key enabled.' : 'Key disabled.');
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not update this key.'));
    }
  }

  /** This method asks for confirmation, then deletes a key. */
  async deleteKey(key: TsigKeyDetail): Promise<void> {
    if (!key.id) return;

    const confirmed = await this.confirm({
      title: `Delete ${key.name}?`,
      message:
        'This permanently deletes the key. Any zone still referencing it by name for zone transfers or dynamic updates will stop being able to authenticate against it. This cannot be undone.',
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(this.tsigKeysApi.apiTsigKeysIdDelete({ id: key.id }));
      this.notification.success('Key deleted.');
      await this.loadPage();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not delete this key.'));
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
        this.tsigKeysApi.apiTsigKeysGet({
          skip: this.pageIndex() * this.pageSize(),
          take: this.pageSize(),
        }),
      );
      this.keys.set(page.items ?? []);
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load TSIG keys.'));
    } finally {
      this.loading.set(false);
    }
  }
}
