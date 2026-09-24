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

/** How many credential sets one page of this list shows -- matches `ZoneListComponent`'s own default. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component lists every cloud DNS provider credential set this server knows about, backed by
 * `GET /api/cloud-provider-credentials`'s own real `skip`/`take` pagination. A credential set's
 * own secret material is never fetched or shown here -- `CloudProviderCredentialDetail` (the
 * shape this list's own `GET` returns) never carries it at all; only
 * `CloudProviderCredentialFormComponent`'s own create/rotate flows ever see a plaintext value.
 * Unlike a TSIG key, a credential set's own secret material genuinely is rotatable in place, so
 * this list's own "edit" action leads to a page that can both edit metadata and rotate credentials
 * -- never a delete-and-recreate workflow.
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

  /** Whether {@link ConfirmDialogComponent} is currently shown for this list's pending credential-set delete. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
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

  /** This method reloads the current page after a {@link CoreuiPaginationComponent} page/size change. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method toggles a credential set's own `isDisabled` flag via a single-field RFC 7396 merge patch. */
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

  /** This method confirms, then permanently deletes, a credential set. */
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
