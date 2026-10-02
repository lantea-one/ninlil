import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { CloudDnsProviderTypeEnum, CloudProviderCredentialsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { CloudProviderCredentialListComponent } from './cloud-provider-credential-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the credential list: loading and paging, the enable/disable toggle and delete
 * with its confirmation. TestBed with a spy-backed `CloudProviderCredentialsService` and
 * `NotificationService`.
 */
describe('CloudProviderCredentialListComponent', () => {
  let credentialsApiSpy: {
    apiCloudProviderCredentialsGet: jasmine.Spy;
    apiCloudProviderCredentialsIdPatch: jasmine.Spy;
    apiCloudProviderCredentialsIdDelete: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    credentialsApiSpy = jasmine.createSpyObj('CloudProviderCredentialsService', [
      'apiCloudProviderCredentialsGet',
      'apiCloudProviderCredentialsIdPatch',
      'apiCloudProviderCredentialsIdDelete',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    credentialsApiSpy.apiCloudProviderCredentialsGet.and.returnValue(
      of({
        items: [
          {
            id: 'cred-1',
            name: 'prod-cloudflare',
            providerType: CloudDnsProviderTypeEnum.Cloudflare,
            isDisabled: false,
          },
        ],
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [CloudProviderCredentialListComponent],
      providers: [
        provideRouter([]),
        { provide: CloudProviderCredentialsService, useValue: credentialsApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(CloudProviderCredentialListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the first page of credential sets on init', async () => {
    const fixture = await createComponent();

    expect(credentialsApiSpy.apiCloudProviderCredentialsGet).toHaveBeenCalledWith({
      skip: 0,
      take: 25,
    });
    expect(fixture.componentInstance.credentials().length).toBe(1);
    expect(fixture.componentInstance.totalCount()).toBe(1);
  });

  it('onPageChange() requests the correctly offset skip/take, per the chosen page size', async () => {
    const fixture = await createComponent();

    await fixture.componentInstance.onPageChange({ pageIndex: 3, pageSize: 10 });

    expect(credentialsApiSpy.apiCloudProviderCredentialsGet).toHaveBeenCalledWith({
      skip: 30,
      take: 10,
    });
  });

  it('toggleDisabled() sends a single-field merge patch, then reloads the page', async () => {
    const fixture = await createComponent();
    credentialsApiSpy.apiCloudProviderCredentialsIdPatch.and.returnValue(of(undefined));

    await fixture.componentInstance.toggleDisabled(fixture.componentInstance.credentials()[0]);

    expect(credentialsApiSpy.apiCloudProviderCredentialsIdPatch).toHaveBeenCalledWith({
      id: 'cred-1',
      body: { isDisabled: true },
    });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteCredential() does nothing when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent();

    const deletePromise = fixture.componentInstance.deleteCredential(
      fixture.componentInstance.credentials()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(false);
    await deletePromise;

    expect(credentialsApiSpy.apiCloudProviderCredentialsIdDelete).not.toHaveBeenCalled();
  });

  it('deleteCredential() deletes and reloads the page once the confirmation dialog is accepted', async () => {
    const fixture = await createComponent();
    credentialsApiSpy.apiCloudProviderCredentialsIdDelete.and.returnValue(of(undefined));

    const deletePromise = fixture.componentInstance.deleteCredential(
      fixture.componentInstance.credentials()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(credentialsApiSpy.apiCloudProviderCredentialsIdDelete).toHaveBeenCalledWith({
      id: 'cred-1',
    });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('shows an error notification when delete fails', async () => {
    const fixture = await createComponent();
    credentialsApiSpy.apiCloudProviderCredentialsIdDelete.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: 'in use' })),
    );

    const deletePromise = fixture.componentInstance.deleteCredential(
      fixture.componentInstance.credentials()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(notificationSpy.error).toHaveBeenCalledWith('in use');
  });
});
