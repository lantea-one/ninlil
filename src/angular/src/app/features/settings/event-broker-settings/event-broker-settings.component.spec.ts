import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { EventBrokerSettingsService, EventTransportEnum } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { EventBrokerSettingsComponent } from './event-broker-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('EventBrokerSettingsComponent', () => {
  let apiSpy: { apiEventBrokerSettingsGet: jasmine.Spy; apiEventBrokerSettingsPatch: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('EventBrokerSettingsService', [
      'apiEventBrokerSettingsGet',
      'apiEventBrokerSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiEventBrokerSettingsGet.and.returnValue(
      of({
        transport: EventTransportEnum.RabbitMq,
        isConnected: true,
        rabbitMq: {
          hostName: 'broker.local',
          port: 5672,
          userName: 'ninlil',
          virtualHost: '/',
          exchangeName: 'ninlil-events',
          initialReconnectDelay: '00:00:05',
          maxReconnectDelay: '00:01:00',
        },
        activeMq: null,
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [EventBrokerSettingsComponent],
      providers: [
        { provide: EventBrokerSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(EventBrokerSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init, never pre-filling a password', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiEventBrokerSettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.rabbitMqHostName.value).toBe('broker.local');
    expect(fixture.componentInstance.form.controls.rabbitMqPassword.value).toBe('');
    expect(fixture.componentInstance.isConnected()).toBeTrue();
  });

  it('save() with a blank password omits the password key entirely, leaving the stored password untouched', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiEventBrokerSettingsPatch.and.returnValue(
        of({ note: 'Written to the configuration file. ... a restart is required.' }),
      );
    });

    fixture.componentInstance.form.patchValue({ rabbitMqExchangeName: 'renamed-exchange' });
    await fixture.componentInstance.save();

    const body = apiSpy.apiEventBrokerSettingsPatch.calls.mostRecent().args[0].body;
    expect(body.transport).toBe(EventTransportEnum.RabbitMq);
    expect(body.rabbitMq.exchangeName).toBe('renamed-exchange');
    expect('password' in body.rabbitMq).toBeFalse();
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('a restart is required'),
    );
  });

  it('save() with a non-blank password includes it in the patch body', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiEventBrokerSettingsPatch.and.returnValue(of({ note: 'ok' }));
    });

    fixture.componentInstance.form.patchValue({ rabbitMqPassword: 'new-password' });
    await fixture.componentInstance.save();

    const body = apiSpy.apiEventBrokerSettingsPatch.calls.mostRecent().args[0].body;
    expect(body.rabbitMq.password).toBe('new-password');
  });

  it('save() only includes the activeMq sub-object when ActiveMq is the selected transport', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiEventBrokerSettingsPatch.and.returnValue(of({ note: 'ok' }));
    });

    fixture.componentInstance.form.patchValue({
      transport: EventTransportEnum.ActiveMq,
      activeMqHostName: 'activemq.local',
    });
    await fixture.componentInstance.save();

    const body = apiSpy.apiEventBrokerSettingsPatch.calls.mostRecent().args[0].body;
    expect(body.transport).toBe(EventTransportEnum.ActiveMq);
    expect(body.activeMq.hostName).toBe('activemq.local');
    expect(body.rabbitMq).toBeUndefined();
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiEventBrokerSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });
});
