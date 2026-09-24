import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { WebhookAuthModeEnum, WebhooksService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { WebhookListComponent } from './webhook-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('WebhookListComponent', () => {
  let webhooksApiSpy: {
    apiWebhooksGet: jasmine.Spy;
    apiWebhooksIdPatch: jasmine.Spy;
    apiWebhooksIdDelete: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    webhooksApiSpy = jasmine.createSpyObj('WebhooksService', [
      'apiWebhooksGet',
      'apiWebhooksIdPatch',
      'apiWebhooksIdDelete',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    webhooksApiSpy.apiWebhooksGet.and.returnValue(
      of({
        items: [
          {
            id: 'hook-1',
            name: 'alert-zone-changes',
            targetUrl: 'https://example.com/webhooks/ninlil',
            eventTypeNames: ['DnsZoneChanged'],
            authMode: WebhookAuthModeEnum.None,
            hasHmacSigningSecret: false,
            maxDeliveryAttempts: 5,
            isDisabled: false,
          },
        ],
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [WebhookListComponent],
      providers: [
        provideRouter([]),
        { provide: WebhooksService, useValue: webhooksApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(WebhookListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the first page of subscriptions on init', async () => {
    const fixture = await createComponent();

    expect(webhooksApiSpy.apiWebhooksGet).toHaveBeenCalledWith({ skip: 0, take: 25 });
    expect(fixture.componentInstance.subscriptions().length).toBe(1);
    expect(fixture.componentInstance.totalCount()).toBe(1);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('onPageChange() requests the correctly offset skip/take, per the chosen page size', async () => {
    const fixture = await createComponent();

    await fixture.componentInstance.onPageChange({ pageIndex: 1, pageSize: 10 });

    expect(webhooksApiSpy.apiWebhooksGet).toHaveBeenCalledWith({ skip: 10, take: 10 });
  });

  it('toggleDisabled() sends a single-field merge patch, then reloads the page', async () => {
    const fixture = await createComponent();
    webhooksApiSpy.apiWebhooksIdPatch.and.returnValue(of(undefined));

    await fixture.componentInstance.toggleDisabled(fixture.componentInstance.subscriptions()[0]);

    expect(webhooksApiSpy.apiWebhooksIdPatch).toHaveBeenCalledWith({
      id: 'hook-1',
      body: { isDisabled: true },
    });
    expect(webhooksApiSpy.apiWebhooksGet).toHaveBeenCalledTimes(2);
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteSubscription() does nothing when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent();

    const deletePromise = fixture.componentInstance.deleteSubscription(
      fixture.componentInstance.subscriptions()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(false);
    await deletePromise;

    expect(webhooksApiSpy.apiWebhooksIdDelete).not.toHaveBeenCalled();
  });

  it('deleteSubscription() deletes and reloads the page once the confirmation dialog is accepted', async () => {
    const fixture = await createComponent();
    webhooksApiSpy.apiWebhooksIdDelete.and.returnValue(of(undefined));

    const deletePromise = fixture.componentInstance.deleteSubscription(
      fixture.componentInstance.subscriptions()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(webhooksApiSpy.apiWebhooksIdDelete).toHaveBeenCalledWith({ id: 'hook-1' });
    expect(notificationSpy.success).toHaveBeenCalled();
    expect(webhooksApiSpy.apiWebhooksGet).toHaveBeenCalledTimes(2);
  });

  it('shows an error notification when delete fails', async () => {
    const fixture = await createComponent();
    webhooksApiSpy.apiWebhooksIdDelete.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: 'in use' })),
    );

    const deletePromise = fixture.componentInstance.deleteSubscription(
      fixture.componentInstance.subscriptions()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(notificationSpy.error).toHaveBeenCalledWith('in use');
  });
});
