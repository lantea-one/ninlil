import { Component, input } from '@angular/core';
import { CardBodyComponent, CardComponent } from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { QuickStat } from '../dashboard.model';

/**
 * This component renders the Dashboard's quick-stat tiles: counts of zones, forwarders, filter
 * lists and webhooks, from {@link DashboardStatsService}. A `null` count (usually a resource the
 * user may not list) shows an em dash instead of a misleading `0`.
 */
@Component({
  selector: 'app-quick-stats',
  imports: [CardComponent, CardBodyComponent, IconDirective],
  templateUrl: './quick-stats.component.html',
  styleUrl: './quick-stats.component.scss',
})
export class QuickStatsComponent {
  /** The stat tiles to render, in display order. */
  readonly stats = input<QuickStat[]>([]);
}
