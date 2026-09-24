import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { CloudDnsProviderTypeEnum, CloudProviderCredentialsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { CloudProviderCredentialFormComponent } from './cloud-provider-credential-form.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('CloudProviderCredentialFormComponent', () => {
  let credentialsApiSpy: {
    apiCloudProviderCredentialsIdGet: jasmine.Spy;
    apiCloudProviderCredentialsPost: jasmine.Spy;
    apiCloudProviderCredentialsIdPut: jasmine.Spy;
    apiCloudProviderCredentialsIdPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let navigateSpy: jasmine.Spy;

  /** See `zone-form.component.spec.ts`'s own identical helper for why `configureApis` must run before construction. */
  async function createComponent(credentialId: string | null, configureApis?: () => void) {
    credentialsApiSpy = jasmine.createSpyObj('CloudProviderCredentialsService', [
      'apiCloudProviderCredentialsIdGet',
      'apiCloudProviderCredentialsPost',
      'apiCloudProviderCredentialsIdPut',
      'apiCloudProviderCredentialsIdPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [CloudProviderCredentialFormComponent],
      providers: [
        provideRouter([]),
        { provide: CloudProviderCredentialsService, useValue: credentialsApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: convertToParamMap(credentialId ? { id: credentialId } : {}) },
          },
        },
      ],
    }).compileComponents();

    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    const fixture = TestBed.createComponent(CloudProviderCredentialFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('create mode: credential fields start editable, defaulting to Cloudflare', async () => {
    const fixture = await createComponent(null);

    expect(fixture.componentInstance.isEditMode).toBeFalse();
    expect(fixture.componentInstance.credentialsEditable()).toBeTrue();
    expect(fixture.componentInstance.selectedProviderType()).toBe(
      CloudDnsProviderTypeEnum.Cloudflare,
    );
    expect(fixture.componentInstance.credentialFields()).toEqual([
      { key: 'ApiToken', label: 'API token' },
    ]);
  });

  it('edit mode: loads the existing credential set and starts with credentials non-editable', async () => {
    const fixture = await createComponent('cred-1', () => {
      credentialsApiSpy.apiCloudProviderCredentialsIdGet.and.returnValue(
        of({
          id: 'cred-1',
          name: 'prod-cloudflare',
          providerType: CloudDnsProviderTypeEnum.Cloudflare,
          isDisabled: false,
        }),
      );
    });

    expect(fixture.componentInstance.isEditMode).toBeTrue();
    expect(fixture.componentInstance.credentialsEditable()).toBeFalse();
    expect(fixture.componentInstance.form.controls.providerType.disabled).toBeTrue();
    expect(fixture.componentInstance.form.controls.name.value).toBe('prod-cloudflare');
  });

  it('startRotatingCredentials() re-enables the provider-type select and every credential field', async () => {
    const fixture = await createComponent('cred-1', () => {
      credentialsApiSpy.apiCloudProviderCredentialsIdGet.and.returnValue(
        of({ id: 'cred-1', name: 'prod', providerType: CloudDnsProviderTypeEnum.Route53 }),
      );
    });

    fixture.componentInstance.startRotatingCredentials();

    expect(fixture.componentInstance.credentialsEditable()).toBeTrue();
    expect(fixture.componentInstance.form.controls.providerType.disabled).toBeFalse();
    expect(fixture.componentInstance.form.controls.accessKeyId.disabled).toBeFalse();
    expect(fixture.componentInstance.form.controls.secretAccessKey.disabled).toBeFalse();
  });

  it('save() in create mode POSTs a full credentials payload and shows the reveal-once dialog without navigating yet', async () => {
    const fixture = await createComponent(null, () => {
      credentialsApiSpy.apiCloudProviderCredentialsPost.and.returnValue(
        of({ credential: { id: 'cred-1' }, secrets: { ApiToken: 'cf-token-value' } }),
      );
    });
    fixture.componentInstance.form.patchValue({
      name: 'prod-cloudflare',
      apiToken: 'cf-token-value',
    });

    await fixture.componentInstance.save();

    expect(credentialsApiSpy.apiCloudProviderCredentialsPost).toHaveBeenCalledWith({
      cloudProviderCredentialCreateRequest: jasmine.objectContaining({
        name: 'prod-cloudflare',
        providerType: CloudDnsProviderTypeEnum.Cloudflare,
        credentials: { ApiToken: 'cf-token-value' },
      }),
    });
    expect(fixture.componentInstance.revealDialogVisible()).toBeTrue();
    expect(fixture.componentInstance.revealDialogData()).toEqual(
      jasmine.objectContaining({
        secrets: [{ label: 'API token', value: 'cf-token-value' }],
      }),
    );
    expect(navigateSpy).not.toHaveBeenCalled();
  });

  it('onSecretDialogClosed() hides the dialog and navigates to the list, once dismissed', async () => {
    const fixture = await createComponent(null, () => {
      credentialsApiSpy.apiCloudProviderCredentialsPost.and.returnValue(
        of({ credential: { id: 'cred-1' }, secrets: { ApiToken: 'cf-token-value' } }),
      );
    });
    fixture.componentInstance.form.patchValue({
      name: 'prod-cloudflare',
      apiToken: 'cf-token-value',
    });
    await fixture.componentInstance.save();

    fixture.componentInstance.onSecretDialogClosed();

    expect(fixture.componentInstance.revealDialogVisible()).toBeFalse();
    expect(navigateSpy).toHaveBeenCalledWith(['/cloud-provider-credentials']);
  });

  it('save() in create mode rejects an incomplete credentials payload before ever calling the API', async () => {
    const fixture = await createComponent(null);
    fixture.componentInstance.form.patchValue({
      name: 'aws',
      providerType: CloudDnsProviderTypeEnum.Route53,
      accessKeyId: 'AKIAEXAMPLE',
      secretAccessKey: '',
    });

    await fixture.componentInstance.save();

    expect(credentialsApiSpy.apiCloudProviderCredentialsPost).not.toHaveBeenCalled();
    expect(fixture.componentInstance.errorMessage()).toContain('every credential field');
  });

  it('save() in edit mode, metadata-only, PATCHes without providerType/credentials, never reveals anything, and navigates immediately', async () => {
    const fixture = await createComponent('cred-1', () => {
      credentialsApiSpy.apiCloudProviderCredentialsIdGet.and.returnValue(
        of({
          id: 'cred-1',
          name: 'prod',
          providerType: CloudDnsProviderTypeEnum.Cloudflare,
          isDisabled: false,
        }),
      );
      credentialsApiSpy.apiCloudProviderCredentialsIdPatch.and.returnValue(of(undefined));
    });
    fixture.componentInstance.form.patchValue({ isDisabled: true, comments: 'paused' });

    await fixture.componentInstance.save();

    expect(credentialsApiSpy.apiCloudProviderCredentialsIdPatch).toHaveBeenCalledWith({
      id: 'cred-1',
      body: { name: 'prod', isDisabled: true, comments: 'paused' },
    });
    expect(credentialsApiSpy.apiCloudProviderCredentialsIdPut).not.toHaveBeenCalled();
    expect(fixture.componentInstance.revealDialogVisible()).toBeFalse();
    expect(navigateSpy).toHaveBeenCalledWith(['/cloud-provider-credentials']);
  });

  it('save() in edit mode, while rotating, PUTs the full replacement including the newly typed credentials, without revealing anything, and navigates immediately', async () => {
    const fixture = await createComponent('cred-1', () => {
      credentialsApiSpy.apiCloudProviderCredentialsIdGet.and.returnValue(
        of({
          id: 'cred-1',
          name: 'prod',
          providerType: CloudDnsProviderTypeEnum.Cloudflare,
          isDisabled: false,
        }),
      );
      credentialsApiSpy.apiCloudProviderCredentialsIdPut.and.returnValue(of(undefined));
    });
    fixture.componentInstance.startRotatingCredentials();
    fixture.componentInstance.form.patchValue({ apiToken: 'brand-new-token' });

    await fixture.componentInstance.save();

    expect(credentialsApiSpy.apiCloudProviderCredentialsIdPut).toHaveBeenCalledWith({
      id: 'cred-1',
      cloudProviderCredentialWriteRequest: jasmine.objectContaining({
        credentials: { ApiToken: 'brand-new-token' },
      }),
    });
    expect(fixture.componentInstance.revealDialogVisible()).toBeFalse();
    expect(navigateSpy).toHaveBeenCalledWith(['/cloud-provider-credentials']);
  });

  it('shows an error message and does not navigate when saving fails', async () => {
    const fixture = await createComponent(null, () => {
      credentialsApiSpy.apiCloudProviderCredentialsPost.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 409,
              error: "A cloud provider credential set named 'x' already exists.",
            }),
        ),
      );
    });
    fixture.componentInstance.form.patchValue({ name: 'prod', apiToken: 'value' });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe(
      "A cloud provider credential set named 'x' already exists.",
    );
    expect(navigateSpy).not.toHaveBeenCalled();
  });
});
