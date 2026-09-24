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

/** How many keys one page of this list shows -- matches `ZoneListComponent`'s own default. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component lists every TSIG key this server knows about, backed by `GET /api/tsig-keys`'s
 * own real `skip`/`take` pagination. A key's own shared secret is never fetched or shown here --
 * `TsigKeyDetail` (the shape this list's own `GET` returns) never carries one at all, per this
 * project's uniform "reveal exactly once, on creation" secret-exposure policy; only
 * `TsigKeyFormComponent`'s own create flow ever sees the plaintext value, via the dedicated
 * `TsigKeyCreateResponse` this list never receives. A TSIG key is immutable once created (rotate =
 * delete + recreate, never edit), which shows up here as an "edit" action that only ever reaches
 * `TsigKeyFormComponent`'s own metadata (name/algorithm/disabled/comments) fields, never a secret.
 */
@Component({
  selector: 'app-tsig-key-list',
  imports: [
    IfPermissionDirective,
    HasPermissionPipe,
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
  templateUrl: './tsig-key-list.component.html',
  styleUrl: './tsig-key-list.component.scss',
})
export class TsigKeyListComponent {
  private readonly tsigKeysApi = inject(TsigKeysService);
  private readonly notification = inject(NotificationService);

  /** Whether {@link ConfirmDialogComponent} is currently shown for this list's pending key delete. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
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

  /** This method reloads the current page after a {@link CoreuiPaginationComponent} page/size change. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method toggles a key's own `isDisabled` flag via a single-field RFC 7396 merge patch. */
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

  /** This method confirms, then permanently deletes, a TSIG key. */
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
