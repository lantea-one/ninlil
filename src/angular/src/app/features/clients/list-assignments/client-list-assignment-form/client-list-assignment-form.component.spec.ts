import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ClientListAssignmentsService, FilterListsService } from '../../../../api-client';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { ClientListAssignmentFormComponent } from './client-list-assignment-form.component';
import { AuthService } from '../../../../core/auth/auth.service';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('ClientListAssignmentFormComponent', () => {
  // The permissions the signed-in test user lacks; it holds every other one, so each test sees
  // the page as an administrator would unless it denies something first.
  let deniedPermissions: string[] = [];
  beforeEach(() => (deniedPermissions = []));

  let listAssignmentsApiSpy: {
    apiClientsClientIdListAssignmentsGet: jasmine.Spy;
    apiClientsClientIdListAssignmentsPost: jasmine.Spy;
  };
  let filterListsApiSpy: { apiFilterListsGet: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let navigateSpy: jasmine.Spy;

  async function createComponent(configureApis?: () => void) {
    listAssignmentsApiSpy = jasmine.createSpyObj('ClientListAssignmentsService', [
      'apiClientsClientIdListAssignmentsGet',
      'apiClientsClientIdListAssignmentsPost',
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
    listAssignmentsApiSpy.apiClientsClientIdListAssignmentsGet.and.returnValue(
      of({
        items: [{ id: 'assignment-1', clientId: 'client-1', listId: 'list-1' }],
        skip: 0,
        take: 500,
        totalCount: 1,
      }),
    );

    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [ClientListAssignmentFormComponent],
      providers: [
        provideRouter([]),
        { provide: ClientListAssignmentsService, useValue: listAssignmentsApiSpy },
        { provide: FilterListsService, useValue: filterListsApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ clientId: 'client-1' }) } },
        },
      ],
    }).compileComponents();

    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    spyOn(TestBed.inject(AuthService), 'hasPermission').and.callFake(
      (permission: string) => !deniedPermissions.includes(permission),
    );
    const fixture = TestBed.createComponent(ClientListAssignmentFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('without filterlists:read, explains that there is nothing to pick instead of failing', async () => {
    deniedPermissions = ['filterlists:read'];
    const fixture = await createComponent();

    expect(filterListsApiSpy.apiFilterListsGet).not.toHaveBeenCalled();
    expect(fixture.componentInstance.errorMessage()).toBeNull();
    expect(fixture.componentInstance.listsUnavailable()).toBeTrue();
    expect(fixture.nativeElement.querySelector('[data-testid="lists-unavailable"]')).not.toBeNull();
  });

  it('excludes an already-assigned filter list from its own available options', async () => {
    const fixture = await createComponent();

    expect(fixture.componentInstance.availableLists().length).toBe(1);
    expect(fixture.componentInstance.availableLists()[0].id).toBe('list-2');
  });

  it('save() POSTs the selected listId and navigates back to the assignment list', async () => {
    const fixture = await createComponent(() => {
      listAssignmentsApiSpy.apiClientsClientIdListAssignmentsPost.and.returnValue(
        of({ id: 'assignment-2' }),
      );
    });
    fixture.componentInstance.form.patchValue({ listId: 'list-2' });

    await fixture.componentInstance.save();

    expect(listAssignmentsApiSpy.apiClientsClientIdListAssignmentsPost).toHaveBeenCalledWith({
      clientId: 'client-1',
      clientListAssignmentWriteRequest: { listId: 'list-2' },
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/clients', 'client-1', 'list-assignments']);
  });

  it('save() does nothing while the form is invalid', async () => {
    const fixture = await createComponent();

    await fixture.componentInstance.save();

    expect(listAssignmentsApiSpy.apiClientsClientIdListAssignmentsPost).not.toHaveBeenCalled();
  });
});
