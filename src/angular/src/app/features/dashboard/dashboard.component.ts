import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { SpinnerComponent } from '@coreui/angular';
import { DnsEventsHubService } from '../../core/realtime/dns-events-hub.service';
import { DashboardStatsService } from './dashboard-stats.service';
import { StatusResponse } from '../../api-client';
import { GettingStartedItem, QuickStat } from './dashboard.model';
import { GettingStartedChecklistComponent } from './getting-started-checklist/getting-started-checklist.component';
import { GettingStartedChecklistService } from './getting-started-checklist/getting-started-checklist.service';
import { HealthTileGridComponent } from './health-tile-grid/health-tile-grid.component';
import { LiveEventFeedComponent } from './live-event-feed/live-event-feed.component';
import { QuickStatsComponent } from './quick-stats/quick-stats.component';
import { SelfUpdateNoticeComponent } from './self-update-notice/self-update-notice.component';

/**
 * This component is the Dashboard (`/dashboard`), where the app lands after sign-in. It needs no
 * permission; each piece shows what the user may read. It combines:
 * {@link SelfUpdateNoticeComponent} (a newer release is available),
 * {@link GettingStartedChecklistComponent} (onboarding steps from
 * {@link GettingStartedChecklistService}), {@link HealthTileGridComponent} (`GET /status`),
 * {@link QuickStatsComponent} (four counts from {@link DashboardStatsService}) and
 * {@link LiveEventFeedComponent} (the SignalR feed from {@link DnsEventsHubService}).
 *
 * <p>The page starts the SignalR connection in `ngOnInit` and stops it in `ngOnDestroy`, so the
 * connection does not stay open after the user leaves the dashboard.
 */
@Component({
  selector: 'app-dashboard',
  imports: [
    SpinnerComponent,
    HealthTileGridComponent,
    QuickStatsComponent,
    LiveEventFeedComponent,
    GettingStartedChecklistComponent,
    SelfUpdateNoticeComponent,
  ],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent implements OnInit, OnDestroy {
  private readonly statsService = inject(DashboardStatsService);
  private readonly eventsHub = inject(DnsEventsHubService);
  private readonly checklistService = inject(GettingStartedChecklistService);

  /** The `/status` response, or `null` while still loading. */
  readonly status = signal<StatusResponse | null>(null);

  /** The four quick-stat tiles. */
  readonly quickStats = signal<QuickStat[]>([]);

  /** The "Getting Started" rows the user may see, in display order. */
  readonly checklistItems = signal<GettingStartedItem[]>([]);

  /** Whether the initial `/status` + quick-stat load is still in flight. */
  readonly loading = signal(true);

  /** A message when the initial load failed, or `null`. */
  readonly loadError = signal<string | null>(null);

  /** The live feed entries, from {@link DnsEventsHubService}. */
  readonly feed = this.eventsHub.feed;

  /** The live feed's connection state, from {@link DnsEventsHubService}. */
  readonly connectionState = this.eventsHub.connectionState;

  async ngOnInit(): Promise<void> {
    await this.loadDashboardData();

    // Start the feed even if the load above failed: it does not depend on /status.
    void this.eventsHub.start();
  }

  async ngOnDestroy(): Promise<void> {
    await this.eventsHub.stop();
  }

  private async loadDashboardData(): Promise<void> {
    this.loading.set(true);
    this.loadError.set(null);

    try {
      // The checklist and quick stats never reject (each check catches its errors), so only
      // a /status failure reaches the catch below; a failed checklist check only shows its row
      // as incomplete.
      const [status, quickStats, checklistItems] = await Promise.all([
        this.statsService.fetchStatus(),
        this.statsService.fetchQuickStats(),
        this.checklistService.fetchItems(),
      ]);
      this.status.set(status);
      this.quickStats.set(quickStats);
      this.checklistItems.set(checklistItems);
    } catch {
      this.loadError.set('Could not load dashboard data. Try reloading the page.');
    } finally {
      this.loading.set(false);
    }
  }
}
