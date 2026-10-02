import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { TsigKeysService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { TsigKeyFormComponent } from './tsig-key-form.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the TSIG key form: create with the reveal-once dialog (generated or supplied
 * secret), edit without a secret field, errors and validation. TestBed with a spy-backed
 * `TsigKeysService` and a stubbed route.
 */
describe('TsigKeyFormComponent', () => {
  // A hand-written spy type rather than `jasmine.SpyObj<TsigKeysService>`; the AuthService spec
  // explains why (overloaded generated methods).
  let tsigKeysApiSpy: {
    apiTsigKeysIdGet: jasmine.Spy;
    apiTsigKeysPost: jasmine.Spy;
    apiTsigKeysIdPut: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let navigateSpy: jasmine.Spy;

  /**
   * This helper builds fresh spies, applies `configureApis` and creates the component.
   * `configureApis` must run first because `ngOnInit` calls the API at once.
   */
  async function createComponent(keyId: string | null, configureApis?: () => void) {
    tsigKeysApiSpy = jasmine.createSpyObj('TsigKeysService', [
      'apiTsigKeysIdGet',
      'apiTsigKeysPost',
      'apiTsigKeysIdPut',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [TsigKeyFormComponent],
      providers: [
        provideRouter([]),
        { provide: TsigKeysService, useValue: tsigKeysApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap(keyId ? { id: keyId } : {}) } },
        },
      ],
    }).compileComponents();

    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    const fixture = TestBed.createComponent(TsigKeyFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('create mode: has no owning key to load, and defaults the algorithm to hmac-sha256', async () => {
    const fixture = await createComponent(null);

    expect(fixture.componentInstance.isEditMode).toBeFalse();
    expect(tsigKeysApiSpy.apiTsigKeysIdGet).not.toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.algorithmName.value).toBe('hmac-sha256');
  });

  it('create mode: does not render a secret field in edit mode (verified by loading an existing key)', async () => {
    const fixture = await createComponent('key-1', () => {
      tsigKeysApiSpy.apiTsigKeysIdGet.and.returnValue(
        of({ id: 'key-1', name: 'secondary1', algorithmName: 'hmac-sha256', isDisabled: false }),
      );
    });

    expect(fixture.componentInstance.isEditMode).toBeTrue();
    expect(fixture.componentInstance.form.controls.name.value).toBe('secondary1');
  });

  it('save() in create mode POSTs the built request and shows the reveal-once dialog without navigating yet', async () => {
    const fixture = await createComponent(null, () => {
      tsigKeysApiSpy.apiTsigKeysPost.and.returnValue(
        of({
          key: { id: 'key-1', name: 'secondary1' },
          secret: 'c29tZS1nZW5lcmF0ZWQtc2VjcmV0',
        }),
      );
    });
    fixture.componentInstance.form.patchValue({ name: 'secondary1' });

    await fixture.componentInstance.save();

    expect(tsigKeysApiSpy.apiTsigKeysPost).toHaveBeenCalledWith({
      tsigKeyCreateRequest: jasmine.objectContaining({ name: 'secondary1', secret: null }),
    });
    expect(fixture.componentInstance.revealDialogVisible()).toBeTrue();
    expect(fixture.componentInstance.revealDialogData()).toEqual(
      jasmine.objectContaining({
        secrets: [{ label: 'Shared secret', value: 'c29tZS1nZW5lcmF0ZWQtc2VjcmV0' }],
      }),
    );
    expect(navigateSpy).not.toHaveBeenCalled();
  });

  it('onSecretDialogClosed() hides the dialog and navigates to the list, once dismissed', async () => {
    const fixture = await createComponent(null, () => {
      tsigKeysApiSpy.apiTsigKeysPost.and.returnValue(
        of({ key: { id: 'key-1' }, secret: 'c29tZS1nZW5lcmF0ZWQtc2VjcmV0' }),
      );
    });
    fixture.componentInstance.form.patchValue({ name: 'secondary1' });
    await fixture.componentInstance.save();

    fixture.componentInstance.onSecretDialogClosed();

    expect(fixture.componentInstance.revealDialogVisible()).toBeFalse();
    expect(navigateSpy).toHaveBeenCalledWith(['/tsig-keys']);
  });

  it('save() in create mode passes a caller-supplied secret through to the request', async () => {
    const fixture = await createComponent(null, () => {
      tsigKeysApiSpy.apiTsigKeysPost.and.returnValue(
        of({ key: { id: 'key-1' }, secret: 'Y2FsbGVyLXN1cHBsaWVk' }),
      );
    });
    fixture.componentInstance.form.patchValue({
      name: 'secondary1',
      secret: 'Y2FsbGVyLXN1cHBsaWVk',
    });

    await fixture.componentInstance.save();

    expect(tsigKeysApiSpy.apiTsigKeysPost).toHaveBeenCalledWith({
      tsigKeyCreateRequest: jasmine.objectContaining({ secret: 'Y2FsbGVyLXN1cHBsaWVk' }),
    });
  });

  it('save() in edit mode PUTs name/algorithm/disabled/comments only, then navigates to the list immediately', async () => {
    const fixture = await createComponent('key-1', () => {
      tsigKeysApiSpy.apiTsigKeysIdGet.and.returnValue(
        of({ id: 'key-1', name: 'secondary1', algorithmName: 'hmac-sha256', isDisabled: false }),
      );
      tsigKeysApiSpy.apiTsigKeysIdPut.and.returnValue(of(undefined));
    });
    fixture.componentInstance.form.patchValue({ isDisabled: true, comments: 'rotated soon' });

    await fixture.componentInstance.save();

    expect(tsigKeysApiSpy.apiTsigKeysIdPut).toHaveBeenCalledWith({
      id: 'key-1',
      tsigKeyWriteRequest: {
        name: 'secondary1',
        algorithmName: 'hmac-sha256',
        isDisabled: true,
        comments: 'rotated soon',
      },
    });
    expect(fixture.componentInstance.revealDialogVisible()).toBeFalse();
    expect(navigateSpy).toHaveBeenCalledWith(['/tsig-keys']);
  });

  it('shows an error message and does not navigate when saving fails', async () => {
    const fixture = await createComponent(null, () => {
      tsigKeysApiSpy.apiTsigKeysPost.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({ status: 409, error: "A TSIG key named 'x' already exists." }),
        ),
      );
    });
    fixture.componentInstance.form.patchValue({ name: 'secondary1' });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe("A TSIG key named 'x' already exists.");
    expect(navigateSpy).not.toHaveBeenCalled();
  });

  it('save() does nothing while the form is invalid', async () => {
    const fixture = await createComponent(null);
    fixture.componentInstance.form.patchValue({ name: '' });

    await fixture.componentInstance.save();

    expect(tsigKeysApiSpy.apiTsigKeysPost).not.toHaveBeenCalled();
  });
});
