import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { WebListenSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { WebListenSettingsComponent } from './web-listen-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('WebListenSettingsComponent', () => {
  let apiSpy: {
    apiWebListenSettingsGet: jasmine.Spy;
    apiWebListenSettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('WebListenSettingsService', [
      'apiWebListenSettingsGet',
      'apiWebListenSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiWebListenSettingsGet.and.returnValue(
      of({
        httpPort: 80,
        httpsPort: 443,
        certificate: {
          pfxPath: '',
          pfxPasswordConfigured: false,
          selfSignedSubjectAlternativeNames: ['localhost'],
          certificateReloadPollInterval: '00:05:00',
          allowedProtocols: [],
        },
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [WebListenSettingsComponent],
      providers: [
        { provide: WebListenSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(WebListenSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiWebListenSettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.httpPort.value).toBe(80);
    expect(fixture.componentInstance.form.controls.selfSignedSubjectAlternativeNames.value).toBe(
      'localhost',
    );
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() shows the confirmation dialog and does nothing when dismissed', async () => {
    const fixture = await createComponent();

    const savePromise = fixture.componentInstance.save();
    expect(fixture.componentInstance.confirmDialogVisible()).toBeTrue();

    fixture.componentInstance.onConfirmDialogResolved(false);
    await savePromise;

    expect(fixture.componentInstance.confirmDialogVisible()).toBeFalse();
    expect(apiSpy.apiWebListenSettingsPatch).not.toHaveBeenCalled();
  });

  it('save() PATCHes the form and surfaces the restart-required note once confirmed', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiWebListenSettingsPatch.and.returnValue(
        of({
          note: 'Written to the configuration file. ... a full process restart is required before this change takes effect.',
        }),
      );
    });

    fixture.componentInstance.form.patchValue({ httpPort: 8080, allowedProtocolTls12: true });
    const savePromise = fixture.componentInstance.save();
    fixture.componentInstance.onConfirmDialogResolved(true);
    await savePromise;

    expect(apiSpy.apiWebListenSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({
        httpPort: 8080,
        certificate: jasmine.objectContaining({
          allowedProtocols: ['Tls12'],
        }),
      }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('restart is required'),
    );
  });

  it('omits pfxPassword from the PATCH body when left blank', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiWebListenSettingsPatch.and.returnValue(of({ note: 'ok' }));
    });

    const savePromise = fixture.componentInstance.save();
    fixture.componentInstance.onConfirmDialogResolved(true);
    await savePromise;

    const body = apiSpy.apiWebListenSettingsPatch.calls.mostRecent().args[0].body;
    expect(body.certificate.pfxPassword).toBeUndefined();
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiWebListenSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    const savePromise = fixture.componentInstance.save();
    fixture.componentInstance.onConfirmDialogResolved(true);
    await savePromise;

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });
});
