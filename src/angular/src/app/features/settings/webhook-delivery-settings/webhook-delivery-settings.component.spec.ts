import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { WebhookDeliverySettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { WebhookDeliverySettingsComponent } from './webhook-delivery-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('WebhookDeliverySettingsComponent', () => {
  let apiSpy: {
    apiWebhookDeliverySettingsGet: jasmine.Spy;
    apiWebhookDeliverySettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('WebhookDeliverySettingsService', [
      'apiWebhookDeliverySettingsGet',
      'apiWebhookDeliverySettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiWebhookDeliverySettingsGet.and.returnValue(
      of({
        initialRetryDelay: '00:00:01',
        maxRetryDelay: '00:00:30',
        requestTimeout: '00:00:10',
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [WebhookDeliverySettingsComponent],
      providers: [
        { provide: WebhookDeliverySettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(WebhookDeliverySettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiWebhookDeliverySettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.requestTimeout.value).toBe('00:00:10');
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the full form, then surfaces whatever note the backend returns', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiWebhookDeliverySettingsPatch.and.returnValue(
        of({ note: 'Written to the configuration file. ... not instant.' }),
      );
    });

    fixture.componentInstance.form.patchValue({ requestTimeout: '00:00:20' });
    await fixture.componentInstance.save();

    expect(apiSpy.apiWebhookDeliverySettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ requestTimeout: '00:00:20' }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(jasmine.stringContaining('not instant'));
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiWebhookDeliverySettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });
});
