import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { MacVendorSettingsService } from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';
import { NotificationService } from '../../../shared/notification/notification.service';
import { MacVendorSettingsComponent } from './mac-vendor-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('MacVendorSettingsComponent', () => {
  let apiSpy: {
    apiMacVendorSettingsGet: jasmine.Spy;
    apiMacVendorSettingsPatch: jasmine.Spy;
    apiMacVendorSettingsRefreshPost: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let authSpy: { hasPermission: jasmine.Spy };

  const status = {
    dataSource: 'Embedded' as const,
    generatedAtUtc: '2026-09-23T17:20:47Z',
    maLEntryCount: 40214,
    maMEntryCount: 6599,
    maSEntryCount: 7200,
    totalEntryCount: 54013,
    lastRefreshAttemptUtc: null,
    lastRefreshResult: undefined,
    lastRefreshError: null,
    lastSuccessfulCheckUtc: null,
    nextRefreshDueUtc: '2026-10-07T17:20:47Z',
  };

  async function createComponent(canManage = true, configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('MacVendorSettingsService', [
      'apiMacVendorSettingsGet',
      'apiMacVendorSettingsPatch',
      'apiMacVendorSettingsRefreshPost',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);
    authSpy = jasmine.createSpyObj('AuthService', ['hasPermission']);
    authSpy.hasPermission.and.returnValue(canManage);

    apiSpy.apiMacVendorSettingsGet.and.returnValue(
      of({
        enableAutomaticRefresh: true,
        refreshInterval: '14.00:00:00',
        maLRegistryUrl: 'https://standards-oui.ieee.org/oui/oui.csv',
        maMRegistryUrl: 'https://standards-oui.ieee.org/oui28/mam.csv',
        maSRegistryUrl: 'https://standards-oui.ieee.org/oui36/oui36.csv',
        dataFilePath: '/opt/ninlil/data/mac-vendors.snapshot.br',
        status,
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [MacVendorSettingsComponent],
      providers: [
        { provide: MacVendorSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        { provide: AuthService, useValue: authSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(MacVendorSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the settings and shows the loaded data status', async () => {
    const fixture = await createComponent();
    const text = fixture.nativeElement.querySelector('[data-testid="mac-vendor-status"]')
      .textContent as string;

    expect(fixture.componentInstance.form.controls.refreshInterval.value).toBe('14.00:00:00');
    expect(text).toContain('Built-in snapshot');
    expect(text).toContain('54,013');
    expect(text).toContain('MA-S 7,200');
    expect(text).toContain('Never');
    expect(text).toContain('/opt/ninlil/data/mac-vendors.snapshot.br');
  });

  it('PATCHes every field and shows the backend note on save', async () => {
    const fixture = await createComponent(true, () =>
      apiSpy.apiMacVendorSettingsPatch.and.returnValue(of({ note: 'Applied live.' })),
    );
    fixture.componentInstance.form.controls.refreshInterval.setValue('30.00:00:00');

    await fixture.componentInstance.save();

    expect(apiSpy.apiMacVendorSettingsPatch).toHaveBeenCalledWith({
      body: {
        enableAutomaticRefresh: true,
        refreshInterval: '30.00:00:00',
        maLRegistryUrl: 'https://standards-oui.ieee.org/oui/oui.csv',
        maMRegistryUrl: 'https://standards-oui.ieee.org/oui28/mam.csv',
        maSRegistryUrl: 'https://standards-oui.ieee.org/oui36/oui36.csv',
      },
    });
    expect(notificationSpy.success).toHaveBeenCalledWith('Applied live.');
  });

  it('never PATCHes a malformed interval or URL', async () => {
    const fixture = await createComponent();
    fixture.componentInstance.form.controls.maMRegistryUrl.setValue('not a url');

    await fixture.componentInstance.save();

    expect(apiSpy.apiMacVendorSettingsPatch).not.toHaveBeenCalled();
  });

  it('surfaces a backend rejection as an error banner', async () => {
    const fixture = await createComponent(true, () =>
      apiSpy.apiMacVendorSettingsPatch.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 400,
              error: { detail: "'refreshInterval' must be at least one day." },
            }),
        ),
      ),
    );

    await fixture.componentInstance.save();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('at least one day');
  });

  it('refreshes on demand and shows the new status', async () => {
    const fixture = await createComponent(true, () =>
      apiSpy.apiMacVendorSettingsRefreshPost.and.returnValue(
        of({
          ...status,
          dataSource: 'Downloaded',
          lastRefreshAttemptUtc: '2026-09-23T18:00:00Z',
          lastRefreshResult: 'Updated',
        }),
      ),
    );

    (
      fixture.nativeElement.querySelector('[data-testid="mac-vendor-refresh"]') as HTMLButtonElement
    ).click();
    await flushMicrotasks();
    fixture.detectChanges();

    expect(apiSpy.apiMacVendorSettingsRefreshPost).toHaveBeenCalled();
    expect(notificationSpy.success).toHaveBeenCalledWith(
      'Vendor data updated from the IEEE registries.',
    );
    expect(fixture.nativeElement.textContent).toContain('Downloaded from IEEE');
    expect(
      fixture.nativeElement.querySelector('[data-testid="mac-vendor-last-result"]').textContent,
    ).toContain('Updated');
  });

  it('reports a failed refresh without treating it as an error response', async () => {
    const fixture = await createComponent(true, () =>
      apiSpy.apiMacVendorSettingsRefreshPost.and.returnValue(
        of({
          ...status,
          lastRefreshAttemptUtc: '2026-09-23T18:00:00Z',
          lastRefreshResult: 'Failed',
          lastRefreshError: 'Connection refused',
        }),
      ),
    );

    await fixture.componentInstance.refreshNow();
    fixture.detectChanges();

    expect(notificationSpy.error).toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Connection refused');
    expect(fixture.componentInstance.errorMessage()).toBeNull();
  });

  it('hides "Refresh now" without macvendorsettings:manage', async () => {
    const fixture = await createComponent(false);

    expect(authSpy.hasPermission).toHaveBeenCalledWith('macvendorsettings:manage');
    expect(fixture.nativeElement.querySelector('[data-testid="mac-vendor-refresh"]')).toBeNull();
  });
});
