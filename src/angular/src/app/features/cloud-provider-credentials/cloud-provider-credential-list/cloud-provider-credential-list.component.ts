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
import {
  CloudProviderCredentialDetail,
  CloudProviderCredentialsService,
} from '../../../api-client';
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
 * This component is the Cloud Credentials page (`/cloud-provider-credentials`,
 * `cloudprovidercredentials:read`). It lists credential sets a page at a time from
 * `GET /api/cloud-provider-credentials`; the response never includes the secrets. With
 * `cloudprovidercredentials:write` it offers create, edit (which can also rotate the secrets),
 * delete and an enable/disable toggle (`PATCH`); without it the toggle is disabled and the rest
 * hidden. Deleting a set that zones still use is allowed; those zones then stop syncing.
 */
@Component({
  selector: 'app-cloud-provider-credential-list',
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
  templateUrl: './cloud-provider-credential-list.component.html',
  styleUrl: './cloud-provider-credential-list.component.scss',
})
export class CloudProviderCredentialListComponent {
  private readonly credentialsApi = inject(CloudProviderCredentialsService);
  private readonly notification = inject(NotificationService);

  /** Whether the delete confirmation dialog is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Settles the pending {@link confirm} promise, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly displayedColumns = ['name', 'providerType', 'disabled', 'actions'];

  readonly credentials = signal<CloudProviderCredentialDetail[]>([]);
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

  /** This method flips a credential set's `isDisabled` with a one-field merge patch (RFC 7396). */
  async toggleDisabled(credential: CloudProviderCredentialDetail): Promise<void> {
    if (!credential.id) return;

    try {
      await firstValueFrom(
        this.credentialsApi.apiCloudProviderCredentialsIdPatch({
          id: credential.id,
          body: { isDisabled: !credential.isDisabled },
        }),
      );
      await this.loadPage();
      this.notification.success(
        credential.isDisabled ? 'Credential set enabled.' : 'Credential set disabled.',
      );
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not update this credential set.'));
    }
  }

  /** This method asks for confirmation, then deletes a credential set. */
  async deleteCredential(credential: CloudProviderCredentialDetail): Promise<void> {
    if (!credential.id) return;

    const confirmed = await this.confirm({
      title: `Delete ${credential.name}?`,
      message:
        'This permanently deletes the credential set. Any zone still referencing it for cloud sync will stop being able to authenticate against it. This cannot be undone.',
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(
        this.credentialsApi.apiCloudProviderCredentialsIdDelete({ id: credential.id }),
      );
      this.notification.success('Credential set deleted.');
      await this.loadPage();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not delete this credential set.'));
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
        this.credentialsApi.apiCloudProviderCredentialsGet({
          skip: this.pageIndex() * this.pageSize(),
          take: this.pageSize(),
        }),
      );
      this.credentials.set(page.items ?? []);
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not load cloud provider credentials.'),
      );
    } finally {
      this.loading.set(false);
    }
  }
}
