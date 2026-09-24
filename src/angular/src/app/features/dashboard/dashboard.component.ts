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
 * This component is the app's own landing page (see `app.routes.ts`'s own `redirectTo:
 * 'dashboard'`), composing five independent pieces: {@link HealthTileGridComponent} (`GET
 * /status`, additive by permission), {@link QuickStatsComponent} (4 derived `take=1` counts),
 * {@link LiveEventFeedComponent} (the SignalR live event feed via {@link DnsEventsHubService}),
 * {@link GettingStartedChecklistComponent} (a dismissible onboarding checklist backed by
 * {@link GettingStartedChecklistService}), and {@link SelfUpdateNoticeComponent} (a real,
 * visible notice when a newer release of this server is known to exist).
 * <para>
 *   The live feed's own connection is started here, in `ngOnInit`, and stopped in `ngOnDestroy`
 *   -- unlike {@link AuthService}, which is a genuinely app-wide singleton with no natural
 *   "stop" point, a SignalR connection is real, per-page-visit network state that should not
 *   keep running once this page is navigated away from.
 * </para>
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

  /** The 4 derived quick-stat tiles. */
  readonly quickStats = signal<QuickStat[]>([]);

  /** The "Getting Started" checklist's own permission-visible rows, in fixed display order. */
  readonly checklistItems = signal<GettingStartedItem[]>([]);

  /** Whether the initial `/status` + quick-stat load is still in flight. */
  readonly loading = signal(true);

  /** A user-facing message if the initial load itself failed outright, or `null`. */
  readonly loadError = signal<string | null>(null);

  /** The live feed's own entries, re-exposed directly from {@link DnsEventsHubService}. */
  readonly feed = this.eventsHub.feed;

  /** The live feed's own connection state, re-exposed directly from {@link DnsEventsHubService}. */
  readonly connectionState = this.eventsHub.connectionState;

  async ngOnInit(): Promise<void> {
    await this.loadDashboardData();

    // The live feed's own connection is independent of the REST load above succeeding -- a
    // caller who cannot reach /status for some reason should still see live activity if the hub
    // itself is reachable, since the live feed is additive, not a hard dependency of the rest of
    // the page.
    void this.eventsHub.start();
  }

  async ngOnDestroy(): Promise<void> {
    await this.eventsHub.stop();
  }

  private async loadDashboardData(): Promise<void> {
    this.loading.set(true);
    this.loadError.set(null);

    try {
      // The checklist's own fetch never rejects (each row's own condition check is individually
      // try/caught inside GettingStartedChecklistService), so folding it into this same
      // Promise.all can't turn a checklist-only hiccup into the generic "dashboard failed to
      // load" error path below -- at worst, one row renders incomplete.
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
