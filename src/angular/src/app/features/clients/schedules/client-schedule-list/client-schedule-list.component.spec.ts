import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ClientSchedulesService, ClientsService } from '../../../../api-client';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { ClientScheduleListComponent } from './client-schedule-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('ClientScheduleListComponent', () => {
  let schedulesApiSpy: {
    apiClientsClientIdSchedulesGet: jasmine.Spy;
    apiClientsClientIdSchedulesIdDelete: jasmine.Spy;
  };
  let clientsApiSpy: { apiClientsIdGet: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    schedulesApiSpy = jasmine.createSpyObj('ClientSchedulesService', [
      'apiClientsClientIdSchedulesGet',
      'apiClientsClientIdSchedulesIdDelete',
    ]);
    clientsApiSpy = jasmine.createSpyObj('ClientsService', ['apiClientsIdGet']);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    clientsApiSpy.apiClientsIdGet.and.returnValue(
      of({
        id: 'client-1',
        addresses: [
          { id: 'address-1', clientIdentifier: 'AA:BB:CC:DD:EE:FF', protocolVersion: 'IPv4' },
        ],
        displayName: 'Living Room TV',
      }),
    );
    schedulesApiSpy.apiClientsClientIdSchedulesGet.and.returnValue(
      of({
        items: [
          {
            id: 'schedule-1',
            clientId: 'client-1',
            daysOfWeek: 'Sunday, Monday',
            startTime: '21:00:00',
            endTime: '07:00:00',
            timeZoneId: 'America/New_York',
            isDisabled: false,
          },
        ],
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [ClientScheduleListComponent],
      providers: [
        provideRouter([]),
        { provide: ClientSchedulesService, useValue: schedulesApiSpy },
        { provide: ClientsService, useValue: clientsApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ clientId: 'client-1' }) } },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ClientScheduleListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads schedules for the client', async () => {
    const fixture = await createComponent();

    expect(fixture.componentInstance.schedules().length).toBe(1);
    // Cast for the same reason ClientScheduleFormComponent's own doc comment explains: the wire
    // value is a combined-flags comma-separated string, but the generated
    // DnsScheduleDayOfWeekEnum TypeScript type only allows one discrete value.
    expect(fixture.componentInstance.schedules()[0].daysOfWeek as unknown as string).toBe(
      'Sunday, Monday',
    );
    expect(fixture.componentInstance.clientLabel()).toBe('Living Room TV');
  });

  it('deleteSchedule() removes and reloads once confirmed', async () => {
    const fixture = await createComponent();
    schedulesApiSpy.apiClientsClientIdSchedulesIdDelete.and.returnValue(of(undefined));

    const deletePromise = fixture.componentInstance.deleteSchedule(
      fixture.componentInstance.schedules()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(schedulesApiSpy.apiClientsClientIdSchedulesIdDelete).toHaveBeenCalledWith({
      clientId: 'client-1',
      id: 'schedule-1',
    });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteSchedule() does nothing when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent();

    const deletePromise = fixture.componentInstance.deleteSchedule(
      fixture.componentInstance.schedules()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(false);
    await deletePromise;

    expect(schedulesApiSpy.apiClientsClientIdSchedulesIdDelete).not.toHaveBeenCalled();
  });
});
