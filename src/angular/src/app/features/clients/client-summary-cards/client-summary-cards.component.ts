import { Component, computed, input, output } from '@angular/core';
import { IconDirective } from '@coreui/icons-angular';
import { ClientSummaryDetail } from '../../../api-client';
import { ClientListFilter } from '../shared/client-list-filter.model';

/** One rendered summary card: which filter it selects, its label, count, icon, and accent color. */
interface SummaryCard {
  readonly filter: ClientListFilter;
  readonly label: string;
  readonly count: number | null;
  readonly icon: string;
  readonly accent: 'primary' | 'success' | 'secondary' | 'info' | 'warning';
}

/**
 * This component shows the client counts (total, online, offline, new today, favorites) as a row
 * of cards above the client list on the Clients page. Each card is also a filter: selecting one
 * emits its {@link ClientListFilter}, and the card of the current filter is shown as pressed. A
 * count that has not loaded, or failed to, shows an em dash instead of a misleading `0`, as the
 * dashboard's quick stats do.
 */
@Component({
  selector: 'app-client-summary-cards',
  imports: [IconDirective],
  templateUrl: './client-summary-cards.component.html',
  styleUrl: './client-summary-cards.component.scss',
})
export class ClientSummaryCardsComponent {
  /** The counts to show, or `null` while they are loading or unavailable. */
  readonly summary = input<ClientSummaryDetail | null>(null);

  /** The list's current filter, whose card is shown as pressed. */
  readonly activeFilter = input<ClientListFilter>('all');

  /** Emits the filter of whichever card the operator selects. */
  readonly filterSelected = output<ClientListFilter>();

  /** The cards, in display order, with their current counts. */
  readonly cards = computed<SummaryCard[]>(() => {
    const summary = this.summary();
    return [
      {
        filter: 'all',
        label: 'All devices',
        count: summary?.totalCount ?? null,
        icon: 'cilDevices',
        accent: 'primary',
      },
      {
        filter: 'online',
        label: 'Online',
        count: summary?.onlineCount ?? null,
        icon: 'cilWifiSignal4',
        accent: 'success',
      },
      {
        filter: 'offline',
        label: 'Offline',
        count: summary?.offlineCount ?? null,
        icon: 'cilBan',
        accent: 'secondary',
      },
      {
        filter: 'new',
        label: 'New today',
        count: summary?.newCount ?? null,
        icon: 'cilClock',
        accent: 'info',
      },
      {
        filter: 'favorites',
        label: 'Favorites',
        count: summary?.favoriteCount ?? null,
        icon: 'cilStar',
        accent: 'warning',
      },
    ];
  });
}
