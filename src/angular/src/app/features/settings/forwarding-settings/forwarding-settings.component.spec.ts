import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ForwardingSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { ForwardingSettingsComponent } from './forwarding-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('ForwardingSettingsComponent', () => {
  let apiSpy: {
    apiForwardingSettingsGet: jasmine.Spy;
    apiForwardingSettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('ForwardingSettingsService', [
      'apiForwardingSettingsGet',
      'apiForwardingSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiForwardingSettingsGet.and.returnValue(
      of({
        enable: false,
        failoverStrategy: 'HealthCheckedRoundRobin',
        healthCheckInterval: '00:00:30',
        perForwarderTimeout: '00:00:03',
        enableCache: false,
        maxCacheEntries: 2500,
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [ForwardingSettingsComponent],
      providers: [
        provideRouter([]),
        { provide: ForwardingSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ForwardingSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiForwardingSettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.enable.value).toBeFalse();
    expect(fixture.componentInstance.form.controls.healthCheckInterval.value).toBe('00:00:30');
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the full form, then surfaces the hot-reload note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiForwardingSettingsPatch.and.returnValue(
        of({
          note: 'Written to the configuration file. This setting is bound via IOptionsMonitor, so it takes effect immediately -- no restart required.',
        }),
      );
    });

    fixture.componentInstance.form.patchValue({ enable: true });
    await fixture.componentInstance.save();

    expect(apiSpy.apiForwardingSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ enable: true, failoverStrategy: 'HealthCheckedRoundRobin' }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('takes effect immediately'),
    );
  });

  it('loads and saves the answer cache settings', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiForwardingSettingsPatch.and.returnValue(of({ note: 'saved' }));
    });
    const form = fixture.componentInstance.form;

    expect(form.controls.enableCache.value).toBeFalse();
    expect(form.controls.maxCacheEntries.value).toBe(2500);

    form.patchValue({ enableCache: true, maxCacheEntries: 5000 });
    await fixture.componentInstance.save();

    expect(apiSpy.apiForwardingSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ enableCache: true, maxCacheEntries: 5000 }),
    });
  });

  it('rejects a cache size below one', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.form.patchValue({ maxCacheEntries: 0 });

    expect(fixture.componentInstance.form.controls.maxCacheEntries.valid).toBeFalse();
  });

  it('rejects a malformed duration', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.form.patchValue({ perForwarderTimeout: '' });

    expect(fixture.componentInstance.form.controls.perForwarderTimeout.valid).toBeFalse();
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiForwardingSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });
});
