import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ClientGroupSchedulesService } from '../../../../api-client';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { ClientGroupScheduleFormComponent } from './client-group-schedule-form.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the group schedule form: building the day string, rejecting no days, loading
 * an "All" schedule in edit mode and validation. TestBed with a spy-backed
 * `ClientGroupSchedulesService` and a stubbed route.
 */
describe('ClientGroupScheduleFormComponent', () => {
  let schedulesApiSpy: {
    apiClientGroupsGroupIdSchedulesPost: jasmine.Spy;
    apiClientGroupsGroupIdSchedulesIdPut: jasmine.Spy;
    apiClientGroupsGroupIdSchedulesIdGet: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let navigateSpy: jasmine.Spy;

  async function createComponent(scheduleId: string | null, configureApis?: () => void) {
    schedulesApiSpy = jasmine.createSpyObj('ClientGroupSchedulesService', [
      'apiClientGroupsGroupIdSchedulesPost',
      'apiClientGroupsGroupIdSchedulesIdPut',
      'apiClientGroupsGroupIdSchedulesIdGet',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [ClientGroupScheduleFormComponent],
      providers: [
        provideRouter([]),
        { provide: ClientGroupSchedulesService, useValue: schedulesApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: convertToParamMap({
                groupId: 'group-1',
                ...(scheduleId ? { scheduleId } : {}),
              }),
            },
          },
        },
      ],
    }).compileComponents();

    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    const fixture = TestBed.createComponent(ClientGroupScheduleFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('is in create mode with no scheduleId route param', async () => {
    const fixture = await createComponent(null);
    expect(fixture.componentInstance.isEditMode).toBeFalse();
  });

  it('posts a new schedule built from every checked day and navigates back to the list', async () => {
    const fixture = await createComponent(null, () => {
      schedulesApiSpy.apiClientGroupsGroupIdSchedulesPost.and.returnValue(of({ id: 'schedule-1' }));
    });

    fixture.componentInstance.form.patchValue({
      sunday: true,
      monday: true,
      tuesday: true,
      wednesday: true,
      thursday: true,
      friday: true,
      saturday: true,
      startTime: '22:00',
      endTime: '06:30',
      timeZoneId: 'Europe/London',
    });

    await fixture.componentInstance.save();

    expect(schedulesApiSpy.apiClientGroupsGroupIdSchedulesPost).toHaveBeenCalledWith(
      jasmine.objectContaining({
        groupId: 'group-1',
        clientGroupScheduleWriteRequest: jasmine.objectContaining({
          daysOfWeek: 'Sunday, Monday, Tuesday, Wednesday, Thursday, Friday, Saturday',
          timeZoneId: 'Europe/London',
        }),
      }),
    );
    expect(notificationSpy.success).toHaveBeenCalled();
    expect(navigateSpy).toHaveBeenCalledWith(['/client-groups', 'group-1', 'schedules']);
  });

  it('save() rejects a schedule with no day checked, without calling the API', async () => {
    const fixture = await createComponent(null);

    fixture.componentInstance.form.patchValue({ startTime: '09:00', endTime: '17:00' });

    await fixture.componentInstance.save();

    expect(schedulesApiSpy.apiClientGroupsGroupIdSchedulesPost).not.toHaveBeenCalled();
    expect(fixture.componentInstance.errorMessage()).toContain('at least one day');
  });

  it('loads an existing "All days" schedule in edit mode, checking every box, and PUTs on save', async () => {
    const fixture = await createComponent('schedule-1', () => {
      schedulesApiSpy.apiClientGroupsGroupIdSchedulesIdGet.and.returnValue(
        of({
          id: 'schedule-1',
          clientGroupId: 'group-1',
          daysOfWeek: 'All',
          startTime: '22:00:00',
          endTime: '06:30:00',
          timeZoneId: 'Europe/London',
          isDisabled: false,
          comments: null,
        }),
      );
      schedulesApiSpy.apiClientGroupsGroupIdSchedulesIdPut.and.returnValue(of(undefined));
    });

    expect(fixture.componentInstance.form.controls.sunday.value).toBeTrue();
    expect(fixture.componentInstance.form.controls.saturday.value).toBeTrue();
    expect(fixture.componentInstance.form.controls.startTime.value).toBe('22:00');

    await fixture.componentInstance.save();

    expect(schedulesApiSpy.apiClientGroupsGroupIdSchedulesIdPut).toHaveBeenCalledWith(
      jasmine.objectContaining({ groupId: 'group-1', id: 'schedule-1' }),
    );
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('save() does nothing while the form is invalid', async () => {
    const fixture = await createComponent(null);
    fixture.componentInstance.form.patchValue({ startTime: '', endTime: '' });

    await fixture.componentInstance.save();

    expect(schedulesApiSpy.apiClientGroupsGroupIdSchedulesPost).not.toHaveBeenCalled();
  });
});
