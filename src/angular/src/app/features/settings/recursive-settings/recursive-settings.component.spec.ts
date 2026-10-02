import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { DnsCacheService, RecursiveSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import {
  RecursiveSettingsComponent,
  toRootHintServers,
  toTrustAnchors,
} from './recursive-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the Recursive Resolver page: loading, saving, when the root hint and trust
 * anchor lists are sent, the cache card and errors, plus the row conversion helpers. TestBed with a
 * spy-backed settings service and `NotificationService`.
 */
describe('RecursiveSettingsComponent', () => {
  let apiSpy: {
    apiRecursiveSettingsGet: jasmine.Spy;
    apiRecursiveSettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let cacheApiSpy: { apiDnsCacheGet: jasmine.Spy };

  async function createComponent(
    configureApi?: () => void,
    overrides: Record<string, unknown> = {},
  ) {
    apiSpy = jasmine.createSpyObj('RecursiveSettingsService', [
      'apiRecursiveSettingsGet',
      'apiRecursiveSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);
    cacheApiSpy = jasmine.createSpyObj('DnsCacheService', ['apiDnsCacheGet']);
    cacheApiSpy.apiDnsCacheGet.and.returnValue(
      of([{ name: 'forwarded', entryCount: 12, maxEntries: 10000, hits: 30, misses: 10 }]),
    );

    apiSpy.apiRecursiveSettingsGet.and.returnValue(
      of({
        enableRecursion: false,
        enableDnssecValidation: true,
        enableQnameMinimization: true,
        upstreamQueryTimeout: '00:00:03',
        overallQueryTimeout: '00:00:15',
        maxReferralDepth: 30,
        maxCnameChainLength: 15,
        maxUpstreamQueries: 100,
        maxCacheEntries: 100000,
        rootHintServersOverride: [],
        trustAnchorsOverride: [],
        enableAutomaticTrustAnchorUpdate: true,
        trustAnchorRefreshInterval: '15.00:00:00',
        trustAnchorHoldDownDuration: '30.00:00:00',
        trustAnchorRemoveHoldDownDuration: '30.00:00:00',
        enableServeStale: true,
        maxStaleServeAge: '1.00:00:00',
        enablePrefetch: true,
        prefetchWindow: '00:00:10',
        prefetchRecentAccessWindow: '00:05:00',
        ...overrides,
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [RecursiveSettingsComponent],
      providers: [
        { provide: RecursiveSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        { provide: DnsCacheService, useValue: cacheApiSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(RecursiveSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('shows the answer cache card below the settings form', async () => {
    const fixture = await createComponent();
    const element: HTMLElement = fixture.nativeElement;

    expect(cacheApiSpy.apiDnsCacheGet).toHaveBeenCalled();
    expect(element.querySelector('app-dns-cache-card')).not.toBeNull();
    expect(element.querySelector('tr[data-cache="forwarded"]')?.textContent).toContain('75%');
  });

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiRecursiveSettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.enableRecursion.value).toBeFalse();
    expect(fixture.componentInstance.form.controls.maxReferralDepth.value).toBe(30);
    expect(fixture.componentInstance.form.controls.enableServeStale.value).toBeTrue();
    expect(fixture.componentInstance.form.controls.prefetchWindow.value).toBe('00:00:10');
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the full form, then surfaces the hot-reload note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiRecursiveSettingsPatch.and.returnValue(
        of({
          note: 'Written to the configuration file. This setting is bound via IOptionsMonitor, so it takes effect immediately -- no restart required.',
        }),
      );
    });

    fixture.componentInstance.form.patchValue({ enableRecursion: true });
    await fixture.componentInstance.save();

    expect(apiSpy.apiRecursiveSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ enableRecursion: true, maxReferralDepth: 30 }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('takes effect immediately'),
    );
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiRecursiveSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });

  it('loads the root hint and trust anchor overrides as editable rows', async () => {
    const fixture = await createComponent(undefined, {
      rootHintServersOverride: [{ name: 'a.root.test.', addresses: ['192.0.2.1', '2001:db8::1'] }],
      trustAnchorsOverride: [
        { keyTag: 20326, algorithm: 'RSASHA256', digestType: 2, digestHex: 'E06D44B8' },
      ],
    });
    const component = fixture.componentInstance;

    expect(component.rootHintServers.length).toBe(1);
    expect(component.rootHintServers.at(0).controls.addresses.value).toBe('192.0.2.1, 2001:db8::1');
    expect(component.trustAnchors.at(0).controls.keyTag.value).toBe(20326);
    expect(fixture.nativeElement.textContent).toContain('Trust anchors override');
    expect(fixture.nativeElement.textContent).not.toContain('configuration-file edit');
  });

  it('save() leaves unchanged override lists out, so no restart is reported', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiRecursiveSettingsPatch.and.returnValue(of({ note: 'Saved.' }));
    });

    await fixture.componentInstance.save();

    const body = apiSpy.apiRecursiveSettingsPatch.calls.mostRecent().args[0].body;
    expect('rootHintServersOverride' in body).toBeFalse();
    expect('trustAnchorsOverride' in body).toBeFalse();
    expect('rootHintServers' in body).toBeFalse();
  });

  it('save() sends a changed override list', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiRecursiveSettingsPatch.and.returnValue(of({ note: 'Restart required.' }));
    });
    const component = fixture.componentInstance;

    component.addRootHintServer();
    component.rootHintServers.at(0).setValue({ name: 'a.root.test.', addresses: '192.0.2.1' });
    await component.save();

    expect(apiSpy.apiRecursiveSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({
        rootHintServersOverride: [{ name: 'a.root.test.', addresses: ['192.0.2.1'] }],
      }),
    });
    const body = apiSpy.apiRecursiveSettingsPatch.calls.mostRecent().args[0].body;
    expect('trustAnchorsOverride' in body).toBeFalse();
  });

  it('removing the last row sends an empty list, restoring the built-in root', async () => {
    const fixture = await createComponent(
      () => apiSpy.apiRecursiveSettingsPatch.and.returnValue(of({ note: 'Saved.' })),
      { rootHintServersOverride: [{ name: 'a.root.test.', addresses: ['192.0.2.1'] }] },
    );

    fixture.componentInstance.removeRootHintServer(0);
    await fixture.componentInstance.save();

    expect(apiSpy.apiRecursiveSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ rootHintServersOverride: [] }),
    });
  });

  it('toRootHintServers() and toTrustAnchors() normalize row input', () => {
    expect(toRootHintServers([{ name: ' ns. ', addresses: '192.0.2.1,  2001:db8::1\n' }])).toEqual([
      { name: 'ns.', addresses: ['192.0.2.1', '2001:db8::1'] },
    ]);
    expect(
      toTrustAnchors([{ keyTag: 1, algorithm: 'ED25519', digestType: 2, digestHex: 'ab cd\nef' }]),
    ).toEqual([{ keyTag: 1, algorithm: 'ED25519', digestType: 2, digestHex: 'ABCDEF' }]);
  });
});
