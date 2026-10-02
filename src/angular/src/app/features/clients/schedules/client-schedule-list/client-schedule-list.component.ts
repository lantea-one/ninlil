import { Component, inject, signal } from '@angular/core';
import { CdkTableModule } from '@angular/cdk/table';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  ButtonDirective,
  SpinnerComponent,
  TableDirective,
  TooltipDirective,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { firstValueFrom } from 'rxjs';
import {
  ClientScheduleDetail,
  ClientSchedulesService,
  ClientsService,
} from '../../../../api-client';
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

/** The page size the list starts with, as on the other list pages. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component is a client's schedules page (`/clients/:clientId/schedules`, `clients:read`).
 * It lists the client's blocking schedules a page at a time from
 * `GET /api/clients/{clientId}/schedules`. With `clients:write` it links to add and edit and
 * deletes a schedule (`DELETE .../schedules/{id}`).
 */
@Component({
  selector: 'app-client-schedule-list',
  imports: [
    IfPermissionDirective,
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
  templateUrl: './client-schedule-list.component.html',
  styleUrl: './client-schedule-list.component.scss',
})
export class ClientScheduleListComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly schedulesApi = inject(ClientSchedulesService);
  private readonly clientsApi = inject(ClientsService);
  private readonly notification = inject(NotificationService);

  /** Whether the delete confirmation dialog is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Settles the pending {@link confirm} promise, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly clientId = this.route.snapshot.paramMap.get('clientId') ?? '';

  readonly displayedColumns = ['days', 'window', 'timeZone', 'status', 'actions'];

  readonly clientLabel = signal<string | null>(null);
  readonly schedules = signal<ClientScheduleDetail[]>([]);
  readonly totalCount = signal(0);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly pageIndex = signal(0);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  constructor() {
    void this.loadClientLabel();
    void this.loadPage();
  }

  /** This method loads the page the pagination bar asks for. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method asks for confirmation, then deletes a schedule from the client. */
  async deleteSchedule(schedule: ClientScheduleDetail): Promise<void> {
    if (!schedule.id) return;

    const confirmed = await this.confirm({
      title: 'Delete this schedule?',
      message: 'This permanently removes the schedule. This cannot be undone.',
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(
        this.schedulesApi.apiClientsClientIdSchedulesIdDelete({
          clientId: this.clientId,
          id: schedule.id,
        }),
      );
      this.notification.success('Schedule deleted.');
      await this.loadPage();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not delete this schedule.'));
    }
  }

  /** This method shows {@link ConfirmDialogComponent} with the given text and resolves once the operator confirms or dismisses it. */
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

  private async loadClientLabel(): Promise<void> {
    try {
      const client = await firstValueFrom(this.clientsApi.apiClientsIdGet({ id: this.clientId }));
      this.clientLabel.set(client.displayName ?? client.addresses?.[0]?.clientIdentifier ?? null);
    } catch {
      // The label is only for the heading; if the client is missing, loadPage() shows the error.
    }
  }

  private async loadPage(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const page = await firstValueFrom(
        this.schedulesApi.apiClientsClientIdSchedulesGet({
          clientId: this.clientId,
          skip: this.pageIndex() * this.pageSize(),
          take: this.pageSize(),
        }),
      );
      this.schedules.set(page.items ?? []);
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load schedules.'));
    } finally {
      this.loading.set(false);
    }
  }
}
