import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { ForwardersService, UpstreamForwarderProtocolEnum } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { ForwarderListComponent } from './forwarder-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('ForwarderListComponent', () => {
  let forwardersApiSpy: {
    apiForwardersGet: jasmine.Spy;
    apiForwardersIdPatch: jasmine.Spy;
    apiForwardersIdDelete: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    forwardersApiSpy = jasmine.createSpyObj('ForwardersService', [
      'apiForwardersGet',
      'apiForwardersIdPatch',
      'apiForwardersIdDelete',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    forwardersApiSpy.apiForwardersGet.and.returnValue(
      of({
        items: [
          {
            id: 'forwarder-1',
            name: 'cloudflare',
            definitionString: '1.1.1.1',
            protocol: UpstreamForwarderProtocolEnum.Plain,
            sortOrder: 0,
            isDisabled: false,
          },
        ],
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [ForwarderListComponent],
      providers: [
        provideRouter([]),
        { provide: ForwardersService, useValue: forwardersApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ForwarderListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the first page of forwarders on init', async () => {
    const fixture = await createComponent();

    expect(forwardersApiSpy.apiForwardersGet).toHaveBeenCalledWith({ skip: 0, take: 25 });
    expect(fixture.componentInstance.forwarders().length).toBe(1);
    expect(fixture.componentInstance.forwarders()[0].protocol).toBe(
      UpstreamForwarderProtocolEnum.Plain,
    );
  });

  it('toggleDisabled() sends a single-field merge patch, then reloads the page', async () => {
    const fixture = await createComponent();
    forwardersApiSpy.apiForwardersIdPatch.and.returnValue(of(undefined));

    await fixture.componentInstance.toggleDisabled(fixture.componentInstance.forwarders()[0]);

    expect(forwardersApiSpy.apiForwardersIdPatch).toHaveBeenCalledWith({
      id: 'forwarder-1',
      body: { isDisabled: true },
    });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteForwarder() deletes and reloads the page once the confirmation dialog is accepted', async () => {
    const fixture = await createComponent();
    forwardersApiSpy.apiForwardersIdDelete.and.returnValue(of(undefined));

    const deletePromise = fixture.componentInstance.deleteForwarder(
      fixture.componentInstance.forwarders()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(forwardersApiSpy.apiForwardersIdDelete).toHaveBeenCalledWith({ id: 'forwarder-1' });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteForwarder() does nothing when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent();

    const deletePromise = fixture.componentInstance.deleteForwarder(
      fixture.componentInstance.forwarders()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(false);
    await deletePromise;

    expect(forwardersApiSpy.apiForwardersIdDelete).not.toHaveBeenCalled();
  });

  it('shows an error notification when delete fails', async () => {
    const fixture = await createComponent();
    forwardersApiSpy.apiForwardersIdDelete.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: 'in use' })),
    );

    const deletePromise = fixture.componentInstance.deleteForwarder(
      fixture.componentInstance.forwarders()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(notificationSpy.error).toHaveBeenCalledWith('in use');
  });
});
