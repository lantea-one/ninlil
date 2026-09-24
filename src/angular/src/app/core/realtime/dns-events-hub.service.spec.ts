import { TestBed } from '@angular/core/testing';
import { AuthService } from '../auth/auth.service';
import { DNS_EVENTS_HUB_CONNECTION_FACTORY, DnsEventsHubService } from './dns-events-hub.service';

/**
 * This type describes this spec's own hand-rolled fake `HubConnection` -- only the handful of
 * members {@link DnsEventsHubService} actually calls (`on`/`start`/`stop`/`onreconnecting`/
 * `onreconnected`/`onclose`), provided directly through {@link DNS_EVENTS_HUB_CONNECTION_FACTORY}
 * (a plain `useValue` provider, which accepts anything, needing no cast to the real, much larger
 * `HubConnection` type) rather than fully implementing its real surface, since this spec never
 * needs anything beyond these callbacks.
 */
interface FakeHubConnection {
  on: jasmine.Spy;
  start: jasmine.Spy;
  stop: jasmine.Spy;
  onreconnecting: jasmine.Spy;
  onreconnected: jasmine.Spy;
  onclose: jasmine.Spy;
  /** This field captures every handler registered via `on(eventName, handler)`, by event name. */
  handlers: Map<string, (payload: unknown) => void>;
}

function createFakeConnection(): FakeHubConnection {
  const handlers = new Map<string, (payload: unknown) => void>();
  return {
    on: jasmine
      .createSpy('on')
      .and.callFake((eventName: string, handler: (payload: unknown) => void) => {
        handlers.set(eventName, handler);
      }),
    start: jasmine.createSpy('start').and.resolveTo(undefined),
    stop: jasmine.createSpy('stop').and.resolveTo(undefined),
    onreconnecting: jasmine.createSpy('onreconnecting'),
    onreconnected: jasmine.createSpy('onreconnected'),
    onclose: jasmine.createSpy('onclose'),
    handlers,
  };
}

describe('DnsEventsHubService', () => {
  let service: DnsEventsHubService;
  let fakeConnection: FakeHubConnection;
  let connectionFactorySpy: jasmine.Spy;

  beforeEach(() => {
    fakeConnection = createFakeConnection();
    connectionFactorySpy = jasmine.createSpy('connectionFactory').and.returnValue(fakeConnection);

    TestBed.configureTestingModule({
      providers: [
        DnsEventsHubService,
        { provide: AuthService, useValue: { accessToken: 'a-token' } },
        { provide: DNS_EVENTS_HUB_CONNECTION_FACTORY, useValue: connectionFactorySpy },
      ],
    });

    service = TestBed.inject(DnsEventsHubService);
  });

  it('starts disconnected, with an empty feed', () => {
    expect(service.connectionState()).toBe('disconnected');
    expect(service.feed()).toEqual([]);
  });

  it('start() builds and starts a connection, ending in the connected state', async () => {
    await service.start();

    expect(connectionFactorySpy).toHaveBeenCalled();
    expect(fakeConnection.start).toHaveBeenCalled();
    expect(service.connectionState()).toBe('connected');
  });

  it('start() is a no-op when already connected, never building a second connection', async () => {
    await service.start();
    await service.start();

    expect(connectionFactorySpy).toHaveBeenCalledTimes(1);
  });

  it('start() reflects the disconnected state, without throwing, when the connection attempt fails', async () => {
    fakeConnection.start.and.rejectWith(new Error('connection refused'));

    await service.start();

    expect(service.connectionState()).toBe('disconnected');
  });

  it('reflects onreconnecting/onreconnected/onclose into connectionState', async () => {
    await service.start();

    fakeConnection.onreconnecting.calls.mostRecent().args[0]();
    expect(service.connectionState()).toBe('reconnecting');

    fakeConnection.onreconnected.calls.mostRecent().args[0]();
    expect(service.connectionState()).toBe('connected');

    fakeConnection.onclose.calls.mostRecent().args[0]();
    expect(service.connectionState()).toBe('disconnected');
  });

  it('adds a readable summary entry to the feed for a DnsZoneChanged broadcast', async () => {
    await service.start();

    fakeConnection.handlers.get('DnsZoneChanged')?.({
      occurredAtUtc: '2026-08-30T12:00:00Z',
      zoneId: '11111111-1111-1111-1111-111111111111',
      zoneName: 'example.test',
    });

    expect(service.feed().length).toBe(1);
    expect(service.feed()[0].eventType).toBe('DnsZoneChanged');
    expect(service.feed()[0].summary).toContain('example.test');
  });

  it('adds a readable summary entry for DnsCacheEvicted, pluralizing the entry count correctly', async () => {
    await service.start();

    fakeConnection.handlers.get('DnsCacheEvicted')?.({
      occurredAtUtc: '2026-08-30T12:00:00Z',
      reason: 'ZoneReloaded',
      entryCount: 1,
    });

    expect(service.feed()[0].summary).toBe('Cache evicted (ZoneReloaded): 1 entry');
  });

  it('adds a readable summary entry for DnsQueryReceived, describing a question-less query', async () => {
    await service.start();

    fakeConnection.handlers.get('DnsQueryReceived')?.({
      occurredAtUtc: '2026-08-30T12:00:00Z',
      protocol: 'Udp',
      remoteAddress: '203.0.113.5',
      queriedName: null,
      queriedType: null,
    });

    expect(service.feed()[0].summary).toContain('(no question)');
    expect(service.feed()[0].summary).toContain('203.0.113.5');
  });

  it('adds a readable summary entry for DnsQueryAnswered', async () => {
    await service.start();

    fakeConnection.handlers.get('DnsQueryAnswered')?.({
      occurredAtUtc: '2026-08-30T12:00:00Z',
      protocol: 'Udp',
      remoteAddress: '203.0.113.5',
      queriedName: 'example.test',
      queriedType: 'A',
      responseCode: 'NOERROR',
      answerCount: 2,
    });

    expect(service.feed()[0].summary).toBe('Answered example.test A: NOERROR (2 records)');
  });

  it('caps the feed at 50 entries, dropping the oldest', async () => {
    await service.start();
    const handler = fakeConnection.handlers.get('DnsZoneChanged');

    for (let i = 0; i < 55; i++) {
      handler?.({ occurredAtUtc: '2026-08-30T12:00:00Z', zoneId: 'id', zoneName: `zone-${i}` });
    }

    expect(service.feed().length).toBe(50);
    // Most recent first: the very last published event (zone-54) is at the front.
    expect(service.feed()[0].summary).toContain('zone-54');
  });

  it('stop() stops the underlying connection and resets to disconnected', async () => {
    await service.start();

    await service.stop();

    expect(fakeConnection.stop).toHaveBeenCalled();
    expect(service.connectionState()).toBe('disconnected');
  });

  it('stop() is a harmless no-op when never started', async () => {
    await service.stop();

    expect(service.connectionState()).toBe('disconnected');
  });

  it('the access-token factory passed to the connection reads the live AuthService token', async () => {
    await service.start();

    const accessTokenFactory = connectionFactorySpy.calls.mostRecent().args[0] as () => string;
    expect(accessTokenFactory()).toBe('a-token');
  });
});
