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
  ClientGroupScheduleDetail,
  ClientGroupSchedulesService,
  ClientGroupsService,
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

/** How many schedules one page of this list shows -- matches every other list page's own precedent. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component lists every time-of-day parental-control schedule assigned to one client
 * group, backed by `GET /api/client-groups/{groupId}/schedules`'s own real `skip`/`take`
 * pagination -- mirrors `ClientScheduleListComponent` exactly, keyed by client group instead of a
 * single client. Every client resolved into this group is blocked by an active schedule here,
 * independently of (unioned with) its own direct schedule.
 */
@Component({
  selector: 'app-client-group-schedule-list',
  imports: [
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
  templateUrl: './client-group-schedule-list.component.html',
  styleUrl: './client-group-schedule-list.component.scss',
})
export class ClientGroupScheduleListComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly schedulesApi = inject(ClientGroupSchedulesService);
  private readonly clientGroupsApi = inject(ClientGroupsService);
  private readonly notification = inject(NotificationService);

  /** Whether {@link ConfirmDialogComponent} is currently shown for this list's pending delete. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly groupId = this.route.snapshot.paramMap.get('groupId') ?? '';

  readonly displayedColumns = ['days', 'window', 'timeZone', 'status', 'actions'];

  readonly groupName = signal<string | null>(null);
  readonly schedules = signal<ClientGroupScheduleDetail[]>([]);
  readonly totalCount = signal(0);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly pageIndex = signal(0);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  constructor() {
    void this.loadGroupName();
    void this.loadPage();
  }

  /** This method reloads the current page after a {@link CoreuiPaginationComponent} page/size change. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method confirms, then deletes, one schedule from this client group. */
  async deleteSchedule(schedule: ClientGroupScheduleDetail): Promise<void> {
    if (!schedule.id) return;

    const confirmed = await this.confirm({
      title: 'Delete this schedule?',
      message: 'This permanently removes the schedule. This cannot be undone.',
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(
        this.schedulesApi.apiClientGroupsGroupIdSchedulesIdDelete({
          groupId: this.groupId,
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

  /** This method runs once {@link ConfirmDialogComponent} resolves, hiding it and settling the pending {@link confirm} promise. */
  onConfirmDialogResolved(confirmed: boolean): void {
    this.confirmDialogVisible.set(false);
    this.confirmResolve?.(confirmed);
    this.confirmResolve = null;
  }

  private async loadGroupName(): Promise<void> {
    try {
      const group = await firstValueFrom(
        this.clientGroupsApi.apiClientGroupsIdGet({ id: this.groupId }),
      );
      this.groupName.set(group.name ?? null);
    } catch {
      // A missing group name is only cosmetic here -- the schedule list load below surfaces its
      // own real error if the owning group itself doesn't exist.
    }
  }

  private async loadPage(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const page = await firstValueFrom(
        this.schedulesApi.apiClientGroupsGroupIdSchedulesGet({
          groupId: this.groupId,
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
