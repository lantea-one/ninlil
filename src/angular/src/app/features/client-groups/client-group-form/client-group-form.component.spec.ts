import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { ClientGroupsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { ClientGroupFormComponent } from './client-group-form.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('ClientGroupFormComponent', () => {
  let clientGroupsApiSpy: {
    apiClientGroupsIdGet: jasmine.Spy;
    apiClientGroupsPost: jasmine.Spy;
    apiClientGroupsIdPut: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let navigateSpy: jasmine.Spy;

  async function createComponent(groupId: string | null, configureApis?: () => void) {
    clientGroupsApiSpy = jasmine.createSpyObj('ClientGroupsService', [
      'apiClientGroupsIdGet',
      'apiClientGroupsPost',
      'apiClientGroupsIdPut',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [ClientGroupFormComponent],
      providers: [
        provideRouter([]),
        { provide: ClientGroupsService, useValue: clientGroupsApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap(groupId ? { groupId } : {}) } },
        },
      ],
    }).compileComponents();

    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    const fixture = TestBed.createComponent(ClientGroupFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('create mode: does not load any group', async () => {
    const fixture = await createComponent(null);

    expect(fixture.componentInstance.isEditMode).toBeFalse();
    expect(clientGroupsApiSpy.apiClientGroupsIdGet).not.toHaveBeenCalled();
  });

  it('edit mode: loads the existing group and populates every field', async () => {
    const fixture = await createComponent('group-1', () => {
      clientGroupsApiSpy.apiClientGroupsIdGet.and.returnValue(
        of({ id: 'group-1', name: 'lan', isDefault: true, isDisabled: false }),
      );
    });

    expect(fixture.componentInstance.form.controls.name.value).toBe('lan');
    expect(fixture.componentInstance.form.controls.isDefault.value).toBeTrue();
  });

  it('save() in create mode POSTs the built request and navigates back to the list', async () => {
    const fixture = await createComponent(null, () => {
      clientGroupsApiSpy.apiClientGroupsPost.and.returnValue(of({ id: 'new-group' }));
    });
    fixture.componentInstance.form.patchValue({ name: 'lan' });

    await fixture.componentInstance.save();

    expect(clientGroupsApiSpy.apiClientGroupsPost).toHaveBeenCalledWith({
      clientGroupWriteRequest: { name: 'lan', isDefault: false, isDisabled: false },
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/client-groups']);
  });

  it('save() in edit mode PUTs the built request', async () => {
    const fixture = await createComponent('group-1', () => {
      clientGroupsApiSpy.apiClientGroupsIdGet.and.returnValue(
        of({ id: 'group-1', name: 'lan', isDefault: false, isDisabled: false }),
      );
      clientGroupsApiSpy.apiClientGroupsIdPut.and.returnValue(of(undefined));
    });

    await fixture.componentInstance.save();

    expect(clientGroupsApiSpy.apiClientGroupsIdPut).toHaveBeenCalledWith({
      id: 'group-1',
      clientGroupWriteRequest: { name: 'lan', isDefault: false, isDisabled: false },
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/client-groups']);
  });

  it('shows an error message and does not navigate when saving fails', async () => {
    const fixture = await createComponent(null, () => {
      clientGroupsApiSpy.apiClientGroupsPost.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 409,
              error: 'A client group named lan already exists.',
            }),
        ),
      );
    });
    fixture.componentInstance.form.patchValue({ name: 'lan' });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe(
      'A client group named lan already exists.',
    );
    expect(navigateSpy).not.toHaveBeenCalled();
  });

  it('save() does nothing while the form is invalid', async () => {
    const fixture = await createComponent(null);
    fixture.componentInstance.form.patchValue({ name: '' });

    await fixture.componentInstance.save();

    expect(clientGroupsApiSpy.apiClientGroupsPost).not.toHaveBeenCalled();
  });
});
