import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import {
  ClientListAssignmentsService,
  ClientsService,
  FilterListsService,
} from '../../../../api-client';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { ClientListAssignmentListComponent } from './client-list-assignment-list.component';
import { AuthService } from '../../../../core/auth/auth.service';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover a client's list assignments page: list names (or ids without
 * `filterlists:read`) and removal with the confirmation accepted or dismissed. TestBed with
 * spy-backed API services and a fake `AuthService` that denies chosen permissions.
 */
describe('ClientListAssignmentListComponent', () => {
  // The permissions the test user lacks. It holds all others, so a test sees the page as an
  // administrator would unless it denies something first.
  let deniedPermissions: string[] = [];
  beforeEach(() => (deniedPermissions = []));

  let listAssignmentsApiSpy: {
    apiClientsClientIdListAssignmentsGet: jasmine.Spy;
    apiClientsClientIdListAssignmentsIdDelete: jasmine.Spy;
  };
  let clientsApiSpy: { apiClientsIdGet: jasmine.Spy };
  let filterListsApiSpy: { apiFilterListsGet: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    listAssignmentsApiSpy = jasmine.createSpyObj('ClientListAssignmentsService', [
      'apiClientsClientIdListAssignmentsGet',
      'apiClientsClientIdListAssignmentsIdDelete',
    ]);
    clientsApiSpy = jasmine.createSpyObj('ClientsService', ['apiClientsIdGet']);
    filterListsApiSpy = jasmine.createSpyObj('FilterListsService', ['apiFilterListsGet']);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    clientsApiSpy.apiClientsIdGet.and.returnValue(
      of({
        id: 'client-1',
        addresses: [
          { id: 'address-1', clientIdentifier: 'AA:BB:CC:DD:EE:FF', protocolVersion: 'IPv4' },
        ],
        displayName: 'Living Room TV',
      }),
    );
    filterListsApiSpy.apiFilterListsGet.and.returnValue(
      of({ items: [{ id: 'list-1', name: 'threat-intel' }], skip: 0, take: 500, totalCount: 1 }),
    );
    listAssignmentsApiSpy.apiClientsClientIdListAssignmentsGet.and.returnValue(
      of({
        items: [{ id: 'assignment-1', clientId: 'client-1', listId: 'list-1' }],
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [ClientListAssignmentListComponent],
      providers: [
        provideRouter([]),
        { provide: ClientListAssignmentsService, useValue: listAssignmentsApiSpy },
        { provide: ClientsService, useValue: clientsApiSpy },
        { provide: FilterListsService, useValue: filterListsApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ clientId: 'client-1' }) } },
        },
      ],
    }).compileComponents();

    spyOn(TestBed.inject(AuthService), 'hasPermission').and.callFake(
      (permission: string) => !deniedPermissions.includes(permission),
    );
    const fixture = TestBed.createComponent(ClientListAssignmentListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('without filterlists:read, still lists the assignments, by list ID, and says why', async () => {
    deniedPermissions = ['filterlists:read'];
    const fixture = await createComponent();

    expect(filterListsApiSpy.apiFilterListsGet).not.toHaveBeenCalled();
    expect(fixture.componentInstance.errorMessage()).toBeNull();
    expect(fixture.componentInstance.rows()[0].listName).toBe('list-1');
    expect(
      fixture.nativeElement.querySelector('[data-testid="list-names-unavailable"]'),
    ).not.toBeNull();
  });

  it("loads assignments and resolves each one's own filter list name", async () => {
    const fixture = await createComponent();

    expect(fixture.componentInstance.rows().length).toBe(1);
    expect(fixture.componentInstance.rows()[0].listName).toBe('threat-intel');
  });

  it('deleteAssignment() removes and reloads once confirmed', async () => {
    const fixture = await createComponent();
    listAssignmentsApiSpy.apiClientsClientIdListAssignmentsIdDelete.and.returnValue(of(undefined));

    const deletePromise = fixture.componentInstance.deleteAssignment(
      fixture.componentInstance.rows()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(listAssignmentsApiSpy.apiClientsClientIdListAssignmentsIdDelete).toHaveBeenCalledWith({
      clientId: 'client-1',
      id: 'assignment-1',
    });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteAssignment() does nothing when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent();

    const deletePromise = fixture.componentInstance.deleteAssignment(
      fixture.componentInstance.rows()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(false);
    await deletePromise;

    expect(listAssignmentsApiSpy.apiClientsClientIdListAssignmentsIdDelete).not.toHaveBeenCalled();
  });
});
