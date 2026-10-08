import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { DnsPipelineSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { DnsPipelineSettingsComponent } from './dns-pipeline-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the Pipeline page: loading, the max concurrent queries field (binding and its minimum), saving with the backend note and a save error.
 * TestBed with a spy-backed settings service and `NotificationService`.
 */
describe('DnsPipelineSettingsComponent', () => {
  let apiSpy: {
    apiDnsPipelineSettingsGet: jasmine.Spy;
    apiDnsPipelineSettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('DnsPipelineSettingsService', [
      'apiDnsPipelineSettingsGet',
      'apiDnsPipelineSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiDnsPipelineSettingsGet.and.returnValue(
      of({
        workerCount: 8,
        maxConcurrentQueries: 1000,
        channelCapacity: 4096,
        maxUdpPayloadSizeOctets: 1232,
        maxUdpAmplificationFactor: 20,
        minUdpAmplificationFloorSizeOctets: 512,
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [DnsPipelineSettingsComponent],
      providers: [
        { provide: DnsPipelineSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(DnsPipelineSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiDnsPipelineSettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.workerCount.value).toBe(8);
    expect(fixture.componentInstance.form.controls.maxConcurrentQueries.value).toBe(1000);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the full form, then surfaces whatever note the backend returns', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiDnsPipelineSettingsPatch.and.returnValue(
        of({ note: 'Written to the configuration file. ... restart it specifically.' }),
      );
    });

    fixture.componentInstance.form.patchValue({ workerCount: 2 });
    await fixture.componentInstance.save();

    expect(apiSpy.apiDnsPipelineSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ workerCount: 2 }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('restart it specifically'),
    );
  });

  it('binds the max concurrent queries input to its form control', async () => {
    const fixture = await createComponent();

    const input: HTMLInputElement = fixture.nativeElement.querySelector(
      '#pipeline-max-concurrent-queries',
    );
    expect(input.value).toBe('1000');

    input.value = '50';
    input.dispatchEvent(new Event('input'));
    expect(fixture.componentInstance.form.controls.maxConcurrentQueries.value).toBe(50);
  });

  it('rejects a max concurrent queries value below 1 and sends nothing', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.form.patchValue({ maxConcurrentQueries: 0 });
    await fixture.componentInstance.save();

    expect(fixture.componentInstance.form.controls.maxConcurrentQueries.valid).toBeFalse();
    expect(apiSpy.apiDnsPipelineSettingsPatch).not.toHaveBeenCalled();
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiDnsPipelineSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });
});
