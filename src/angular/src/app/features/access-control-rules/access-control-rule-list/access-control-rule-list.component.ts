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
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';
import { HasPermissionPipe } from '../../../shared/if-permission/has-permission.pipe';

const DEFAULT_PAGE_SIZE = 25;

/**
 * This component is the Access Control Rules page (`/access-control-rules`, `accesscontrol:read`).
 * It lists the rules a page at a time from `GET /api/access-control-rules` (`skip`/`take`), with
 * each granted capability as a badge. With `accesscontrol:write` it offers create, edit, delete
 * (`DELETE /api/access-control-rules/{id}`) and an enable/disable toggle; without it the toggle
 * is shown disabled and the other actions are hidden.
 */
@Component({
  selector: 'app-access-control-rule-list',
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
    ClusterSyncInfoComponent,
    EmptyStateComponent,
  ],
  templateUrl: './access-control-rule-list.component.html',
  styleUrl: './access-control-rule-list.component.scss',
})
export class AccessControlRuleListComponent {
  private readonly accessControlRulesApi = inject(DnsAccessControlRulesService);
  private readonly notification = inject(NotificationService);

  /** Whether the delete confirmation dialog is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Settles the pending {@link confirm} promise, if any. */
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

  /** This method loads the page the pagination bar asks for. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method flips a rule's `isDisabled` with a full `PUT`, since the controller has no `PATCH`. */
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
