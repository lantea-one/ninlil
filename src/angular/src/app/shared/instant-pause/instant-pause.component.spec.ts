import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { ClientGroupsService, ClientsService } from '../../api-client';
import { NotificationService } from '../notification/notification.service';
import { InstantPauseComponent } from './instant-pause.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('InstantPauseComponent', () => {
  let clientsApiSpy: {
    apiClientsIdGet: jasmine.Spy;
    apiClientsIdPausePost: jasmine.Spy;
    apiClientsIdResumePost: jasmine.Spy;
  };
  let clientGroupsApiSpy: {
    apiClientGroupsIdGet: jasmine.Spy;
    apiClientGroupsIdPausePost: jasmine.Spy;
    apiClientGroupsIdResumePost: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(input: { clientId?: string; clientGroupId?: string }) {
    clientsApiSpy = jasmine.createSpyObj('ClientsService', [
      'apiClientsIdGet',
      'apiClientsIdPausePost',
      'apiClientsIdResumePost',
    ]);
    clientGroupsApiSpy = jasmine.createSpyObj('ClientGroupsService', [
      'apiClientGroupsIdGet',
      'apiClientGroupsIdPausePost',
      'apiClientGroupsIdResumePost',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    clientsApiSpy.apiClientsIdGet.and.returnValue(
      of({ id: input.clientId, pausedUntilUtc: null, isCurrentlyPaused: false }),
    );
    clientGroupsApiSpy.apiClientGroupsIdGet.and.returnValue(
      of({ id: input.clientGroupId, pausedUntilUtc: null, isCurrentlyPaused: false }),
    );

    await TestBed.configureTestingModule({
      imports: [InstantPauseComponent],
      providers: [
        { provide: ClientsService, useValue: clientsApiSpy },
        { provide: ClientGroupsService, useValue: clientGroupsApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(InstantPauseComponent);
    fixture.componentInstance.clientId = input.clientId;
    fixture.componentInstance.clientGroupId = input.clientGroupId;
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current client pause status on init', async () => {
    const fixture = await createComponent({ clientId: 'client-1' });

    expect(clientsApiSpy.apiClientsIdGet).toHaveBeenCalledWith(
      jasmine.objectContaining({ id: 'client-1' }),
    );
    expect(fixture.componentInstance.isCurrentlyPaused()).toBeFalse();
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('loads the current client group pause status on init', async () => {
    const fixture = await createComponent({ clientGroupId: 'group-1' });

    expect(clientGroupsApiSpy.apiClientGroupsIdGet).toHaveBeenCalledWith(
      jasmine.objectContaining({ id: 'group-1' }),
    );
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('pauseFor() calls the client pause endpoint with the given duration', async () => {
    const fixture = await createComponent({ clientId: 'client-1' });
    clientsApiSpy.apiClientsIdPausePost.and.returnValue(
      of({ id: 'client-1', pausedUntilUtc: '2026-09-17T22:00:00Z', isCurrentlyPaused: true }),
    );

    await fixture.componentInstance.pauseFor(15);

    expect(clientsApiSpy.apiClientsIdPausePost).toHaveBeenCalledWith(
      jasmine.objectContaining({
        id: 'client-1',
        instantPauseRequest: { durationMinutes: 15 },
      }),
    );
    expect(fixture.componentInstance.isCurrentlyPaused()).toBeTrue();
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('pauseFor() calls the client group pause endpoint with the given duration', async () => {
    const fixture = await createComponent({ clientGroupId: 'group-1' });
    clientGroupsApiSpy.apiClientGroupsIdPausePost.and.returnValue(
      of({ id: 'group-1', pausedUntilUtc: '2026-09-17T23:00:00Z', isCurrentlyPaused: true }),
    );

    await fixture.componentInstance.pauseFor(60);

    expect(clientGroupsApiSpy.apiClientGroupsIdPausePost).toHaveBeenCalledWith(
      jasmine.objectContaining({
        id: 'group-1',
        instantPauseRequest: { durationMinutes: 60 },
      }),
    );
    expect(fixture.componentInstance.isCurrentlyPaused()).toBeTrue();
  });

  it('pauseUntilTomorrowMorning() submits a positive duration in minutes', async () => {
    const fixture = await createComponent({ clientId: 'client-1' });
    clientsApiSpy.apiClientsIdPausePost.and.returnValue(
      of({ id: 'client-1', pausedUntilUtc: '2026-09-18T07:00:00Z', isCurrentlyPaused: true }),
    );

    await fixture.componentInstance.pauseUntilTomorrowMorning();

    const call = clientsApiSpy.apiClientsIdPausePost.calls.mostRecent();
    expect(call.args[0].instantPauseRequest.durationMinutes).toBeGreaterThan(0);
  });

  it('resume() calls the client resume endpoint and clears the pause', async () => {
    const fixture = await createComponent({ clientId: 'client-1' });
    fixture.componentInstance.isCurrentlyPaused.set(true);
    clientsApiSpy.apiClientsIdResumePost.and.returnValue(
      of({ id: 'client-1', pausedUntilUtc: null, isCurrentlyPaused: false }),
    );

    await fixture.componentInstance.resume();

    expect(clientsApiSpy.apiClientsIdResumePost).toHaveBeenCalledWith(
      jasmine.objectContaining({ id: 'client-1' }),
    );
    expect(fixture.componentInstance.isCurrentlyPaused()).toBeFalse();
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('resume() calls the client group resume endpoint', async () => {
    const fixture = await createComponent({ clientGroupId: 'group-1' });
    clientGroupsApiSpy.apiClientGroupsIdResumePost.and.returnValue(
      of({ id: 'group-1', pausedUntilUtc: null, isCurrentlyPaused: false }),
    );

    await fixture.componentInstance.resume();

    expect(clientGroupsApiSpy.apiClientGroupsIdResumePost).toHaveBeenCalledWith(
      jasmine.objectContaining({ id: 'group-1' }),
    );
  });

  it('shows an error message when pausing fails', async () => {
    const fixture = await createComponent({ clientId: 'client-1' });
    clientsApiSpy.apiClientsIdPausePost.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 400, error: 'boom' })),
    );

    await fixture.componentInstance.pauseFor(15);

    expect(fixture.componentInstance.errorMessage()).toBeTruthy();
  });

  it('pauseFor() ignores a non-positive duration without calling the API', async () => {
    const fixture = await createComponent({ clientId: 'client-1' });

    await fixture.componentInstance.pauseFor(0);

    expect(clientsApiSpy.apiClientsIdPausePost).not.toHaveBeenCalled();
  });
});
