import { Component, computed, inject, signal } from '@angular/core';
import { Clipboard } from '@angular/cdk/clipboard';
import { CdkTableModule } from '@angular/cdk/table';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  BadgeComponent,
  ButtonDirective,
  ModalBodyComponent,
  ModalComponent,
  ModalFooterComponent,
  ModalHeaderComponent,
  ModalTitleDirective,
  SpinnerComponent,
  TableDirective,
  TooltipDirective,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { firstValueFrom } from 'rxjs';
import {
  WebhookDeliveryAttemptDetail,
  WebhookDeliveryAttemptStatusEnum,
  WebhookSubscriptionDetail,
  WebhooksService,
} from '../../../api-client';
import {
  CoreuiPaginationComponent,
  PageChangeEvent,
} from '../../../shared/coreui-pagination/coreui-pagination.component';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';

/** How many delivery attempts one page of this history view shows -- matches every other list's own default. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component shows a single webhook subscription's own persisted delivery-attempt history,
 * backed by `GET /api/webhooks/{id}/delivery-attempts`'s own real `skip`/`take` pagination,
 * most-recently-created attempt first. This is a read-only view: there is no create/edit affordance
 * anywhere here, since a delivery-attempt row is written exclusively by this server's own delivery
 * engine (`WebhookDeliveryService`/`WebhookDeliveryRetrySweepService`), never by an operator -- the
 * closest existing precedent for a route with no write affordance at all is
 * `QueryLogListComponent`, for the same underlying reason (server-written history, not
 * operator-authored data).
 */
@Component({
  selector: 'app-webhook-delivery-attempts',
  imports: [
    DatePipe,
    RouterLink,
    CdkTableModule,
    BadgeComponent,
    ButtonDirective,
    ModalBodyComponent,
    ModalComponent,
    ModalFooterComponent,
    ModalHeaderComponent,
    ModalTitleDirective,
    SpinnerComponent,
    TableDirective,
    TooltipDirective,
    IconDirective,
    CoreuiPaginationComponent,
    EmptyStateComponent,
  ],
  templateUrl: './webhook-delivery-attempts.component.html',
  styleUrl: './webhook-delivery-attempts.component.scss',
})
export class WebhookDeliveryAttemptsComponent {
  private readonly webhooksApi = inject(WebhooksService);
  private readonly route = inject(ActivatedRoute);
  private readonly clipboard = inject(Clipboard);

  /** The subscription identifier this history view targets, taken from the route's own `:id` segment. */
  private readonly subscriptionId = this.route.snapshot.paramMap.get('id') ?? '';

  readonly displayedColumns = [
    'eventTypeName',
    'status',
    'attemptCount',
    'lastAttemptedAtUtc',
    'nextRetryDueUtc',
    'lastHttpStatusCode',
    'lastErrorMessage',
    'completedAtUtc',
    'payload',
  ];

  /** The delivery whose request body is shown in the payload dialog, or `null` while it's closed. */
  readonly payloadAttempt = signal<WebhookDeliveryAttemptDetail | null>(null);

  /** The shown payload, pretty-printed; the stored text as-is if it somehow isn't valid JSON. */
  readonly formattedPayload = computed(() => formatPayload(this.payloadAttempt()?.payloadJson));

  /** Whether the dialog's copy button has just copied, to swap its icon and tooltip. */
  readonly payloadCopied = signal(false);

  /** The targeted subscription's own name, shown in this page's heading once loaded. */
  readonly subscription = signal<WebhookSubscriptionDetail | null>(null);

  readonly attempts = signal<WebhookDeliveryAttemptDetail[]>([]);
  readonly totalCount = signal(0);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly pageIndex = signal(0);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  constructor() {
    void this.loadSubscription();
    void this.loadPage();
  }

  /** This method reloads the current page after a {@link CoreuiPaginationComponent} page/size change. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method maps a delivery attempt's own status onto the {@link BadgeComponent} color that best conveys its severity. */
  statusBadgeColor(status: WebhookDeliveryAttemptStatusEnum | undefined): string {
    switch (status) {
      case WebhookDeliveryAttemptStatusEnum.Succeeded:
        return 'success';
      case WebhookDeliveryAttemptStatusEnum.DeadLettered:
        return 'danger';
      default:
        return 'warning';
    }
  }

  /** This method opens the payload dialog for one delivery. */
  showPayload(attempt: WebhookDeliveryAttemptDetail): void {
    this.payloadCopied.set(false);
    this.payloadAttempt.set(attempt);
  }

  /** This method closes the payload dialog. */
  closePayload(): void {
    this.payloadAttempt.set(null);
  }

  /** This method copies the payload exactly as it was sent, not the pretty-printed form. */
  copyPayload(): void {
    const payload = this.payloadAttempt()?.payloadJson;
    if (!payload) return;
    this.payloadCopied.set(this.clipboard.copy(payload));
  }

  private async loadSubscription(): Promise<void> {
    if (!this.subscriptionId) return;

    try {
      const subscription = await firstValueFrom(
        this.webhooksApi.apiWebhooksIdGet({ id: this.subscriptionId }),
      );
      this.subscription.set(subscription);
    } catch {
      // The page still works with just the identifier in its heading if this lookup fails
      // (a subscription deleted moments after this page was opened, for example) -- the
      // delivery-attempt history itself is unaffected, since WebhookDeliveryAttemptEntity
      // snapshots its own subscription name independently (see that entity's own doc comment).
    }
  }

  private async loadPage(): Promise<void> {
    if (!this.subscriptionId) {
      this.errorMessage.set('No subscription was specified.');
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const page = await firstValueFrom(
        this.webhooksApi.apiWebhooksIdDeliveryAttemptsGet({
          id: this.subscriptionId,
          skip: this.pageIndex() * this.pageSize(),
          take: this.pageSize(),
        }),
      );
      this.attempts.set(page.items ?? []);
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not load this subscription’s delivery history.'),
      );
    } finally {
      this.loading.set(false);
    }
  }
}

/**
 * This function pretty-prints a delivery's stored request body for reading. The body is what the
 * server sent, so anything that doesn't parse is shown unchanged rather than hidden.
 */
export function formatPayload(payloadJson: string | null | undefined): string {
  if (!payloadJson) return '';
  try {
    return JSON.stringify(JSON.parse(payloadJson), null, 2);
  } catch {
    return payloadJson;
  }
}
