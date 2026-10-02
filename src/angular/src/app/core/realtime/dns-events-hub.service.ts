import { InjectionToken, Injectable, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { AuthService } from '../auth/auth.service';
import {
  DnsCacheEvictedEventPayload,
  DnsEventsHubConnectionState,
  DnsFeedEntry,
  DnsQueryAnsweredEventPayload,
  DnsQueryReceivedEventPayload,
  DnsZoneChangedEventPayload,
} from './dns-event.model';

/** The hub's route, mapped in `Program.cs` by `app.MapHub<DnsEventsHub>("/hubs/events")`. */
const HUB_URL = '/hubs/events';

/**
 * This constant caps how many entries {@link DnsEventsHubService.feed} keeps, so a dashboard tab
 * left open for weeks does not grow without bound. The feed is not a history; 50 covers a recent
 * burst of activity.
 */
const MAX_FEED_ENTRIES = 50;

/**
 * This type is the function {@link DNS_EVENTS_HUB_CONNECTION_FACTORY} provides: it builds an
 * unstarted `HubConnection` from an access-token factory. SignalR calls the factory on every
 * connect and reconnect, so a token that {@link AuthService} refreshed is picked up.
 */
export type DnsEventsHubConnectionFactory = (accessTokenFactory: () => string) => HubConnection;

/**
 * This injection token builds the hub connection. It exists so the {@link DnsEventsHubService}
 * tests can supply a fake `HubConnection` with only the members the service calls, and never
 * open a SignalR connection.
 */
export const DNS_EVENTS_HUB_CONNECTION_FACTORY = new InjectionToken<DnsEventsHubConnectionFactory>(
  'DNS_EVENTS_HUB_CONNECTION_FACTORY',
  {
    providedIn: 'root',
    factory: () => (accessTokenFactory: () => string) =>
      new HubConnectionBuilder()
        .withUrl(HUB_URL, { accessTokenFactory })
        .withAutomaticReconnect()
        .build(),
  },
);

/**
 * This service holds the dashboard's SignalR connection to `DnsEventsHub` (`/hubs/events`). It
 * turns the four events `DnsEventsHubBroadcaster` sends (`DnsQueryReceived`, `DnsQueryAnswered`,
 * `DnsZoneChanged`, `DnsCacheEvicted`) into a short, capped activity feed, and exposes the
 * connection state for the feed header. The backend only sends each event to users holding its
 * permission (`querylog:read`, `zones:read` or `recursivesettings:read`).
 *
 * <p>The feed is live only. No endpoint returns past events, so the feed holds what arrived since
 * `start()` and is lost on a page reload; the dashboard template says so. Because the service is a
 * root singleton, entries survive leaving and returning to the dashboard within one page load.
 *
 * <p>A browser cannot set an `Authorization` header on a WebSocket handshake, so the token goes
 * through `accessTokenFactory`, which SignalR sends as the `access_token` query parameter. The
 * `OnMessageReceived` handler in `Program.cs` accepts it for `/hubs` paths.
 */
@Injectable({ providedIn: 'root' })
export class DnsEventsHubService {
  private readonly authService = inject(AuthService);
  private readonly connectionFactory = inject(DNS_EVENTS_HUB_CONNECTION_FACTORY);

  /**
   * This field holds the active connection, or `null` when not started or stopped. {@link start}
   * checks it so a second call does not open a duplicate connection.
   */
  private connection: HubConnection | null = null;

  /** This field holds the next {@link DnsFeedEntry} id. */
  private nextEntryId = 0;

  private readonly connectionStateSignal = signal<DnsEventsHubConnectionState>('disconnected');
  private readonly feedSignal = signal<DnsFeedEntry[]>([]);

  /** The connection state, shown in the dashboard's feed header. */
  readonly connectionState = this.connectionStateSignal.asReadonly();

  /** The activity feed, newest first, capped at {@link MAX_FEED_ENTRIES}. */
  readonly feed = this.feedSignal.asReadonly();

  /**
   * This method opens the hub connection (a no-op if one is open or opening) and feeds every event
   * into {@link feed}. A failure to connect sets `'disconnected'` instead of throwing, so the rest
   * of the dashboard still renders without the live feed.
   */
  async start(): Promise<void> {
    if (this.connection) return;

    const connection = this.connectionFactory(() => this.authService.accessToken ?? '');

    connection.onreconnecting(() => this.connectionStateSignal.set('reconnecting'));
    connection.onreconnected(() => this.connectionStateSignal.set('connected'));
    connection.onclose(() => this.connectionStateSignal.set('disconnected'));

    connection.on('DnsQueryReceived', (payload: DnsQueryReceivedEventPayload) =>
      this.pushEntry(
        'DnsQueryReceived',
        payload.occurredAtUtc,
        `Query received: ${describeQuestion(payload.queriedName, payload.queriedType)} from ${payload.remoteAddress} via ${payload.protocol}`,
      ),
    );

    connection.on('DnsQueryAnswered', (payload: DnsQueryAnsweredEventPayload) =>
      this.pushEntry(
        'DnsQueryAnswered',
        payload.occurredAtUtc,
        `Answered ${describeQuestion(payload.queriedName, payload.queriedType)}: ${payload.responseCode} (${pluralize(payload.answerCount, 'record')})`,
      ),
    );

    connection.on('DnsZoneChanged', (payload: DnsZoneChangedEventPayload) =>
      this.pushEntry('DnsZoneChanged', payload.occurredAtUtc, `Zone changed: ${payload.zoneName}`),
    );

    connection.on('DnsCacheEvicted', (payload: DnsCacheEvictedEventPayload) =>
      this.pushEntry(
        'DnsCacheEvicted',
        payload.occurredAtUtc,
        `Cache evicted (${payload.reason}): ${pluralize(payload.entryCount, 'entry', 'entries')}`,
      ),
    );

    this.connection = connection;
    this.connectionStateSignal.set('connecting');

    try {
      await connection.start();
      this.connectionStateSignal.set('connected');
    } catch {
      // withAutomaticReconnect() only retries a connection that was established and then
      // dropped, not a failed first attempt, so the feed stays 'disconnected' until the next
      // start().
      this.connection = null;
      this.connectionStateSignal.set('disconnected');
    }
  }

  /**
   * This method closes the hub connection, if one is open, and sets the state to
   * `'disconnected'`. The dashboard calls it from `ngOnDestroy` so leaving the page closes the
   * connection. The feed entries are kept.
   */
  async stop(): Promise<void> {
    const connection = this.connection;
    this.connection = null;

    if (connection) await connection.stop();

    this.connectionStateSignal.set('disconnected');
  }

  private pushEntry(
    eventType: DnsFeedEntry['eventType'],
    occurredAtUtc: string,
    summary: string,
  ): void {
    const entry: DnsFeedEntry = { id: this.nextEntryId++, eventType, occurredAtUtc, summary };
    this.feedSignal.update((entries) => [entry, ...entries].slice(0, MAX_FEED_ENTRIES));
  }
}

/** This function renders a query's name and type, or a placeholder for a message with no question. */
function describeQuestion(queriedName: string | null, queriedType: string | null): string {
  if (!queriedName) return '(no question)';
  return queriedType ? `${queriedName} ${queriedType}` : queriedName;
}

/** This function pluralizes a count for a feed summary, e.g. `pluralize(1, 'record')` -> `"1 record"`. */
function pluralize(count: number, singular: string, plural: string = `${singular}s`): string {
  return `${count} ${count === 1 ? singular : plural}`;
}
