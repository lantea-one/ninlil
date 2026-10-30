import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { DnsCacheService, RecursiveSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import {
  RecursiveSettingsComponent,
  toInsecureDomains,
  toRootHintServers,
  toTrustAnchors,
} from './recursive-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the Recursive Resolver page: loading, saving only the changed fields, Save
 * disabled without a change, the baseline moving after a save, when the root hint, trust anchor
 * and Insecure domain lists are sent, the cache card and errors, plus the row conversion helpers. TestBed with a
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
        insecureDomains: ['home.arpa', 'internal'],
        useCustomInsecureDomains: false,
        builtInInsecureDomains: ['home.arpa', 'internal'],
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

    // An administrator's view: every permission held, so the write-only buttons render.
    spyOn(TestBed.inject(AuthService), 'hasPermission').and.returnValue(true);
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

  it('save() PATCHes only the changed field, then surfaces the hot-reload note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiRecursiveSettingsPatch.and.returnValue(
        of({
          note: 'Written to the configuration file. This setting is bound via IOptionsMonitor, so it takes effect immediately -- no restart required.',
        }),
      );
    });

    fixture.componentInstance.form.patchValue({ enableRecursion: true });
    await fixture.componentInstance.save();

    expect(apiSpy.apiRecursiveSettingsPatch).toHaveBeenCalledOnceWith({
      body: { enableRecursion: true },
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('takes effect immediately'),
    );
  });

  it('disables Save with a tooltip while nothing changed and does not send an empty patch', async () => {
    const fixture = await createComponent();
    const button = (fixture.nativeElement as HTMLElement).querySelector(
      '.form-card c-card-footer button',
    ) as HTMLButtonElement;

    expect(button.disabled).toBeTrue();
    expect(button.getAttribute('title')).toBe('No changes to save');

    await fixture.componentInstance.save();
    expect(apiSpy.apiRecursiveSettingsPatch).not.toHaveBeenCalled();

    fixture.componentInstance.form.patchValue({ enableRecursion: true });
    fixture.detectChanges();
    expect(button.disabled).toBeFalse();
    expect(button.hasAttribute('title')).toBeFalse();
  });

  it('treats a time span written differently but equal as unchanged', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.form.patchValue({ prefetchRecentAccessWindow: '0:05:00' });

    expect(fixture.componentInstance.hasChanges()).toBeFalse();
  });

  it('save() moves the baseline, so the next save sends only what changed since', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiRecursiveSettingsPatch.and.returnValue(of({ note: 'ok' }));
    });

    fixture.componentInstance.form.patchValue({ enableRecursion: true });
    await fixture.componentInstance.save();
    expect(fixture.componentInstance.hasChanges()).toBeFalse();

    fixture.componentInstance.form.patchValue({ maxReferralDepth: 20 });
    await fixture.componentInstance.save();

    expect(apiSpy.apiRecursiveSettingsPatch.calls.mostRecent().args[0]).toEqual({
      body: { maxReferralDepth: 20 },
    });
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiRecursiveSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    fixture.componentInstance.form.patchValue({ enableRecursion: true });
    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
    // A failed save keeps the edit pending.
    expect(fixture.componentInstance.hasChanges()).toBeTrue();
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

    fixture.componentInstance.form.patchValue({ enableRecursion: true });
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

    expect(apiSpy.apiRecursiveSettingsPatch).toHaveBeenCalledOnceWith({
      body: { rootHintServersOverride: [{ name: 'a.root.test.', addresses: ['192.0.2.1'] }] },
    });
  });

  it('removing the last row sends an empty list, restoring the built-in root', async () => {
    const fixture = await createComponent(
      () => apiSpy.apiRecursiveSettingsPatch.and.returnValue(of({ note: 'Saved.' })),
      { rootHintServersOverride: [{ name: 'a.root.test.', addresses: ['192.0.2.1'] }] },
    );

    fixture.componentInstance.removeRootHintServer(0);
    await fixture.componentInstance.save();

    expect(apiSpy.apiRecursiveSettingsPatch).toHaveBeenCalledOnceWith({
      body: { rootHintServersOverride: [] },
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

  it('shows the Insecure domains section with the list in force', async () => {
    const fixture = await createComponent();
    const element: HTMLElement = fixture.nativeElement;

    const section = element.querySelector('[data-section="insecure-domains"]');
    expect(section).not.toBeNull();
    expect(fixture.componentInstance.form.controls.insecureDomainsText.value).toBe(
      'home.arpa\ninternal',
    );
    expect(section?.querySelector('[data-list-kind]')?.textContent).toContain('Built-in list');
  });

  it('save() leaves an unchanged Insecure domain list out', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiRecursiveSettingsPatch.and.returnValue(of({ note: 'Saved.' }));
    });

    fixture.componentInstance.form.patchValue({ enableRecursion: true });
    await fixture.componentInstance.save();

    const body = apiSpy.apiRecursiveSettingsPatch.calls.mostRecent().args[0].body;
    expect('insecureDomains' in body).toBeFalse();
    expect('insecureDomainsText' in body).toBeFalse();
  });

  it('save() sends an edited Insecure domain list and marks it custom', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiRecursiveSettingsPatch.and.returnValue(of({ note: 'Saved.' }));
    });
    const component = fixture.componentInstance;

    component.form.controls.insecureDomainsText.setValue('home.arpa\nCorp.Example.\n');
    await component.save();

    expect(apiSpy.apiRecursiveSettingsPatch).toHaveBeenCalledOnceWith({
      body: { insecureDomains: ['home.arpa', 'corp.example'] },
    });
    expect(component.insecureDomainsCustom()).toBeTrue();

    // A second save without a change sends nothing: Save is disabled and the call is refused.
    await component.save();
    expect(apiSpy.apiRecursiveSettingsPatch).toHaveBeenCalledTimes(1);
  });

  it('save() can send an empty Insecure domain list', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiRecursiveSettingsPatch.and.returnValue(of({ note: 'Saved.' }));
    });

    fixture.componentInstance.form.controls.insecureDomainsText.setValue('');
    await fixture.componentInstance.save();

    expect(apiSpy.apiRecursiveSettingsPatch).toHaveBeenCalledOnceWith({
      body: { insecureDomains: [] },
    });
  });

  it('Restore defaults refills the editor and a save then sends null', async () => {
    const fixture = await createComponent(
      () => apiSpy.apiRecursiveSettingsPatch.and.returnValue(of({ note: 'Saved.' })),
      { insecureDomains: ['corp.example'], useCustomInsecureDomains: true },
    );
    const component = fixture.componentInstance;
    expect(component.insecureDomainsCustom()).toBeTrue();

    const button: HTMLButtonElement = fixture.nativeElement.querySelector(
      '[data-action="restore-insecure-domains"]',
    );
    button.click();
    expect(component.form.controls.insecureDomainsText.value).toBe('home.arpa\ninternal');

    await component.save();

    expect(apiSpy.apiRecursiveSettingsPatch).toHaveBeenCalledOnceWith({
      body: { insecureDomains: null },
    });
    expect(component.insecureDomainsCustom()).toBeFalse();
  });

  it('editing after Restore defaults sends the edited list, not null', async () => {
    const fixture = await createComponent(
      () => apiSpy.apiRecursiveSettingsPatch.and.returnValue(of({ note: 'Saved.' })),
      { insecureDomains: ['corp.example'], useCustomInsecureDomains: true },
    );
    const component = fixture.componentInstance;

    component.restoreInsecureDomainDefaults();
    component.form.controls.insecureDomainsText.setValue('internal');
    await component.save();

    expect(apiSpy.apiRecursiveSettingsPatch).toHaveBeenCalledOnceWith({
      body: { insecureDomains: ['internal'] },
    });
  });

  it('toInsecureDomains() splits, lowercases and de-duplicates the editor text', () => {
    expect(toInsecureDomains(' A.example., b.example\nA.EXAMPLE  \n\n')).toEqual([
      'a.example',
      'b.example',
    ]);
    expect(toInsecureDomains('')).toEqual([]);
  });
});
