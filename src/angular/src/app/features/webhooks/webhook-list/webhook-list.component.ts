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
import { WebhookSubscriptionDetail, WebhooksService } from '../../../api-client';
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
import { summarizeEventTypeNames } from '../webhook.model';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';
import { HasPermissionPipe } from '../../../shared/if-permission/has-permission.pipe';

/** The page size the list starts with, as on the other list pages. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component is the Webhooks page (`/webhooks`, `webhooks:read`). It lists subscriptions a page
 * at a time from `GET /api/webhooks` and links to each one's delivery history; the response never
 * includes the credentials or signing secret. With `webhooks:write` it offers create, edit,
 * delete and an enable/disable toggle (`PATCH`); without it the toggle is disabled and the rest
 * hidden.
 */
@Component({
  selector: 'app-webhook-list',
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
  templateUrl: './webhook-list.component.html',
  styleUrl: './webhook-list.component.scss',
})
export class WebhookListComponent {
  private readonly webhooksApi = inject(WebhooksService);
  private readonly notification = inject(NotificationService);

  /** Whether the delete confirmation dialog is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Settles the pending {@link confirm} promise, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly summarizeEventTypeNames = summarizeEventTypeNames;

  readonly displayedColumns = [
    'name',
    'targetUrl',
    'eventTypeNames',
    'authMode',
    'disabled',
    'actions',
  ];

  readonly subscriptions = signal<WebhookSubscriptionDetail[]>([]);
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

  /** This method flips a subscription's `isDisabled` with a one-field merge patch (RFC 7396). */
  async toggleDisabled(subscription: WebhookSubscriptionDetail): Promise<void> {
    if (!subscription.id) return;

    try {
      await firstValueFrom(
        this.webhooksApi.apiWebhooksIdPatch({
          id: subscription.id,
          body: { isDisabled: !subscription.isDisabled },
        }),
      );
      await this.loadPage();
      this.notification.success(
        subscription.isDisabled ? 'Subscription enabled.' : 'Subscription disabled.',
      );
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not update this subscription.'));
    }
  }

  /** This method asks for confirmation, then deletes a subscription. */
  async deleteSubscription(subscription: WebhookSubscriptionDetail): Promise<void> {
    if (!subscription.id) return;

    const confirmed = await this.confirm({
      title: `Delete ${subscription.name}?`,
      message:
        'This permanently deletes the subscription. No further events will ever be delivered to its target URL. This cannot be undone.',
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(this.webhooksApi.apiWebhooksIdDelete({ id: subscription.id }));
      this.notification.success('Subscription deleted.');
      await this.loadPage();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not delete this subscription.'));
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
        this.webhooksApi.apiWebhooksGet({
          skip: this.pageIndex() * this.pageSize(),
          take: this.pageSize(),
        }),
      );
      this.subscriptions.set(page.items ?? []);
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load webhook subscriptions.'));
    } finally {
      this.loading.set(false);
    }
  }
}
