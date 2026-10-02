/**
 * This union is the SignalR connection state that {@link DnsEventsHubService} tracks from the
 * `HubConnection` callbacks (`onreconnecting`, `onreconnected`, `onclose`). The dashboard's live
 * feed header shows it, so a reconnect attempt looks different from a dropped connection.
 */
export type DnsEventsHubConnectionState =
  'disconnected' | 'connecting' | 'connected' | 'reconnecting';

/**
 * This union lists the four SignalR method names `DnsEventsHubBroadcaster` sends over
 * `/hubs/events`. They differ from the C# event type names: `DnsQueryReceivedEvent` is sent as
 * `"DnsQueryReceived"`.
 */
export type DnsFeedEventType =
  'DnsQueryReceived' | 'DnsQueryAnswered' | 'DnsZoneChanged' | 'DnsCacheEvicted';

/**
 * This interface is one entry in the dashboard's live activity feed: a one-line summary of the
 * event, not the raw payload, since the feed is read at a glance.
 */
export interface DnsFeedEntry {
  /** A locally assigned, increasing id, used only for `@for` tracking. */
  id: number;

  /** Which of the four event types produced this entry. */
  eventType: DnsFeedEventType;

  /** The event's `occurredAtUtc`, as the ISO-8601 string sent over the wire. */
  occurredAtUtc: string;

  /** A one-line, human-readable summary of what happened. */
  summary: string;
}

/**
 * This interface mirrors the camelCase JSON of the backend's `DnsQueryReceivedEvent` as the hub
 * sends it (enums as strings, IP addresses as text). It is hand-written because hub events are not
 * in the OpenAPI document that `api-client/` is generated from.
 */
export interface DnsQueryReceivedEventPayload {
  occurredAtUtc: string;
  protocol: string;
  remoteAddress: string;
  queriedName: string | null;
  queriedType: string | null;
}

/**
 * This interface mirrors the JSON of the backend's `DnsQueryAnsweredEvent`; see
 * {@link DnsQueryReceivedEventPayload} for why it is hand-written.
 */
export interface DnsQueryAnsweredEventPayload {
  occurredAtUtc: string;
  protocol: string;
  remoteAddress: string;
  queriedName: string | null;
  queriedType: string | null;
  responseCode: string;
  answerCount: number;
}

/**
 * This interface mirrors the JSON of the backend's `DnsZoneChangedEvent`; see
 * {@link DnsQueryReceivedEventPayload} for why it is hand-written.
 */
export interface DnsZoneChangedEventPayload {
  occurredAtUtc: string;
  zoneId: string;
  zoneName: string;
}

/**
 * This interface mirrors the JSON of the backend's `DnsCacheEvictedEvent`; see
 * {@link DnsQueryReceivedEventPayload} for why it is hand-written.
 */
export interface DnsCacheEvictedEventPayload {
  occurredAtUtc: string;
  reason: string;
  entryCount: number;
}
