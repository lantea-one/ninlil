/**
 * This union describes {@link DnsEventsHubService}'s own tracked SignalR connection lifecycle
 * state, driven by the real `HubConnection` callbacks (`onreconnecting`/`onreconnected`/
 * `onclose`) rather than guessed from the connection's own `state` property alone, so a
 * transient reconnect attempt is visibly distinct from a fully dropped connection in the
 * dashboard's own live-feed header.
 */
export type DnsEventsHubConnectionState =
  'disconnected' | 'connecting' | 'connected' | 'reconnecting';

/**
 * This union lists the exact 4 event names `Ninlil.Web.Hubs.DnsEventsHubBroadcaster`
 * forwards over `/hubs/events` -- one literal per its own `SendAsync` method-name argument, not
 * guessed from the underlying `IDnsServerEvent` record type names (which differ: e.g.
 * `DnsQueryReceivedEvent` the C# type vs. `"DnsQueryReceived"` the SignalR method name).
 */
export type DnsFeedEventType =
  'DnsQueryReceived' | 'DnsQueryAnswered' | 'DnsZoneChanged' | 'DnsCacheEvicted';

/**
 * This interface describes one entry in the dashboard's own live, session-only activity feed --
 * a human-readable summary derived from whichever real event payload produced it, not the raw
 * payload itself, since a live feed is read at a glance, not inspected field-by-field.
 */
export interface DnsFeedEntry {
  /** A locally-assigned, monotonically increasing identifier, used only for `@for` tracking. */
  id: number;

  /** Which of the 4 curated event types produced this entry. */
  eventType: DnsFeedEventType;

  /** The event's own `occurredAtUtc`, in its original ISO-8601 wire form. */
  occurredAtUtc: string;

  /** A one-line, human-readable summary of what happened. */
  summary: string;
}

/**
 * This interface mirrors `Ninlil.Dns.Server.Core.Events.DnsQueryReceivedEvent`'s own
 * camelCase JSON shape, exactly as `JsonHubProtocol`'s default camelCase naming policy serializes
 * it over the wire -- deliberately hand-written here rather than generated, since this event
 * never appears in the REST OpenAPI surface the generated `api-client/` is built from (it only
 * ever travels over the SignalR hub).
 */
export interface DnsQueryReceivedEventPayload {
  occurredAtUtc: string;
  protocol: string;
  remoteAddress: string;
  queriedName: string | null;
  queriedType: string | null;
}

/**
 * This interface mirrors `Ninlil.Dns.Server.Core.Events.DnsQueryAnsweredEvent`'s own
 * camelCase JSON shape -- see {@link DnsQueryReceivedEventPayload}'s own doc comment for why this
 * is hand-written rather than generated.
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
 * This interface mirrors `Ninlil.Dns.Server.Core.Events.DnsZoneChangedEvent`'s own camelCase
 * JSON shape -- see {@link DnsQueryReceivedEventPayload}'s own doc comment for why this is
 * hand-written rather than generated.
 */
export interface DnsZoneChangedEventPayload {
  occurredAtUtc: string;
  zoneId: string;
  zoneName: string;
}

/**
 * This interface mirrors `Ninlil.Dns.Server.Core.Events.DnsCacheEvictedEvent`'s own camelCase
 * JSON shape -- see {@link DnsQueryReceivedEventPayload}'s own doc comment for why this is
 * hand-written rather than generated.
 */
export interface DnsCacheEvictedEventPayload {
  occurredAtUtc: string;
  reason: string;
  entryCount: number;
}
