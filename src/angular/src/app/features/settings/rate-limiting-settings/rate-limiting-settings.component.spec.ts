import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { RateLimitingSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { RateLimitingSettingsComponent } from './rate-limiting-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('RateLimitingSettingsComponent', () => {
  let apiSpy: {
    apiRateLimitingSettingsGet: jasmine.Spy;
    apiRateLimitingSettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('RateLimitingSettingsService', [
      'apiRateLimitingSettingsGet',
      'apiRateLimitingSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiRateLimitingSettingsGet.and.returnValue(
      of({
        enable: false,
        queriesPerSecond: 20,
        burstSize: 40,
        trackedClientExpiry: '00:05:00',
        cleanupInterval: '00:01:00',
        maxTrackedClients: 100000,
        subnetPrefixLengthIPv4: 24,
        subnetPrefixLengthIPv6: 56,
        subnetSampleWindow: '00:00:10',
        subnetUdpQueryLimit: 500,
        subnetTcpQueryLimit: 100,
        subnetTruncatePercentage: 50,
        subnetBypassCidrs: ['10.0.0.0/8'],
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [RateLimitingSettingsComponent],
      providers: [
        { provide: RateLimitingSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(RateLimitingSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiRateLimitingSettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.queriesPerSecond.value).toBe(20);
    expect(fixture.componentInstance.form.controls.subnetPrefixLengthIPv4.value).toBe(24);
    expect(fixture.componentInstance.form.controls.subnetBypassCidrsText.value).toBe('10.0.0.0/8');
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the full form, then surfaces the restart-required note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiRateLimitingSettingsPatch.and.returnValue(
        of({ note: 'Written to the configuration file. ... a restart is required.' }),
      );
    });

    fixture.componentInstance.form.patchValue({ enable: true });
    await fixture.componentInstance.save();

    expect(apiSpy.apiRateLimitingSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({
        enable: true,
        queriesPerSecond: 20,
        subnetBypassCidrs: ['10.0.0.0/8'],
      }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('a restart is required'),
    );
  });

  it('save() splits the bypass CIDR textarea into a trimmed, blank-free array', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiRateLimitingSettingsPatch.and.returnValue(of({ note: 'ok' }));
    });

    fixture.componentInstance.form.patchValue({
      subnetBypassCidrsText: ' 10.0.0.0/8 \n\n192.168.0.0/16\n',
    });
    await fixture.componentInstance.save();

    expect(apiSpy.apiRateLimitingSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({
        subnetBypassCidrs: ['10.0.0.0/8', '192.168.0.0/16'],
      }),
    });
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiRateLimitingSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });
});
