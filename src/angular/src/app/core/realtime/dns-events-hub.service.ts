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

/** The hub's own route, mapped by `Program.cs` via `app.MapHub<DnsEventsHub>("/hubs/events")`. */
const HUB_URL = '/hubs/events';

/**
 * This constant caps how many entries {@link DnsEventsHubService.feed} ever retains -- a live
 * feed with no cap would grow unbounded for a long-lived dashboard tab, exactly the kind of
 * "set up once and left running for years" homelab deployment this whole project targets
 * targets. 50 is enough to scroll through a recent burst of
 * activity without ever needing more, since this feed is explicitly not a persisted history.
 */
const MAX_FEED_ENTRIES = 50;

/**
 * This type describes the function {@link DNS_EVENTS_HUB_CONNECTION_FACTORY} resolves to: given
 * an access-token factory (re-read fresh on every SignalR reconnect attempt, so a token refreshed
 * by {@link AuthService} mid-session is picked up automatically), build a real, not-yet-started
 * `HubConnection`.
 */
export type DnsEventsHubConnectionFactory = (accessTokenFactory: () => string) => HubConnection;

/**
 * This injection token exists purely so {@link DnsEventsHubService}'s own unit tests can swap in
 * a fake `HubConnection` (one exposing only the handful of members this service actually calls)
 * without ever opening a real SignalR/WebSocket connection during a `ng test` run -- the same
 * "inject the thing that talks to the outside world, don't hand-mock a whole vendor SDK" shape
 * this workspace's own `AuthApiService`-fake tests already use one layer up.
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
 * This service owns the dashboard's own live SignalR connection to `DnsEventsHub`
 * (`/hubs/events`), bridging its 4 curated broadcast events (`DnsQueryReceived`/
 * `DnsQueryAnswered`/`DnsZoneChanged`/`DnsCacheEvicted` -- see `DnsEventsHubBroadcaster` on the
 * backend) into a small, capped, human-readable activity feed plus a connection-state signal the
 * dashboard's own live-feed header renders.
 * <para>
 *   **Deliberately, inherently session-only** -- no backend endpoint returns historical events,
 *   only live ones from the moment this service's own `start()` succeeds onward;
 *   {@link feed} always starts empty on every fresh connection
 *   and is never persisted across a page reload. The dashboard's own template labels this
 *   explicitly rather than letting it read like a persisted history it is not.
 * </para>
 * <para>
 *   Since a browser cannot attach an `Authorization` header to a WebSocket handshake, the access
 *   token is instead supplied via `HubConnectionOptions.accessTokenFactory` -- the client-side
 *   half of the same query-string-based JWT pattern `Program.cs`'s own `OnMessageReceived`
 *   override implements on the backend (see that file's own comment for the exact reasoning).
 * </para>
 */
@Injectable({ providedIn: 'root' })
export class DnsEventsHubService {
  private readonly authService = inject(AuthService);
  private readonly connectionFactory = inject(DNS_EVENTS_HUB_CONNECTION_FACTORY);

  /**
   * This field holds the currently-active connection, or `null` when never started (or already
   * stopped) -- checked by {@link start} so a second call while already connected/connecting is a
   * harmless no-op rather than opening a duplicate connection.
   */
  private connection: HubConnection | null = null;

  /** This field assigns each new {@link DnsFeedEntry} its own locally-unique, incrementing id. */
  private nextEntryId = 0;

  private readonly connectionStateSignal = signal<DnsEventsHubConnectionState>('disconnected');
  private readonly feedSignal = signal<DnsFeedEntry[]>([]);

  /** The connection's own current lifecycle state, rendered by the dashboard's feed header. */
  readonly connectionState = this.connectionStateSignal.asReadonly();

  /** The live activity feed itself, most-recent-first, capped at {@link MAX_FEED_ENTRIES}. */
  readonly feed = this.feedSignal.asReadonly();

  /**
   * This method opens the hub connection (a no-op if one is already open or in progress) and
   * wires every curated event to {@link feed}. A connection failure is caught and reflected as
   * `'disconnected'` rather than thrown -- the dashboard's own REST-backed tiles (`/status`, the
   * quick-stat counts) must still render even when the live feed itself cannot connect, since the
   * live feed is additive, never a hard dependency of the rest of the page.
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
      // A failed initial connection is not retried here -- withAutomaticReconnect() only covers
      // a connection that *was* established and later dropped, not the very first attempt.
      // Reflecting 'disconnected' lets the dashboard's own template show a clear, non-crashing
      // "not connected" state instead of an unhandled rejection.
      this.connection = null;
      this.connectionStateSignal.set('disconnected');
    }
  }

  /**
   * This method closes the hub connection, if one is open, and resets the connection-state
   * signal -- called from the dashboard component's own `ngOnDestroy` so navigating away stops
   * an open SignalR connection rather than leaking it for the rest of the session.
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

/** This function renders a query's own name/type pair, or a placeholder for a question-less message. */
function describeQuestion(queriedName: string | null, queriedType: string | null): string {
  if (!queriedName) return '(no question)';
  return queriedType ? `${queriedName} ${queriedType}` : queriedName;
}

/** This function pluralizes a count for a feed summary, e.g. `pluralize(1, 'record')` -> `"1 record"`. */
function pluralize(count: number, singular: string, plural: string = `${singular}s`): string {
  return `${count} ${count === 1 ? singular : plural}`;
}
