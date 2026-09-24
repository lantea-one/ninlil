import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ClientSchedulesService } from '../../../../api-client';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { ClientScheduleFormComponent } from './client-schedule-form.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('ClientScheduleFormComponent', () => {
  let schedulesApiSpy: {
    apiClientsClientIdSchedulesPost: jasmine.Spy;
    apiClientsClientIdSchedulesIdPut: jasmine.Spy;
    apiClientsClientIdSchedulesIdGet: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let navigateSpy: jasmine.Spy;

  async function createComponent(scheduleId: string | null, configureApis?: () => void) {
    schedulesApiSpy = jasmine.createSpyObj('ClientSchedulesService', [
      'apiClientsClientIdSchedulesPost',
      'apiClientsClientIdSchedulesIdPut',
      'apiClientsClientIdSchedulesIdGet',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [ClientScheduleFormComponent],
      providers: [
        provideRouter([]),
        { provide: ClientSchedulesService, useValue: schedulesApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: convertToParamMap({
                clientId: 'client-1',
                ...(scheduleId ? { scheduleId } : {}),
              }),
            },
          },
        },
      ],
    }).compileComponents();

    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    const fixture = TestBed.createComponent(ClientScheduleFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('is in create mode with no scheduleId route param', async () => {
    const fixture = await createComponent(null);
    expect(fixture.componentInstance.isEditMode).toBeFalse();
  });

  it('posts a new schedule built from the checked days, in wire order, and navigates back to the list', async () => {
    const fixture = await createComponent(null, () => {
      schedulesApiSpy.apiClientsClientIdSchedulesPost.and.returnValue(of({ id: 'schedule-1' }));
    });

    fixture.componentInstance.form.patchValue({
      sunday: true,
      monday: true,
      thursday: true,
      startTime: '21:00',
      endTime: '07:00',
      timeZoneId: 'America/New_York',
    });

    await fixture.componentInstance.save();

    expect(schedulesApiSpy.apiClientsClientIdSchedulesPost).toHaveBeenCalledWith(
      jasmine.objectContaining({
        clientId: 'client-1',
        clientScheduleWriteRequest: jasmine.objectContaining({
          daysOfWeek: 'Sunday, Monday, Thursday',
          startTime: '21:00',
          endTime: '07:00',
          timeZoneId: 'America/New_York',
        }),
      }),
    );
    expect(notificationSpy.success).toHaveBeenCalled();
    expect(navigateSpy).toHaveBeenCalledWith(['/clients', 'client-1', 'schedules']);
  });

  it('save() rejects a schedule with no day checked, without calling the API', async () => {
    const fixture = await createComponent(null);

    fixture.componentInstance.form.patchValue({ startTime: '09:00', endTime: '17:00' });

    await fixture.componentInstance.save();

    expect(schedulesApiSpy.apiClientsClientIdSchedulesPost).not.toHaveBeenCalled();
    expect(fixture.componentInstance.errorMessage()).toContain('at least one day');
  });

  it('save() rejects an equal start and end time, without calling the API', async () => {
    const fixture = await createComponent(null);

    fixture.componentInstance.form.patchValue({
      monday: true,
      startTime: '09:00',
      endTime: '09:00',
    });

    await fixture.componentInstance.save();

    expect(schedulesApiSpy.apiClientsClientIdSchedulesPost).not.toHaveBeenCalled();
    expect(fixture.componentInstance.errorMessage()).toContain('cannot be equal');
  });

  it('loads an existing schedule in edit mode, checking the correct day boxes, and PUTs on save', async () => {
    const fixture = await createComponent('schedule-1', () => {
      schedulesApiSpy.apiClientsClientIdSchedulesIdGet.and.returnValue(
        of({
          id: 'schedule-1',
          clientId: 'client-1',
          daysOfWeek: 'Saturday',
          startTime: '08:00:00',
          endTime: '09:00:00',
          timeZoneId: 'Europe/London',
          isDisabled: false,
          comments: null,
        }),
      );
      schedulesApiSpy.apiClientsClientIdSchedulesIdPut.and.returnValue(of(undefined));
    });

    expect(fixture.componentInstance.form.controls.saturday.value).toBeTrue();
    expect(fixture.componentInstance.form.controls.sunday.value).toBeFalse();
    expect(fixture.componentInstance.form.controls.startTime.value).toBe('08:00');
    expect(fixture.componentInstance.form.controls.timeZoneId.value).toBe('Europe/London');

    await fixture.componentInstance.save();

    expect(schedulesApiSpy.apiClientsClientIdSchedulesIdPut).toHaveBeenCalledWith(
      jasmine.objectContaining({ clientId: 'client-1', id: 'schedule-1' }),
    );
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('save() does nothing while the form is invalid', async () => {
    const fixture = await createComponent(null);
    fixture.componentInstance.form.patchValue({ startTime: '', endTime: '' });

    await fixture.componentInstance.save();

    expect(schedulesApiSpy.apiClientsClientIdSchedulesPost).not.toHaveBeenCalled();
  });
});
