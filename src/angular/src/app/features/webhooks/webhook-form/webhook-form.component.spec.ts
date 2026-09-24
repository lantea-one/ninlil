import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { WebhookAuthModeEnum, WebhooksService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { WebhookFormComponent } from './webhook-form.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('WebhookFormComponent', () => {
  let webhooksApiSpy: {
    apiWebhooksIdGet: jasmine.Spy;
    apiWebhooksPost: jasmine.Spy;
    apiWebhooksIdPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let navigateSpy: jasmine.Spy;

  async function createComponent(subscriptionId: string | null, configureApis?: () => void) {
    webhooksApiSpy = jasmine.createSpyObj('WebhooksService', [
      'apiWebhooksIdGet',
      'apiWebhooksPost',
      'apiWebhooksIdPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [WebhookFormComponent],
      providers: [
        provideRouter([]),
        { provide: WebhooksService, useValue: webhooksApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: convertToParamMap(subscriptionId ? { id: subscriptionId } : {}) },
          },
        },
      ],
    }).compileComponents();

    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    const fixture = TestBed.createComponent(WebhookFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('create mode: has no owning subscription to load, and defaults the auth mode to None', async () => {
    const fixture = await createComponent(null);

    expect(fixture.componentInstance.isEditMode).toBeFalse();
    expect(webhooksApiSpy.apiWebhooksIdGet).not.toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.authMode.value).toBe(WebhookAuthModeEnum.None);
  });

  it('restricts the event type picker to the 7 curated, actually-delivering event types', async () => {
    const fixture = await createComponent(null);

    expect(fixture.componentInstance.eventTypeOptions).toEqual([
      'DnsZoneChanged',
      'DnsCacheEvicted',
      'DnssecZoneResigningCompleted',
      'AcmeCertificateRenewalSucceeded',
      'AcmeCertificateRenewalFailed',
      'CloudDnsZoneSyncFailed',
      'TrustAnchorRollover',
    ]);
  });

  it('save() in create mode with Bearer auth and HMAC signing shows the reveal-once dialog with both secrets, without navigating yet', async () => {
    const fixture = await createComponent(null, () => {
      webhooksApiSpy.apiWebhooksPost.and.returnValue(
        of({
          subscription: { id: 'hook-1', name: 'alert-zone-changes' },
          authCredentials: { BearerToken: 'c29tZS10b2tlbg==' },
          hmacSigningSecret: 'c29tZS1obWFjLXNlY3JldA==',
        }),
      );
    });

    fixture.componentInstance.form.patchValue({
      name: 'alert-zone-changes',
      targetUrl: 'https://example.com/webhooks/ninlil',
      eventTypeNames: ['DnsZoneChanged'],
      authMode: WebhookAuthModeEnum.Bearer,
      bearerToken: 'plaintext-token',
      enableHmacSigning: true,
    });

    await fixture.componentInstance.save();

    expect(webhooksApiSpy.apiWebhooksPost).toHaveBeenCalledWith({
      webhookSubscriptionCreateRequest: jasmine.objectContaining({
        name: 'alert-zone-changes',
        authMode: WebhookAuthModeEnum.Bearer,
        authCredentials: { BearerToken: 'plaintext-token' },
        enableHmacSigning: true,
      }),
    });
    expect(fixture.componentInstance.revealDialogVisible()).toBeTrue();
    expect(fixture.componentInstance.revealDialogData()).toEqual(
      jasmine.objectContaining({
        secrets: [
          { label: 'Bearer token', value: 'c29tZS10b2tlbg==' },
          { label: 'HMAC signing secret', value: 'c29tZS1obWFjLXNlY3JldA==' },
        ],
      }),
    );
    expect(navigateSpy).not.toHaveBeenCalled();
  });

  it('onSecretDialogClosed() hides the dialog and navigates to the list, once dismissed', async () => {
    const fixture = await createComponent(null, () => {
      webhooksApiSpy.apiWebhooksPost.and.returnValue(
        of({
          subscription: { id: 'hook-1', name: 'alert-zone-changes' },
          authCredentials: { BearerToken: 'c29tZS10b2tlbg==' },
        }),
      );
    });
    fixture.componentInstance.form.patchValue({
      name: 'alert-zone-changes',
      targetUrl: 'https://example.com/webhooks/ninlil',
      eventTypeNames: ['DnsZoneChanged'],
      authMode: WebhookAuthModeEnum.Bearer,
      bearerToken: 'plaintext-token',
    });
    await fixture.componentInstance.save();

    fixture.componentInstance.onSecretDialogClosed();

    expect(fixture.componentInstance.revealDialogVisible()).toBeFalse();
    expect(navigateSpy).toHaveBeenCalledWith(['/webhooks']);
  });

  it('save() in create mode with no secrets to reveal (auth None, signing off) navigates to the list immediately', async () => {
    const fixture = await createComponent(null, () => {
      webhooksApiSpy.apiWebhooksPost.and.returnValue(
        of({ subscription: { id: 'hook-1', name: 'alert-zone-changes' } }),
      );
    });
    fixture.componentInstance.form.patchValue({
      name: 'alert-zone-changes',
      targetUrl: 'https://example.com/webhooks/ninlil',
      eventTypeNames: ['DnsZoneChanged'],
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.revealDialogVisible()).toBeFalse();
    expect(navigateSpy).toHaveBeenCalledWith(['/webhooks']);
  });

  it('save() in create mode requires every credential field to be filled in before submitting', async () => {
    const fixture = await createComponent(null);

    fixture.componentInstance.form.patchValue({
      name: 'alert-zone-changes',
      targetUrl: 'https://example.com/webhooks/ninlil',
      eventTypeNames: ['DnsZoneChanged'],
      authMode: WebhookAuthModeEnum.Bearer,
      bearerToken: '',
    });

    await fixture.componentInstance.save();

    expect(webhooksApiSpy.apiWebhooksPost).not.toHaveBeenCalled();
    expect(fixture.componentInstance.errorMessage()).toContain('every credential field');
  });

  it('edit mode: loads the existing subscription, disables credential fields, and never re-opens the reveal dialog on save', async () => {
    const fixture = await createComponent('hook-1', () => {
      webhooksApiSpy.apiWebhooksIdGet.and.returnValue(
        of({
          id: 'hook-1',
          name: 'alert-zone-changes',
          targetUrl: 'https://example.com/webhooks/ninlil',
          eventTypeNames: ['DnsZoneChanged'],
          authMode: WebhookAuthModeEnum.None,
          hasHmacSigningSecret: true,
          maxDeliveryAttempts: 5,
          isDisabled: false,
        }),
      );
      webhooksApiSpy.apiWebhooksIdPatch.and.returnValue(of(undefined));
    });

    expect(fixture.componentInstance.isEditMode).toBeTrue();
    expect(fixture.componentInstance.hasHmacSigningSecret()).toBeTrue();
    expect(fixture.componentInstance.form.controls.authMode.disabled).toBeTrue();

    fixture.componentInstance.form.patchValue({ comments: 'rotated soon' });
    await fixture.componentInstance.save();

    expect(webhooksApiSpy.apiWebhooksIdPatch).toHaveBeenCalledWith({
      id: 'hook-1',
      body: jasmine.objectContaining({
        name: 'alert-zone-changes',
        comments: 'rotated soon',
      }),
    });
    const patchBody = webhooksApiSpy.apiWebhooksIdPatch.calls.mostRecent().args[0].body;
    expect(patchBody.authMode).toBeUndefined();
    expect(patchBody.authCredentials).toBeUndefined();
    expect(fixture.componentInstance.revealDialogVisible()).toBeFalse();
    expect(navigateSpy).toHaveBeenCalledWith(['/webhooks']);
  });

  it('edit mode: rotating credentials includes authMode/authCredentials in the same patch', async () => {
    const fixture = await createComponent('hook-1', () => {
      webhooksApiSpy.apiWebhooksIdGet.and.returnValue(
        of({
          id: 'hook-1',
          name: 'alert-zone-changes',
          targetUrl: 'https://example.com/webhooks/ninlil',
          eventTypeNames: ['DnsZoneChanged'],
          authMode: WebhookAuthModeEnum.None,
          hasHmacSigningSecret: false,
          maxDeliveryAttempts: 5,
          isDisabled: false,
        }),
      );
      webhooksApiSpy.apiWebhooksIdPatch.and.returnValue(of(undefined));
    });

    fixture.componentInstance.startRotatingCredentials();
    fixture.componentInstance.form.patchValue({
      authMode: WebhookAuthModeEnum.Basic,
      username: 'operator',
      password: 'hunter2',
    });

    await fixture.componentInstance.save();

    expect(webhooksApiSpy.apiWebhooksIdPatch).toHaveBeenCalledWith({
      id: 'hook-1',
      body: jasmine.objectContaining({
        authMode: WebhookAuthModeEnum.Basic,
        authCredentials: { Username: 'operator', Password: 'hunter2' },
      }),
    });
  });

  it('shows an error message and does not navigate when saving fails', async () => {
    const fixture = await createComponent(null, () => {
      webhooksApiSpy.apiWebhooksPost.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 409,
              error: "A webhook subscription named 'x' already exists.",
            }),
        ),
      );
    });
    fixture.componentInstance.form.patchValue({
      name: 'alert-zone-changes',
      targetUrl: 'https://example.com/webhooks/ninlil',
      eventTypeNames: ['DnsZoneChanged'],
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe(
      "A webhook subscription named 'x' already exists.",
    );
    expect(navigateSpy).not.toHaveBeenCalled();
  });

  it('save() does nothing while the form is invalid', async () => {
    const fixture = await createComponent(null);
    fixture.componentInstance.form.patchValue({ name: '' });

    await fixture.componentInstance.save();

    expect(webhooksApiSpy.apiWebhooksPost).not.toHaveBeenCalled();
  });
});
