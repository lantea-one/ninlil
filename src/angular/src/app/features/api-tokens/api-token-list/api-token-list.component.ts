import { DatePipe } from '@angular/common';
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
import { ApiTokenSummary, ApiTokensService } from '../../../api-client';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../../shared/confirm-dialog/confirm-dialog.component';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';

/**
 * This component lists the current caller's own API tokens, via `GET /api/api-tokens` -- a
 * plain, unpaginated array, scoped server-side to the signed-in caller alone
 * (`ApiTokensController` reads `User.GetUserId()` directly, never an `id` route parameter), which
 * is also why this whole feature carries no `permission` in `NAV_ITEMS` at all: possessing a
 * valid session is itself sufficient authorization to manage one's own tokens. A token's own raw
 * value is never shown here -- `ApiTokenSummary` (this list's own response shape) never carries
 * one at all; only `ApiTokenFormComponent`'s own create flow ever sees it.
 */
@Component({
  selector: 'app-api-token-list',
  imports: [
    DatePipe,
    RouterLink,
    CdkTableModule,
    BadgeComponent,
    ButtonDirective,
    SpinnerComponent,
    TableDirective,
    TooltipDirective,
    IconDirective,
    ConfirmDialogComponent,
    EmptyStateComponent,
  ],
  templateUrl: './api-token-list.component.html',
  styleUrl: './api-token-list.component.scss',
})
export class ApiTokenListComponent {
  private readonly apiTokensApi = inject(ApiTokensService);
  private readonly notification = inject(NotificationService);

  /** Whether {@link ConfirmDialogComponent} is currently shown for this list's pending revoke. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly displayedColumns = ['name', 'type', 'createdAtUtc', 'expiresAtUtc', 'status', 'actions'];

  readonly tokens = signal<ApiTokenSummary[]>([]);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  /** This method reports a token's own current status, for display. */
  status(token: ApiTokenSummary): string {
    if (token.revokedAtUtc) return 'Revoked';
    if (token.type === 'OneTime' && token.consumedAtUtc) return 'Consumed';
    if (token.expiresAtUtc && new Date(token.expiresAtUtc).getTime() < Date.now()) {
      return 'Expired';
    }
    return 'Active';
  }

  /** This method reports which badge color best represents a token's own current status. */
  statusColor(token: ApiTokenSummary): string {
    switch (this.status(token)) {
      case 'Active':
        return 'success';
      case 'Revoked':
        return 'danger';
      case 'Expired':
        return 'secondary';
      default:
        return 'info';
    }
  }

  /** This method confirms, then revokes, one of the current caller's own tokens. */
  async revokeToken(token: ApiTokenSummary): Promise<void> {
    if (!token.id) return;

    const confirmed = await this.confirm({
      title: `Revoke ${token.name}?`,
      message: 'This token will no longer be able to authenticate. This cannot be undone.',
      confirmLabel: 'Revoke',
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(this.apiTokensApi.apiApiTokensIdDelete({ id: token.id }));
      this.notification.success('Token revoked.');
      await this.load();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not revoke this token.'));
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

  private async load(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const tokens = await firstValueFrom(this.apiTokensApi.apiApiTokensGet());
      this.tokens.set(tokens);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load your API tokens.'));
    } finally {
      this.loading.set(false);
    }
  }
}
