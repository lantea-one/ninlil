import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ClientGroupNetworksService } from '../../../../api-client';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { ClientGroupNetworkFormComponent } from './client-group-network-form.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('ClientGroupNetworkFormComponent', () => {
  let networksApiSpy: {
    apiClientGroupsGroupIdNetworksIdGet: jasmine.Spy;
    apiClientGroupsGroupIdNetworksPost: jasmine.Spy;
    apiClientGroupsGroupIdNetworksIdPut: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let navigateSpy: jasmine.Spy;

  async function createComponent(networkId: string | null, configureApis?: () => void) {
    networksApiSpy = jasmine.createSpyObj('ClientGroupNetworksService', [
      'apiClientGroupsGroupIdNetworksIdGet',
      'apiClientGroupsGroupIdNetworksPost',
      'apiClientGroupsGroupIdNetworksIdPut',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [ClientGroupNetworkFormComponent],
      providers: [
        provideRouter([]),
        { provide: ClientGroupNetworksService, useValue: networksApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: convertToParamMap({
                groupId: 'group-1',
                ...(networkId ? { networkId } : {}),
              }),
            },
          },
        },
      ],
    }).compileComponents();

    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    const fixture = TestBed.createComponent(ClientGroupNetworkFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('edit mode: loads the existing network', async () => {
    const fixture = await createComponent('network-1', () => {
      networksApiSpy.apiClientGroupsGroupIdNetworksIdGet.and.returnValue(
        of({ id: 'network-1', cidr: '192.168.1.0/24' }),
      );
    });

    expect(fixture.componentInstance.form.controls.cidr.value).toBe('192.168.1.0/24');
  });

  it('save() in create mode POSTs the built request and navigates back to the network list', async () => {
    const fixture = await createComponent(null, () => {
      networksApiSpy.apiClientGroupsGroupIdNetworksPost.and.returnValue(of({ id: 'network-1' }));
    });
    fixture.componentInstance.form.patchValue({ cidr: '10.0.0.0/8' });

    await fixture.componentInstance.save();

    expect(networksApiSpy.apiClientGroupsGroupIdNetworksPost).toHaveBeenCalledWith({
      groupId: 'group-1',
      clientGroupNetworkWriteRequest: { cidr: '10.0.0.0/8' },
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/client-groups', 'group-1', 'networks']);
  });

  it('save() in edit mode PUTs the built request', async () => {
    const fixture = await createComponent('network-1', () => {
      networksApiSpy.apiClientGroupsGroupIdNetworksIdGet.and.returnValue(
        of({ id: 'network-1', cidr: '10.0.0.0/8' }),
      );
      networksApiSpy.apiClientGroupsGroupIdNetworksIdPut.and.returnValue(of(undefined));
    });

    await fixture.componentInstance.save();

    expect(networksApiSpy.apiClientGroupsGroupIdNetworksIdPut).toHaveBeenCalledWith({
      groupId: 'group-1',
      id: 'network-1',
      clientGroupNetworkWriteRequest: { cidr: '10.0.0.0/8' },
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/client-groups', 'group-1', 'networks']);
  });

  it('save() does nothing while the form is invalid', async () => {
    const fixture = await createComponent(null);
    fixture.componentInstance.form.patchValue({ cidr: '' });

    await fixture.componentInstance.save();

    expect(networksApiSpy.apiClientGroupsGroupIdNetworksPost).not.toHaveBeenCalled();
  });

  it('edit mode: a ClientID row selects the ClientID match kind, not CIDR', async () => {
    const fixture = await createComponent('network-1', () => {
      networksApiSpy.apiClientGroupsGroupIdNetworksIdGet.and.returnValue(
        of({ id: 'network-1', clientId: 'laptop-1' }),
      );
    });

    expect(fixture.componentInstance.matchKind()).toBe('clientId');
    expect(fixture.componentInstance.form.controls.clientId.value).toBe('laptop-1');
  });

  it('save() in create mode with the ClientID match kind sends only clientId, not cidr', async () => {
    const fixture = await createComponent(null, () => {
      networksApiSpy.apiClientGroupsGroupIdNetworksPost.and.returnValue(of({ id: 'network-1' }));
    });
    fixture.componentInstance.form.controls.matchKind.setValue('clientId');
    fixture.componentInstance.form.controls.clientId.setValue('laptop-1');

    await fixture.componentInstance.save();

    expect(networksApiSpy.apiClientGroupsGroupIdNetworksPost).toHaveBeenCalledWith({
      groupId: 'group-1',
      clientGroupNetworkWriteRequest: { clientId: 'laptop-1' },
    });
  });

  it('switching match kind clears the field the previous kind used', async () => {
    const fixture = await createComponent(null);
    fixture.componentInstance.form.controls.cidr.setValue('10.0.0.0/8');

    fixture.componentInstance.form.controls.matchKind.setValue('clientId');

    expect(fixture.componentInstance.form.controls.cidr.value).toBe('');
    expect(fixture.componentInstance.form.controls.cidr.hasError('required')).toBeFalse();
  });
});
