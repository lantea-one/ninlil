import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ClientGroupNetworksService, ClientGroupsService } from '../../../../api-client';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { ClientGroupNetworkListComponent } from './client-group-network-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('ClientGroupNetworkListComponent', () => {
  let networksApiSpy: {
    apiClientGroupsGroupIdNetworksGet: jasmine.Spy;
    apiClientGroupsGroupIdNetworksIdDelete: jasmine.Spy;
  };
  let clientGroupsApiSpy: { apiClientGroupsIdGet: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    networksApiSpy = jasmine.createSpyObj('ClientGroupNetworksService', [
      'apiClientGroupsGroupIdNetworksGet',
      'apiClientGroupsGroupIdNetworksIdDelete',
    ]);
    clientGroupsApiSpy = jasmine.createSpyObj('ClientGroupsService', ['apiClientGroupsIdGet']);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    clientGroupsApiSpy.apiClientGroupsIdGet.and.returnValue(of({ id: 'group-1', name: 'lan' }));
    networksApiSpy.apiClientGroupsGroupIdNetworksGet.and.returnValue(
      of({
        items: [{ id: 'network-1', clientGroupId: 'group-1', cidr: '192.168.1.0/24' }],
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [ClientGroupNetworkListComponent],
      providers: [
        provideRouter([]),
        { provide: ClientGroupNetworksService, useValue: networksApiSpy },
        { provide: ClientGroupsService, useValue: clientGroupsApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ groupId: 'group-1' }) } },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ClientGroupNetworkListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads networks and the owning group name', async () => {
    const fixture = await createComponent();

    expect(networksApiSpy.apiClientGroupsGroupIdNetworksGet).toHaveBeenCalledWith({
      groupId: 'group-1',
      skip: 0,
      take: 25,
    });
    expect(fixture.componentInstance.networks().length).toBe(1);
    expect(fixture.componentInstance.groupName()).toBe('lan');
  });

  it('deleteNetwork() deletes and reloads once confirmed', async () => {
    const fixture = await createComponent();
    networksApiSpy.apiClientGroupsGroupIdNetworksIdDelete.and.returnValue(of(undefined));

    const deletePromise = fixture.componentInstance.deleteNetwork(
      fixture.componentInstance.networks()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(networksApiSpy.apiClientGroupsGroupIdNetworksIdDelete).toHaveBeenCalledWith({
      groupId: 'group-1',
      id: 'network-1',
    });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteNetwork() does nothing when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent();

    const deletePromise = fixture.componentInstance.deleteNetwork(
      fixture.componentInstance.networks()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(false);
    await deletePromise;

    expect(networksApiSpy.apiClientGroupsGroupIdNetworksIdDelete).not.toHaveBeenCalled();
  });
});
