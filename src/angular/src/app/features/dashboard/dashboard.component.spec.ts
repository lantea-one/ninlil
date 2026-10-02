import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { SelfUpdateService } from '../../api-client';
import { AuthService } from '../../core/auth/auth.service';
import { DnsEventsHubService } from '../../core/realtime/dns-events-hub.service';
import { DashboardComponent } from './dashboard.component';
import { DashboardStatsService } from './dashboard-stats.service';
import { GettingStartedChecklistService } from './getting-started-checklist/getting-started-checklist.service';

/**
 * This function waits until every pending microtask has run, including the `await`s in
 * `DashboardComponent.ngOnInit`. The app is zoneless, so `fixture.whenStable()` does not see
 * promises a fake returns and can resolve before `ngOnInit()` finishes. A `setTimeout` runs only
 * after the microtask queue is empty.
 */
function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the Dashboard: the spinner, loading status, quick stats and checklist, the
 * error state, and starting and stopping the live feed. TestBed with fake stats, checklist,
 * events hub, self-update and auth services.
 */
describe('DashboardComponent', () => {
  let statsServiceFake: { fetchStatus: jasmine.Spy; fetchQuickStats: jasmine.Spy };
  let checklistServiceFake: { fetchItems: jasmine.Spy };
  let eventsHubFake: {
    start: jasmine.Spy;
    stop: jasmine.Spy;
    feed: ReturnType<typeof signal>;
    connectionState: ReturnType<typeof signal>;
  };

  async function createComponent() {
    statsServiceFake = {
      fetchStatus: jasmine
        .createSpy('fetchStatus')
        .and.resolveTo({ status: 'Healthy', components: null }),
      fetchQuickStats: jasmine.createSpy('fetchQuickStats').and.resolveTo([]),
    };
    checklistServiceFake = {
      fetchItems: jasmine.createSpy('fetchItems').and.resolveTo([]),
    };
    eventsHubFake = {
      start: jasmine.createSpy('start').and.resolveTo(undefined),
      stop: jasmine.createSpy('stop').and.resolveTo(undefined),
      feed: signal([]),
      connectionState: signal('disconnected'),
    };

    const selfUpdateApiFake = jasmine.createSpyObj('SelfUpdateService', ['apiSelfUpdateGet']);
    selfUpdateApiFake.apiSelfUpdateGet.and.returnValue(
      of({ configured: false, currentVersion: '1.0.0.0', updateAvailable: false }),
    );
    // hasPermission() returns false, so SelfUpdateNoticeComponent in the template never fetches;
    // its own spec covers it.
    const authFake = jasmine.createSpyObj('AuthService', ['hasPermission']);
    authFake.hasPermission.and.returnValue(false);

    await TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [
        provideRouter([]),
        { provide: DashboardStatsService, useValue: statsServiceFake },
        { provide: GettingStartedChecklistService, useValue: checklistServiceFake },
        { provide: DnsEventsHubService, useValue: eventsHubFake },
        { provide: SelfUpdateService, useValue: selfUpdateApiFake },
        { provide: AuthService, useValue: authFake },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(DashboardComponent);
    return fixture;
  }

  it('shows a loading spinner before the initial fetch resolves', async () => {
    const fixture = await createComponent();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('c-spinner')).not.toBeNull();
  });

  it('loads status and quick stats on init, then renders the health grid and quick stats', async () => {
    const fixture = await createComponent();
    statsServiceFake.fetchStatus.and.resolveTo({ status: 'Healthy', components: [] });
    statsServiceFake.fetchQuickStats.and.resolveTo([{ label: 'Zones', icon: 'dns', count: 5 }]);

    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();

    expect(fixture.componentInstance.status()).toEqual({ status: 'Healthy', components: [] });
    expect(fixture.componentInstance.quickStats()).toEqual([
      { label: 'Zones', icon: 'dns', count: 5 },
    ]);
    expect(fixture.componentInstance.loading()).toBeFalse();
    expect(fixture.nativeElement.querySelector('app-health-tile-grid')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('app-quick-stats')).not.toBeNull();
  });

  it('loads the "Getting Started" checklist items on init, alongside status and quick stats', async () => {
    const fixture = await createComponent();
    checklistServiceFake.fetchItems.and.resolveTo([
      { id: 'add-zone', label: 'Add your first zone', complete: false, routerLink: '/zones' },
    ]);

    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();

    expect(fixture.componentInstance.checklistItems()).toEqual([
      { id: 'add-zone', label: 'Add your first zone', complete: false, routerLink: '/zones' },
    ]);
    expect(fixture.nativeElement.querySelector('app-getting-started-checklist')).not.toBeNull();
  });

  it('starts the live events hub connection once the initial load settles', async () => {
    const fixture = await createComponent();
    fixture.detectChanges();
    await flushMicrotasks();

    expect(eventsHubFake.start).toHaveBeenCalled();
  });

  it('shows an error message, and never the health grid, when the initial load itself throws', async () => {
    const fixture = await createComponent();
    statsServiceFake.fetchStatus.and.rejectWith(new Error('network error'));

    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();

    expect(fixture.componentInstance.loadError()).not.toBeNull();
    expect(fixture.nativeElement.querySelector('app-health-tile-grid')).toBeNull();
    // The live feed still shows; it does not depend on the load above.
    expect(fixture.nativeElement.querySelector('app-live-event-feed')).not.toBeNull();
  });

  it('stops the live events hub connection when the component is destroyed', async () => {
    const fixture = await createComponent();
    fixture.detectChanges();
    await flushMicrotasks();

    fixture.destroy();

    expect(eventsHubFake.stop).toHaveBeenCalled();
  });
});
