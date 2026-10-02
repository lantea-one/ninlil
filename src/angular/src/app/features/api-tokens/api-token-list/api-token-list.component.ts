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
 * This component is the API Tokens page (`/api-tokens`). It lists the signed-in user's tokens
 * from `GET /api/api-tokens` (not paged) and revokes one with `DELETE /api/api-tokens/{id}`.
 * `ApiTokensController` needs only a session and works on the caller's tokens alone, so the page
 * has no permission check. Raw token values are never listed; they are shown once, at creation.
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

  /** Whether the revoke confirmation dialog is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Settles the pending {@link confirm} promise, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly displayedColumns = ['name', 'type', 'createdAtUtc', 'expiresAtUtc', 'status', 'actions'];

  readonly tokens = signal<ApiTokenSummary[]>([]);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  /** This method returns a token's status for display: Revoked, Consumed, Expired or Active. */
  status(token: ApiTokenSummary): string {
    if (token.revokedAtUtc) return 'Revoked';
    if (token.type === 'OneTime' && token.consumedAtUtc) return 'Consumed';
    if (token.expiresAtUtc && new Date(token.expiresAtUtc).getTime() < Date.now()) {
      return 'Expired';
    }
    return 'Active';
  }

  /** This method returns the badge color for a token's status. */
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

  /** This method asks for confirmation, then revokes a token and reloads the list. */
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
