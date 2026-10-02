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
import { IfPermissionDirective } from '../../../../shared/if-permission/if-permission.directive';
import { HasPermissionPipe } from '../../../../shared/if-permission/has-permission.pipe';

/** The page size the list starts with, as on the other list pages. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component is the DHCP Tag Rules page (`/dhcp-tag-rules`, `dhcptagrules:read`). It lists
 * rules a page at a time from `GET /api/dhcp-tag-rules`. With `dhcptagrules:write` it offers
 * create, edit (which includes the rule's options), delete and an enable/disable toggle
 * (`PATCH`); without it the toggle is disabled and the rest hidden. Deleting a rule also deletes
 * its options.
 *
 * <p>A rule's optional scope is shown by name, from one load of all scopes. A scope that cannot
 * be named (deleted, still loading, or no `dhcpscopes:read`) shows its id.
 */
@Component({
  selector: 'app-dhcp-tag-rule-list',
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
  templateUrl: './dhcp-tag-rule-list.component.html',
  styleUrl: './dhcp-tag-rule-list.component.scss',
})
export class DhcpTagRuleListComponent {
  private readonly tagRulesApi = inject(DhcpTagRulesService);
  private readonly scopesApi = inject(DhcpScopesService);
  private readonly notification = inject(NotificationService);

  /** Whether the delete confirmation dialog is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Settles the pending {@link confirm} promise, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly displayedColumns = ['name', 'scope', 'priority', 'status', 'actions'];

  readonly rules = signal<DhcpTagRuleDetail[]>([]);
  readonly totalCount = signal(0);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly pageIndex = signal(0);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  /** The scope names, by id, loaded once. */
  readonly scopeNamesById = signal<ReadonlyMap<string, string>>(new Map());

  constructor() {
    void this.loadScopeNames();
    void this.loadPage();
  }

  /** This method returns the scope column text: the scope name, its id when unknown, or a placeholder for no scope. */
  scopeLabel(rule: DhcpTagRuleDetail): string {
    if (!rule.scopeId) return 'Any scope';
    return this.scopeNamesById().get(rule.scopeId) ?? rule.scopeId;
  }

  /** This method loads the page the pagination bar asks for. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method flips a rule's `isDisabled` with a one-field merge patch (RFC 7396). */
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

  /** This method asks for confirmation, then deletes a rule with its options. */
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

  /** This method hides the confirmation dialog and settles the pending {@link confirm} promise. */
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
      // Without the names, scopeLabel() shows the scope ids.
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
