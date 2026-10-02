import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ManagedServiceCategoryEnum, ServicesService } from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';
import { NotificationService } from '../../../shared/notification/notification.service';
import { ServiceListComponent } from './service-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the Services page: grouping by category, the `services:manage` check, load
 * errors, and the confirmation, start and failure paths. TestBed with spy-backed
 * `ServicesService`, `AuthService` and `NotificationService`.
 */
describe('ServiceListComponent', () => {
  let servicesApiSpy: {
    apiServicesGet: jasmine.Spy;
    apiServicesNameStartPost: jasmine.Spy;
    apiServicesNameStopPost: jasmine.Spy;
    apiServicesNameRestartPost: jasmine.Spy;
  };
  let authServiceSpy: { hasPermission: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  const sampleServices = [
    { name: 'plain-udp', category: ManagedServiceCategoryEnum.Transport, isRunning: true },
    { name: 'doh', category: ManagedServiceCategoryEnum.Transport, isRunning: false },
    {
      name: 'query-log-cleanup',
      category: ManagedServiceCategoryEnum.Background,
      isRunning: true,
    },
  ];

  async function createComponent(canManage = true, loadShouldFail = false) {
    servicesApiSpy = jasmine.createSpyObj('ServicesService', [
      'apiServicesGet',
      'apiServicesNameStartPost',
      'apiServicesNameStopPost',
      'apiServicesNameRestartPost',
    ]);
    authServiceSpy = jasmine.createSpyObj('AuthService', ['hasPermission']);
    authServiceSpy.hasPermission.and.returnValue(canManage);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    servicesApiSpy.apiServicesGet.and.returnValue(
      loadShouldFail ? throwError(() => new Error('boom')) : of(sampleServices),
    );

    await TestBed.configureTestingModule({
      imports: [ServiceListComponent],
      providers: [
        { provide: ServicesService, useValue: servicesApiSpy },
        { provide: AuthService, useValue: authServiceSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ServiceListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads and groups the registered services by category', async () => {
    const fixture = await createComponent();

    expect(servicesApiSpy.apiServicesGet).toHaveBeenCalled();
    expect(fixture.componentInstance.transportServices().length).toBe(2);
    expect(fixture.componentInstance.backgroundServices().length).toBe(1);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('checks services:manage via AuthService.hasPermission to decide whether lifecycle buttons render', async () => {
    await createComponent(false);

    expect(authServiceSpy.hasPermission).toHaveBeenCalledWith('services:manage');
  });

  it('shows an error message when the initial load fails', async () => {
    const fixture = await createComponent(true, true);

    expect(fixture.componentInstance.errorMessage()).toBeTruthy();
  });

  it('invoke("stop", ...) opens a confirmation naming the real transport risk before calling the API', async () => {
    const fixture = await createComponent();
    servicesApiSpy.apiServicesNameStopPost.and.returnValue(
      of({ name: 'plain-udp', category: ManagedServiceCategoryEnum.Transport, isRunning: false }),
    );

    const invokePromise = fixture.componentInstance.invoke('stop', sampleServices[0]);
    const dialogData = fixture.componentInstance.confirmDialogData();
    expect(dialogData?.title).toContain('plain-udp');
    expect(dialogData?.message).toContain('client DNS queries');
    fixture.componentInstance.onConfirmDialogResolved(true);
    await invokePromise;

    expect(servicesApiSpy.apiServicesNameStopPost).toHaveBeenCalledWith({ name: 'plain-udp' });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('invoke(...) does nothing further when the confirmation dialog is cancelled', async () => {
    const fixture = await createComponent();

    const invokePromise = fixture.componentInstance.invoke('stop', sampleServices[0]);
    fixture.componentInstance.onConfirmDialogResolved(false);
    await invokePromise;

    expect(servicesApiSpy.apiServicesNameStopPost).not.toHaveBeenCalled();
  });

  it('invoke("start", ...) calls the start endpoint and replaces the service summary in place', async () => {
    const fixture = await createComponent();
    servicesApiSpy.apiServicesNameStartPost.and.returnValue(
      of({ name: 'doh', category: ManagedServiceCategoryEnum.Transport, isRunning: true }),
    );

    const invokePromise = fixture.componentInstance.invoke('start', sampleServices[1]);
    fixture.componentInstance.onConfirmDialogResolved(true);
    await invokePromise;

    expect(servicesApiSpy.apiServicesNameStartPost).toHaveBeenCalledWith({ name: 'doh' });
    const updated = fixture.componentInstance
      .transportServices()
      .find((service) => service.name === 'doh');
    expect(updated?.isRunning).toBeTrue();
  });

  it('invoke("restart", ...) reports an error via NotificationService when the call fails', async () => {
    const fixture = await createComponent();
    servicesApiSpy.apiServicesNameRestartPost.and.returnValue(throwError(() => new Error('boom')));

    const invokePromise = fixture.componentInstance.invoke('restart', sampleServices[2]);
    fixture.componentInstance.onConfirmDialogResolved(true);
    await invokePromise;

    expect(notificationSpy.error).toHaveBeenCalled();
  });
});
