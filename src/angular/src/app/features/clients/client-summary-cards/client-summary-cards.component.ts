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
 * This component renders the known-client registry's headline counts -- total, online, offline,
 * new today, and favorites -- as a row of cards above the client list. Each card is also the
 * list's quick filter: selecting one emits its {@link ClientListFilter}, and the card matching the
 * list's current filter is shown as pressed. A card whose count has not loaded (or failed to)
 * shows an em dash rather than a misleading `0`, the same convention the dashboard's own quick
 * stats use.
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
