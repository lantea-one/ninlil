import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { DhcpServiceControlService, DhcpServiceKindEnum } from '../../../../api-client';
import { AuthService } from '../../../../core/auth/auth.service';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { DhcpServiceListComponent } from './dhcp-service-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('DhcpServiceListComponent', () => {
  let dhcpServicesApiSpy: {
    apiDhcpServicesGet: jasmine.Spy;
    apiDhcpServicesKindStartPost: jasmine.Spy;
    apiDhcpServicesKindStopPost: jasmine.Spy;
  };
  let authServiceSpy: { hasPermission: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  const sampleServices = [
    { kind: DhcpServiceKindEnum.Dhcpv4, enabled: true, isRunning: true },
    { kind: DhcpServiceKindEnum.Dhcpv6, enabled: false, isRunning: false },
    { kind: DhcpServiceKindEnum.RouterAdvertisement, enabled: false, isRunning: true },
    { kind: DhcpServiceKindEnum.Tftp, enabled: true, isRunning: false },
  ];

  async function createComponent(canManage = true, loadShouldFail = false) {
    dhcpServicesApiSpy = jasmine.createSpyObj('DhcpServiceControlService', [
      'apiDhcpServicesGet',
      'apiDhcpServicesKindStartPost',
      'apiDhcpServicesKindStopPost',
    ]);
    authServiceSpy = jasmine.createSpyObj('AuthService', ['hasPermission']);
    authServiceSpy.hasPermission.and.returnValue(canManage);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    dhcpServicesApiSpy.apiDhcpServicesGet.and.returnValue(
      loadShouldFail ? throwError(() => new Error('boom')) : of(sampleServices),
    );

    await TestBed.configureTestingModule({
      imports: [DhcpServiceListComponent],
      providers: [
        { provide: DhcpServiceControlService, useValue: dhcpServicesApiSpy },
        { provide: AuthService, useValue: authServiceSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(DhcpServiceListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads all four DHCP-family services', async () => {
    const fixture = await createComponent();

    expect(dhcpServicesApiSpy.apiDhcpServicesGet).toHaveBeenCalled();
    expect(fixture.componentInstance.services().length).toBe(4);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('checks dhcpservices:manage via AuthService.hasPermission to decide whether lifecycle buttons render', async () => {
    await createComponent(false);

    expect(authServiceSpy.hasPermission).toHaveBeenCalledWith('dhcpservices:manage');
  });

  it('shows an error message when the initial load fails', async () => {
    const fixture = await createComponent(true, true);

    expect(fixture.componentInstance.errorMessage()).toBeTruthy();
  });

  it('invoke("stop", ...) opens a confirmation naming the real DHCPv4 lease-traffic risk before calling the API', async () => {
    const fixture = await createComponent();
    dhcpServicesApiSpy.apiDhcpServicesKindStopPost.and.returnValue(
      of({ kind: DhcpServiceKindEnum.Dhcpv4, enabled: true, isRunning: false }),
    );

    const invokePromise = fixture.componentInstance.invoke('stop', sampleServices[0]);
    const dialogData = fixture.componentInstance.confirmDialogData();
    expect(dialogData?.title).toContain('DHCPv4');
    expect(dialogData?.message).toContain('lease');
    fixture.componentInstance.onConfirmDialogResolved(true);
    await invokePromise;

    expect(dhcpServicesApiSpy.apiDhcpServicesKindStopPost).toHaveBeenCalledWith({
      kind: DhcpServiceKindEnum.Dhcpv4,
    });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('invoke(...) does nothing further when the confirmation dialog is cancelled', async () => {
    const fixture = await createComponent();

    const invokePromise = fixture.componentInstance.invoke('stop', sampleServices[0]);
    fixture.componentInstance.onConfirmDialogResolved(false);
    await invokePromise;

    expect(dhcpServicesApiSpy.apiDhcpServicesKindStopPost).not.toHaveBeenCalled();
  });

  it('invoke("start", ...) calls the start endpoint and replaces the service status in place', async () => {
    const fixture = await createComponent();
    dhcpServicesApiSpy.apiDhcpServicesKindStartPost.and.returnValue(
      of({ kind: DhcpServiceKindEnum.Dhcpv6, enabled: false, isRunning: true }),
    );

    const invokePromise = fixture.componentInstance.invoke('start', sampleServices[1]);
    fixture.componentInstance.onConfirmDialogResolved(true);
    await invokePromise;

    expect(dhcpServicesApiSpy.apiDhcpServicesKindStartPost).toHaveBeenCalledWith({
      kind: DhcpServiceKindEnum.Dhcpv6,
    });
    const updated = fixture.componentInstance
      .services()
      .find((service) => service.kind === DhcpServiceKindEnum.Dhcpv6);
    expect(updated?.isRunning).toBeTrue();
  });

  it('invoke("stop", ...) reports an error via NotificationService when the call fails', async () => {
    const fixture = await createComponent();
    dhcpServicesApiSpy.apiDhcpServicesKindStopPost.and.returnValue(
      throwError(() => new Error('boom')),
    );

    const invokePromise = fixture.componentInstance.invoke('stop', sampleServices[2]);
    fixture.componentInstance.onConfirmDialogResolved(true);
    await invokePromise;

    expect(notificationSpy.error).toHaveBeenCalled();
  });
});
