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

/** How many subscriptions one page of this list shows -- matches every other list's own default. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component lists every outbound webhook subscription this server knows about, backed by
 * `GET /api/webhooks`'s own real `skip`/`take` pagination. Neither a subscription's own auth
 * credential(s) nor its HMAC signing secret is ever fetched or shown here -- `WebhookSubscriptionDetail`
 * (the shape this list's own `GET` returns) never carries either, per this project's uniform
 * "reveal exactly once, on creation" secret-exposure policy; only `WebhookFormComponent`'s own
 * create flow ever sees either plaintext value, via the dedicated `WebhookSubscriptionCreateResponse`
 * this list never receives.
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

  /** Whether {@link ConfirmDialogComponent} is currently shown for this list's pending delete. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
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

  /** This method reloads the current page after a {@link CoreuiPaginationComponent} page/size change. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method toggles a subscription's own `isDisabled` flag via a single-field RFC 7396 merge patch. */
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

  /** This method confirms, then permanently deletes, a webhook subscription. */
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
