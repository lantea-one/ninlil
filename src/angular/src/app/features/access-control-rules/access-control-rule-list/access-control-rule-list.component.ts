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
import { DnsAccessControlRuleDetail, DnsAccessControlRulesService } from '../../../api-client';
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
import { ClusterSyncInfoComponent } from '../../../shared/cluster-sync-info/cluster-sync-info.component';

const DEFAULT_PAGE_SIZE = 25;

/**
 * This component lists every access-control rule this server knows about
 * (`DnsAccessControlRuleEntity`, one row per network+capability set -- entirely flat, no owning
 * parent), backed by `GET /api/access-control-rules`'s own real `skip`/`take` pagination. Every
 * capability a rule grants is shown as its own badge, since `DnsAccessControlRuleDetail
 * .Capabilities` is already the REST-boundary array-of-strings projection
 * `DnsAccessControlRulesController` computes from the entity's own stored bitmask -- this
 * component never touches the bitmask itself.
 */
@Component({
  selector: 'app-access-control-rule-list',
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
    ClusterSyncInfoComponent,
    EmptyStateComponent,
  ],
  templateUrl: './access-control-rule-list.component.html',
  styleUrl: './access-control-rule-list.component.scss',
})
export class AccessControlRuleListComponent {
  private readonly accessControlRulesApi = inject(DnsAccessControlRulesService);
  private readonly notification = inject(NotificationService);

  /** Whether {@link ConfirmDialogComponent} is currently shown for this list's pending delete. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly displayedColumns = ['name', 'cidr', 'capabilities', 'disabled', 'sync', 'actions'];

  readonly rules = signal<DnsAccessControlRuleDetail[]>([]);
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

  /** This method toggles a rule's own `isDisabled` flag via a full `PUT` (this controller has no `PATCH`). */
  async toggleDisabled(rule: DnsAccessControlRuleDetail): Promise<void> {
    if (!rule.id) return;

    try {
      await firstValueFrom(
        this.accessControlRulesApi.apiAccessControlRulesIdPut({
          id: rule.id,
          dnsAccessControlRuleWriteRequest: {
            name: rule.name,
            cidr: rule.cidr,
            capabilities: rule.capabilities,
            isDisabled: !rule.isDisabled,
          },
        }),
      );
      await this.loadPage();
      this.notification.success(rule.isDisabled ? 'Rule enabled.' : 'Rule disabled.');
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not update this rule.'));
    }
  }

  /** This method confirms, then deletes, an access-control rule. */
  async deleteRule(rule: DnsAccessControlRuleDetail): Promise<void> {
    if (!rule.id) return;

    const confirmed = await this.confirm({
      title: `Delete ${rule.name}?`,
      message:
        'This permanently removes this access-control rule. Any client relying on it for recursion access or rate-limit exemption will lose that capability immediately. This cannot be undone.',
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(
        this.accessControlRulesApi.apiAccessControlRulesIdDelete({ id: rule.id }),
      );
      this.notification.success('Rule deleted.');
      await this.loadPage();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not delete this rule.'));
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
        this.accessControlRulesApi.apiAccessControlRulesGet({
          skip: this.pageIndex() * this.pageSize(),
          take: this.pageSize(),
        }),
      );
      this.rules.set(page.items ?? []);
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load access-control rules.'));
    } finally {
      this.loading.set(false);
    }
  }
}
