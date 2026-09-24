import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { RecursiveSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { RecursiveSettingsComponent } from './recursive-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('RecursiveSettingsComponent', () => {
  let apiSpy: {
    apiRecursiveSettingsGet: jasmine.Spy;
    apiRecursiveSettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('RecursiveSettingsService', [
      'apiRecursiveSettingsGet',
      'apiRecursiveSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

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
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [RecursiveSettingsComponent],
      providers: [
        { provide: RecursiveSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(RecursiveSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

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
});
