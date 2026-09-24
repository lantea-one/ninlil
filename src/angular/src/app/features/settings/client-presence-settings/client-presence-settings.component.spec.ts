import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { ClientPresenceSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { ClientPresenceSettingsComponent } from './client-presence-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('ClientPresenceSettingsComponent', () => {
  let apiSpy: {
    apiClientPresenceSettingsGet: jasmine.Spy;
    apiClientPresenceSettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('ClientPresenceSettingsService', [
      'apiClientPresenceSettingsGet',
      'apiClientPresenceSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiClientPresenceSettingsGet.and.returnValue(
      of({
        onlineWindow: '00:05:00',
        flushInterval: '00:00:30',
        enableNeighborTableSignal: false,
        neighborTablePollInterval: '00:00:15',
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [ClientPresenceSettingsComponent],
      providers: [
        { provide: ClientPresenceSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ClientPresenceSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();
    const form = fixture.componentInstance.form;

    expect(apiSpy.apiClientPresenceSettingsGet).toHaveBeenCalled();
    expect(form.controls.onlineWindow.value).toBe('00:05:00');
    expect(form.controls.enableNeighborTableSignal.value).toBeFalse();
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('PATCHes every field and shows the backend note on save', async () => {
    const fixture = await createComponent(() =>
      apiSpy.apiClientPresenceSettingsPatch.and.returnValue(of({ note: 'Applied live.' })),
    );
    fixture.componentInstance.form.controls.onlineWindow.setValue('00:30:00');

    await fixture.componentInstance.save();

    expect(apiSpy.apiClientPresenceSettingsPatch).toHaveBeenCalledWith({
      body: {
        onlineWindow: '00:30:00',
        flushInterval: '00:00:30',
        enableNeighborTableSignal: false,
        neighborTablePollInterval: '00:00:15',
      },
    });
    expect(notificationSpy.success).toHaveBeenCalledWith('Applied live.');
  });

  it('treats a malformed duration as invalid and never PATCHes it', async () => {
    const fixture = await createComponent();
    fixture.componentInstance.form.controls.flushInterval.setValue('thirty seconds');

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.form.invalid).toBeTrue();
    expect(apiSpy.apiClientPresenceSettingsPatch).not.toHaveBeenCalled();
  });

  it('surfaces a backend rejection as an error banner', async () => {
    const fixture = await createComponent(() =>
      apiSpy.apiClientPresenceSettingsPatch.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 400,
              error: { detail: "'onlineWindow' must be a valid, positive TimeSpan." },
            }),
        ),
      ),
    );

    await fixture.componentInstance.save();
    fixture.detectChanges();

    expect(fixture.componentInstance.errorMessage()).toContain('positive TimeSpan');
    expect(fixture.nativeElement.textContent).toContain('positive TimeSpan');
  });
});
