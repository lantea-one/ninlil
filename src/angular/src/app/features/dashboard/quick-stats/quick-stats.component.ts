import { Component, input } from '@angular/core';
import { CardBodyComponent, CardComponent } from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { QuickStat } from '../dashboard.model';

/**
 * This component renders this app's own "quick stat" tiles -- a small row of icon-led count
 * cards (zones, forwarders, filter lists, webhooks) computed from a minimal `take=1` list request
 * each. A `null` count (a resource the signed-in caller lacks permission to list) renders as an
 * explicit em dash rather than a misleading `0`.
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
