import { DatePipe, NgClass } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import {
  BadgeComponent,
  CardBodyComponent,
  CardComponent,
  CardHeaderComponent,
  ListGroupDirective,
  ListGroupItemDirective,
} from '@coreui/angular';
import { DnsEventsHubConnectionState, DnsFeedEntry } from '../../../core/realtime/dns-event.model';

/**
 * This component renders {@link DnsEventsHubService}'s own live feed of curated DNS server
 * events, plus the connection's own current lifecycle state. **Deliberately, visibly labeled as
 * session-only** -- no backend endpoint returns historical events, only live ones from the
 * moment the hub connection succeeds; the template says so explicitly rather than letting an
 * empty-on-reload feed read like a bug.
 */
@Component({
  selector: 'app-live-event-feed',
  imports: [
    NgClass,
    DatePipe,
    CardComponent,
    CardHeaderComponent,
    CardBodyComponent,
    BadgeComponent,
    ListGroupDirective,
    ListGroupItemDirective,
  ],
  templateUrl: './live-event-feed.component.html',
  styleUrl: './live-event-feed.component.scss',
})
export class LiveEventFeedComponent {
  /** The feed entries to render, most-recent-first. */
  readonly feed = input<DnsFeedEntry[]>([]);

  /** The hub connection's own current lifecycle state, shown in this section's own header badge. */
  readonly connectionState = input<DnsEventsHubConnectionState>('disconnected');

  /** The CoreUI themed color the connection-state badge is shown in. */
  readonly connectionBadgeColor = computed(() => {
    switch (this.connectionState()) {
      case 'connected':
        return 'success';
      case 'connecting':
        return 'info';
      case 'reconnecting':
        return 'warning';
      default:
        return 'secondary';
    }
  });
}
