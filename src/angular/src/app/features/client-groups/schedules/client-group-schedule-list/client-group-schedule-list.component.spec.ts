import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ClientGroupSchedulesService, ClientGroupsService } from '../../../../api-client';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { ClientGroupScheduleListComponent } from './client-group-schedule-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('ClientGroupScheduleListComponent', () => {
  let schedulesApiSpy: {
    apiClientGroupsGroupIdSchedulesGet: jasmine.Spy;
    apiClientGroupsGroupIdSchedulesIdDelete: jasmine.Spy;
  };
  let clientGroupsApiSpy: { apiClientGroupsIdGet: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    schedulesApiSpy = jasmine.createSpyObj('ClientGroupSchedulesService', [
      'apiClientGroupsGroupIdSchedulesGet',
      'apiClientGroupsGroupIdSchedulesIdDelete',
    ]);
    clientGroupsApiSpy = jasmine.createSpyObj('ClientGroupsService', ['apiClientGroupsIdGet']);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    clientGroupsApiSpy.apiClientGroupsIdGet.and.returnValue(
      of({ id: 'group-1', name: 'Trusted LAN' }),
    );
    schedulesApiSpy.apiClientGroupsGroupIdSchedulesGet.and.returnValue(
      of({
        items: [
          {
            id: 'schedule-1',
            clientGroupId: 'group-1',
            daysOfWeek: 'All',
            startTime: '22:00:00',
            endTime: '06:30:00',
            timeZoneId: 'Europe/London',
            isDisabled: false,
          },
        ],
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [ClientGroupScheduleListComponent],
      providers: [
        provideRouter([]),
        { provide: ClientGroupSchedulesService, useValue: schedulesApiSpy },
        { provide: ClientGroupsService, useValue: clientGroupsApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ groupId: 'group-1' }) } },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ClientGroupScheduleListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads schedules for the client group', async () => {
    const fixture = await createComponent();

    expect(fixture.componentInstance.schedules().length).toBe(1);
    expect(fixture.componentInstance.groupName()).toBe('Trusted LAN');
  });

  it('deleteSchedule() removes and reloads once confirmed', async () => {
    const fixture = await createComponent();
    schedulesApiSpy.apiClientGroupsGroupIdSchedulesIdDelete.and.returnValue(of(undefined));

    const deletePromise = fixture.componentInstance.deleteSchedule(
      fixture.componentInstance.schedules()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(schedulesApiSpy.apiClientGroupsGroupIdSchedulesIdDelete).toHaveBeenCalledWith({
      groupId: 'group-1',
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

    expect(schedulesApiSpy.apiClientGroupsGroupIdSchedulesIdDelete).not.toHaveBeenCalled();
  });
});
