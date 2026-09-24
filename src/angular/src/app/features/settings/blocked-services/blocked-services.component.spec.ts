import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { BlockedServicesService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { BlockedServicesComponent } from './blocked-services.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('BlockedServicesComponent', () => {
  let apiSpy: {
    apiBlockedServicesGet: jasmine.Spy;
    apiBlockedServicesKeyPost: jasmine.Spy;
    apiBlockedServicesKeyDelete: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    apiSpy = jasmine.createSpyObj('BlockedServicesService', [
      'apiBlockedServicesGet',
      'apiBlockedServicesKeyPost',
      'apiBlockedServicesKeyDelete',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiBlockedServicesGet.and.returnValue(
      of([
        {
          key: 'tiktok',
          displayName: 'TikTok',
          description: 'Blocks TikTok.',
          subscriptionUrl: 'https://example.test/tiktok.txt',
          isEnabled: false,
          filterListId: null,
        },
      ]),
    );

    await TestBed.configureTestingModule({
      imports: [BlockedServicesComponent],
      providers: [
        { provide: BlockedServicesService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(BlockedServicesComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the catalog on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiBlockedServicesGet).toHaveBeenCalled();
    expect(fixture.componentInstance.services().length).toBe(1);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('toggle() enables a disabled entry', async () => {
    const fixture = await createComponent();
    apiSpy.apiBlockedServicesKeyPost.and.returnValue(
      of({ key: 'tiktok', displayName: 'TikTok', isEnabled: true }),
    );
    apiSpy.apiBlockedServicesGet.and.returnValue(
      of([{ key: 'tiktok', displayName: 'TikTok', isEnabled: true }]),
    );

    await fixture.componentInstance.toggle(fixture.componentInstance.services()[0]);

    expect(apiSpy.apiBlockedServicesKeyPost).toHaveBeenCalledWith(
      jasmine.objectContaining({ key: 'tiktok' }),
    );
    expect(notificationSpy.success).toHaveBeenCalledWith(jasmine.stringContaining('blocked'));
  });

  it('toggle() disables an already-enabled entry', async () => {
    const fixture = await createComponent();
    fixture.componentInstance.services.set([
      { key: 'tiktok', displayName: 'TikTok', isEnabled: true },
    ]);
    apiSpy.apiBlockedServicesKeyDelete.and.returnValue(of(undefined));
    apiSpy.apiBlockedServicesGet.and.returnValue(
      of([{ key: 'tiktok', displayName: 'TikTok', isEnabled: false }]),
    );

    await fixture.componentInstance.toggle(fixture.componentInstance.services()[0]);

    expect(apiSpy.apiBlockedServicesKeyDelete).toHaveBeenCalledWith(
      jasmine.objectContaining({ key: 'tiktok' }),
    );
    expect(notificationSpy.success).toHaveBeenCalledWith(jasmine.stringContaining('unblocked'));
  });

  it('shows an error message when a toggle fails', async () => {
    const fixture = await createComponent();
    apiSpy.apiBlockedServicesKeyPost.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500, error: 'boom' })),
    );

    await fixture.componentInstance.toggle(fixture.componentInstance.services()[0]);

    expect(fixture.componentInstance.errorMessage()).toBeTruthy();
  });

  it('forwards an optional clientId input on every load and toggle call', async () => {
    apiSpy = jasmine.createSpyObj('BlockedServicesService', [
      'apiBlockedServicesGet',
      'apiBlockedServicesKeyPost',
      'apiBlockedServicesKeyDelete',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);
    apiSpy.apiBlockedServicesGet.and.returnValue(
      of([{ key: 'tiktok', displayName: 'TikTok', isEnabled: false }]),
    );

    await TestBed.configureTestingModule({
      imports: [BlockedServicesComponent],
      providers: [
        { provide: BlockedServicesService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(BlockedServicesComponent);
    fixture.componentInstance.clientId = 'client-123';
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();

    expect(apiSpy.apiBlockedServicesGet).toHaveBeenCalledWith(
      jasmine.objectContaining({ clientId: 'client-123' }),
    );

    apiSpy.apiBlockedServicesKeyPost.and.returnValue(
      of({ key: 'tiktok', displayName: 'TikTok', isEnabled: true }),
    );
    await fixture.componentInstance.toggle(fixture.componentInstance.services()[0]);

    expect(apiSpy.apiBlockedServicesKeyPost).toHaveBeenCalledWith(
      jasmine.objectContaining({ key: 'tiktok', clientId: 'client-123' }),
    );
  });
});
