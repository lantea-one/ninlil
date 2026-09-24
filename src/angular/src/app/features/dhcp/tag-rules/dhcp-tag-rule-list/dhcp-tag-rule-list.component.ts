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
import { DhcpScopesService, DhcpTagRuleDetail, DhcpTagRulesService } from '../../../../api-client';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../../../shared/confirm-dialog/confirm-dialog.component';
import {
  CoreuiPaginationComponent,
  PageChangeEvent,
} from '../../../../shared/coreui-pagination/coreui-pagination.component';
import { EmptyStateComponent } from '../../../../shared/empty-state/empty-state.component';
import { extractErrorMessage } from '../../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../../shared/notification/notification.service';

/** How many tag rules one page of this list shows -- matches every other list page's own precedent. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component lists every tag-based conditional option delivery rule this server manages,
 * backed by `GET /api/dhcp-tag-rules`'s own real `skip`/`take` pagination, with actions to manage a
 * rule's own contributed options, edit its metadata, and delete it (behind a confirmation dialog,
 * since `DhcpTagRulesController.Delete` also removes every option the rule holds) -- the same
 * "one parent, one nested child collection" shape {@link DhcpScopeListComponent} already
 * establishes for a scope's own reservations/custom options.
 *
 * <p>A rule's own optional {@link DhcpTagRuleDetail.scopeId} is resolved to the owning scope's own
 * display name via a one-time lookup of every scope on load (a homelab-scale deployment has few
 * enough scopes that fetching them all up front is simpler and cheaper than a per-row lookup, or a
 * second server-side join this REST resource doesn't offer) -- a scope this session can't resolve
 * (deleted since the rule was created, or simply still loading) falls back to showing its raw
 * identifier rather than hiding the column.
 */
@Component({
  selector: 'app-dhcp-tag-rule-list',
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
    EmptyStateComponent,
  ],
  templateUrl: './dhcp-tag-rule-list.component.html',
  styleUrl: './dhcp-tag-rule-list.component.scss',
})
export class DhcpTagRuleListComponent {
  private readonly tagRulesApi = inject(DhcpTagRulesService);
  private readonly scopesApi = inject(DhcpScopesService);
  private readonly notification = inject(NotificationService);

  /** Whether {@link ConfirmDialogComponent} is currently shown for this list's pending delete. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly displayedColumns = ['name', 'scope', 'priority', 'status', 'actions'];

  readonly rules = signal<DhcpTagRuleDetail[]>([]);
  readonly totalCount = signal(0);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly pageIndex = signal(0);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  /** A one-time lookup of every scope's own display name, keyed by id -- see this class's own doc comment. */
  readonly scopeNamesById = signal<ReadonlyMap<string, string>>(new Map());

  constructor() {
    void this.loadScopeNames();
    void this.loadPage();
  }

  /** This method resolves a rule's own optional scope id to a display name, per this class's own doc comment. */
  scopeLabel(rule: DhcpTagRuleDetail): string {
    if (!rule.scopeId) return 'Any scope';
    return this.scopeNamesById().get(rule.scopeId) ?? rule.scopeId;
  }

  /** This method reloads the current page after a {@link CoreuiPaginationComponent} page/size change. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method toggles a rule's own `isDisabled` flag via a single-field RFC 7396 merge patch. */
  async toggleDisabled(rule: DhcpTagRuleDetail): Promise<void> {
    if (!rule.id) return;

    try {
      await firstValueFrom(
        this.tagRulesApi.apiDhcpTagRulesIdPatch({
          id: rule.id,
          body: { isDisabled: !rule.isDisabled },
        }),
      );
      await this.loadPage();
      this.notification.success(rule.isDisabled ? 'Rule enabled.' : 'Rule disabled.');
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not update this rule.'));
    }
  }

  /** This method confirms, then deletes, a tag rule -- including every option it contributes. */
  async deleteRule(rule: DhcpTagRuleDetail): Promise<void> {
    if (!rule.id) return;

    const confirmed = await this.confirm({
      title: `Delete ${rule.name}?`,
      message:
        'This permanently deletes the rule and every option it contributes. This cannot be undone.',
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(this.tagRulesApi.apiDhcpTagRulesIdDelete({ id: rule.id }));
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

  private async loadScopeNames(): Promise<void> {
    try {
      const page = await firstValueFrom(this.scopesApi.apiDhcpScopesGet({ skip: 0, take: 200 }));
      const byId = new Map<string, string>();
      for (const scope of page.items ?? []) {
        if (scope.id && scope.name) byId.set(scope.id, scope.name);
      }
      this.scopeNamesById.set(byId);
    } catch {
      // A missing scope-name lookup is only cosmetic -- scopeLabel() already falls back to the
      // raw identifier when a name isn't resolved.
    }
  }

  private async loadPage(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const page = await firstValueFrom(
        this.tagRulesApi.apiDhcpTagRulesGet({
          skip: this.pageIndex() * this.pageSize(),
          take: this.pageSize(),
        }),
      );
      this.rules.set(page.items ?? []);
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load tag rules.'));
    } finally {
      this.loading.set(false);
    }
  }
}
