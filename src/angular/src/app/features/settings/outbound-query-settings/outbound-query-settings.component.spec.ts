import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { OutboundQuerySettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { OutboundQuerySettingsComponent } from './outbound-query-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the Outbound Query Hardening page: loading, saving the excluded ports as
 * numbers and a save error. TestBed with a spy-backed settings service and `NotificationService`.
 */
describe('OutboundQuerySettingsComponent', () => {
  let apiSpy: {
    apiOutboundQuerySettingsGet: jasmine.Spy;
    apiOutboundQuerySettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('OutboundQuerySettingsService', [
      'apiOutboundQuerySettingsGet',
      'apiOutboundQuerySettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiOutboundQuerySettingsGet.and.returnValue(
      of({
        enableSourcePortPool: true,
        poolSizePerAddressFamily: 128,
        excludedSourcePorts: [51820],
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [OutboundQuerySettingsComponent],
      providers: [
        { provide: OutboundQuerySettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(OutboundQuerySettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiOutboundQuerySettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.enableSourcePortPool.value).toBeTrue();
    expect(fixture.componentInstance.form.controls.excludedSourcePortsText.value).toBe('51820');
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() splits the excluded-ports textarea into a trimmed, numeric array', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiOutboundQuerySettingsPatch.and.returnValue(of({ note: 'ok' }));
    });

    fixture.componentInstance.form.patchValue({
      excludedSourcePortsText: ' 51820 \n\n5353\n',
    });
    await fixture.componentInstance.save();

    expect(apiSpy.apiOutboundQuerySettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ excludedSourcePorts: [51820, 5353] }),
    });
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiOutboundQuerySettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });
});
