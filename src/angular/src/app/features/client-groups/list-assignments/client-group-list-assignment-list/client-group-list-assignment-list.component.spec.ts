import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import {
  ClientGroupListAssignmentsService,
  ClientGroupsService,
  FilterListsService,
} from '../../../../api-client';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { ClientGroupListAssignmentListComponent } from './client-group-list-assignment-list.component';
import { AuthService } from '../../../../core/auth/auth.service';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('ClientGroupListAssignmentListComponent', () => {
  // The permissions the signed-in test user lacks; it holds every other one, so each test sees
  // the page as an administrator would unless it denies something first.
  let deniedPermissions: string[] = [];
  beforeEach(() => (deniedPermissions = []));

  let listAssignmentsApiSpy: {
    apiClientGroupsGroupIdListAssignmentsGet: jasmine.Spy;
    apiClientGroupsGroupIdListAssignmentsIdDelete: jasmine.Spy;
  };
  let clientGroupsApiSpy: { apiClientGroupsIdGet: jasmine.Spy };
  let filterListsApiSpy: { apiFilterListsGet: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    listAssignmentsApiSpy = jasmine.createSpyObj('ClientGroupListAssignmentsService', [
      'apiClientGroupsGroupIdListAssignmentsGet',
      'apiClientGroupsGroupIdListAssignmentsIdDelete',
    ]);
    clientGroupsApiSpy = jasmine.createSpyObj('ClientGroupsService', ['apiClientGroupsIdGet']);
    filterListsApiSpy = jasmine.createSpyObj('FilterListsService', ['apiFilterListsGet']);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    clientGroupsApiSpy.apiClientGroupsIdGet.and.returnValue(of({ id: 'group-1', name: 'lan' }));
    filterListsApiSpy.apiFilterListsGet.and.returnValue(
      of({ items: [{ id: 'list-1', name: 'threat-intel' }], skip: 0, take: 500, totalCount: 1 }),
    );
    listAssignmentsApiSpy.apiClientGroupsGroupIdListAssignmentsGet.and.returnValue(
      of({
        items: [{ id: 'assignment-1', clientGroupId: 'group-1', listId: 'list-1' }],
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [ClientGroupListAssignmentListComponent],
      providers: [
        provideRouter([]),
        { provide: ClientGroupListAssignmentsService, useValue: listAssignmentsApiSpy },
        { provide: ClientGroupsService, useValue: clientGroupsApiSpy },
        { provide: FilterListsService, useValue: filterListsApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ groupId: 'group-1' }) } },
        },
      ],
    }).compileComponents();

    spyOn(TestBed.inject(AuthService), 'hasPermission').and.callFake(
      (permission: string) => !deniedPermissions.includes(permission),
    );
    const fixture = TestBed.createComponent(ClientGroupListAssignmentListComponent);
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
    listAssignmentsApiSpy.apiClientGroupsGroupIdListAssignmentsIdDelete.and.returnValue(
      of(undefined),
    );

    const deletePromise = fixture.componentInstance.deleteAssignment(
      fixture.componentInstance.rows()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(
      listAssignmentsApiSpy.apiClientGroupsGroupIdListAssignmentsIdDelete,
    ).toHaveBeenCalledWith({ groupId: 'group-1', id: 'assignment-1' });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteAssignment() does nothing when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent();

    const deletePromise = fixture.componentInstance.deleteAssignment(
      fixture.componentInstance.rows()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(false);
    await deletePromise;

    expect(
      listAssignmentsApiSpy.apiClientGroupsGroupIdListAssignmentsIdDelete,
    ).not.toHaveBeenCalled();
  });
});
