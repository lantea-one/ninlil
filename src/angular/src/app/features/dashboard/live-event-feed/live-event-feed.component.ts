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
 * This component renders the Dashboard's live event feed from {@link DnsEventsHubService} and the
 * connection state. The template says the feed is live only and resets on reload, since no
 * endpoint returns past events and an empty feed after a reload would otherwise look like a bug.
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

  /** The hub connection state, shown in the header badge. */
  readonly connectionState = input<DnsEventsHubConnectionState>('disconnected');

  /** The CoreUI color of the connection-state badge. */
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
