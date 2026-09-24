import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ClientGroupListAssignmentsService, FilterListsService } from '../../../../api-client';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { ClientGroupListAssignmentFormComponent } from './client-group-list-assignment-form.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('ClientGroupListAssignmentFormComponent', () => {
  let listAssignmentsApiSpy: {
    apiClientGroupsGroupIdListAssignmentsGet: jasmine.Spy;
    apiClientGroupsGroupIdListAssignmentsPost: jasmine.Spy;
  };
  let filterListsApiSpy: { apiFilterListsGet: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let navigateSpy: jasmine.Spy;

  async function createComponent(configureApis?: () => void) {
    listAssignmentsApiSpy = jasmine.createSpyObj('ClientGroupListAssignmentsService', [
      'apiClientGroupsGroupIdListAssignmentsGet',
      'apiClientGroupsGroupIdListAssignmentsPost',
    ]);
    filterListsApiSpy = jasmine.createSpyObj('FilterListsService', ['apiFilterListsGet']);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    filterListsApiSpy.apiFilterListsGet.and.returnValue(
      of({
        items: [
          { id: 'list-1', name: 'threat-intel' },
          { id: 'list-2', name: 'family-safe' },
        ],
        skip: 0,
        take: 500,
        totalCount: 2,
      }),
    );
    listAssignmentsApiSpy.apiClientGroupsGroupIdListAssignmentsGet.and.returnValue(
      of({
        items: [{ id: 'assignment-1', clientGroupId: 'group-1', listId: 'list-1' }],
        skip: 0,
        take: 500,
        totalCount: 1,
      }),
    );

    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [ClientGroupListAssignmentFormComponent],
      providers: [
        provideRouter([]),
        { provide: ClientGroupListAssignmentsService, useValue: listAssignmentsApiSpy },
        { provide: FilterListsService, useValue: filterListsApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ groupId: 'group-1' }) } },
        },
      ],
    }).compileComponents();

    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    const fixture = TestBed.createComponent(ClientGroupListAssignmentFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('excludes an already-assigned filter list from its own available options', async () => {
    const fixture = await createComponent();

    expect(fixture.componentInstance.availableLists().length).toBe(1);
    expect(fixture.componentInstance.availableLists()[0].id).toBe('list-2');
  });

  it('save() POSTs the selected listId and navigates back to the assignment list', async () => {
    const fixture = await createComponent(() => {
      listAssignmentsApiSpy.apiClientGroupsGroupIdListAssignmentsPost.and.returnValue(
        of({ id: 'assignment-2' }),
      );
    });
    fixture.componentInstance.form.patchValue({ listId: 'list-2' });

    await fixture.componentInstance.save();

    expect(listAssignmentsApiSpy.apiClientGroupsGroupIdListAssignmentsPost).toHaveBeenCalledWith({
      groupId: 'group-1',
      clientGroupListAssignmentWriteRequest: { listId: 'list-2' },
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/client-groups', 'group-1', 'list-assignments']);
  });

  it('save() does nothing while the form is invalid', async () => {
    const fixture = await createComponent();

    await fixture.componentInstance.save();

    expect(listAssignmentsApiSpy.apiClientGroupsGroupIdListAssignmentsPost).not.toHaveBeenCalled();
  });
});
