import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import {
  WebhookAuthModeEnum,
  WebhookDeliveryAttemptStatusEnum,
  WebhooksService,
} from '../../../api-client';
import { Clipboard } from '@angular/cdk/clipboard';
import {
  WebhookDeliveryAttemptsComponent,
  formatPayload,
} from './webhook-delivery-attempts.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover a webhook's delivery history: loading and paging, status colors, errors, the
 * payload dialog and copy, and the pretty-print helper. TestBed with a spy-backed
 * `WebhooksService` and a stubbed route.
 */
describe('WebhookDeliveryAttemptsComponent', () => {
  let webhooksApiSpy: {
    apiWebhooksIdGet: jasmine.Spy;
    apiWebhooksIdDeliveryAttemptsGet: jasmine.Spy;
  };

  async function createComponent(subscriptionId: string | null = 'hook-1') {
    webhooksApiSpy = jasmine.createSpyObj('WebhooksService', [
      'apiWebhooksIdGet',
      'apiWebhooksIdDeliveryAttemptsGet',
    ]);

    webhooksApiSpy.apiWebhooksIdGet.and.returnValue(
      of({
        id: 'hook-1',
        name: 'zone-change-notifier',
        targetUrl: 'https://example.com/webhooks/ninlil',
        eventTypeNames: ['DnsZoneChanged'],
        authMode: WebhookAuthModeEnum.None,
        hasHmacSigningSecret: false,
        maxDeliveryAttempts: 5,
        isDisabled: false,
      }),
    );
    webhooksApiSpy.apiWebhooksIdDeliveryAttemptsGet.and.returnValue(
      of({
        items: [
          {
            id: 'attempt-1',
            subscriptionId: 'hook-1',
            subscriptionName: 'zone-change-notifier',
            eventTypeName: 'DnsZoneChanged',
            status: WebhookDeliveryAttemptStatusEnum.Succeeded,
            attemptCount: 1,
            maxAttempts: 5,
            lastAttemptedAtUtc: '2026-09-10T22:03:11+00:00',
            lastHttpStatusCode: 200,
            payloadJson: '{"zoneId":"z-1","changeKind":"Updated"}',
            completedAtUtc: '2026-09-10T22:03:12+00:00',
          },
        ],
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [WebhookDeliveryAttemptsComponent],
      providers: [
        provideRouter([]),
        { provide: WebhooksService, useValue: webhooksApiSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: convertToParamMap(subscriptionId ? { id: subscriptionId } : {}),
            },
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(WebhookDeliveryAttemptsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the subscription name and the first page of delivery attempts on init', async () => {
    const fixture = await createComponent();

    expect(webhooksApiSpy.apiWebhooksIdGet).toHaveBeenCalledWith({ id: 'hook-1' });
    expect(webhooksApiSpy.apiWebhooksIdDeliveryAttemptsGet).toHaveBeenCalledWith({
      id: 'hook-1',
      skip: 0,
      take: 25,
    });
    expect(fixture.componentInstance.subscription()?.name).toBe('zone-change-notifier');
    expect(fixture.componentInstance.attempts().length).toBe(1);
    expect(fixture.componentInstance.totalCount()).toBe(1);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('onPageChange() requests the correctly offset skip/take, per the chosen page size', async () => {
    const fixture = await createComponent();

    await fixture.componentInstance.onPageChange({ pageIndex: 1, pageSize: 10 });

    expect(webhooksApiSpy.apiWebhooksIdDeliveryAttemptsGet).toHaveBeenCalledWith({
      id: 'hook-1',
      skip: 10,
      take: 10,
    });
  });

  it('statusBadgeColor() maps every real delivery status onto its own severity color', async () => {
    const fixture = await createComponent();

    expect(
      fixture.componentInstance.statusBadgeColor(WebhookDeliveryAttemptStatusEnum.Succeeded),
    ).toBe('success');
    expect(
      fixture.componentInstance.statusBadgeColor(WebhookDeliveryAttemptStatusEnum.DeadLettered),
    ).toBe('danger');
    expect(
      fixture.componentInstance.statusBadgeColor(WebhookDeliveryAttemptStatusEnum.Pending),
    ).toBe('warning');
  });

  it('shows an error message when loading the delivery history fails', async () => {
    webhooksApiSpy = jasmine.createSpyObj('WebhooksService', [
      'apiWebhooksIdGet',
      'apiWebhooksIdDeliveryAttemptsGet',
    ]);
    webhooksApiSpy.apiWebhooksIdGet.and.returnValue(of(null));
    webhooksApiSpy.apiWebhooksIdDeliveryAttemptsGet.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 404, error: 'not found' })),
    );

    await TestBed.configureTestingModule({
      imports: [WebhookDeliveryAttemptsComponent],
      providers: [
        provideRouter([]),
        { provide: WebhooksService, useValue: webhooksApiSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ id: 'hook-1' }) } },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(WebhookDeliveryAttemptsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();

    expect(fixture.componentInstance.errorMessage()).toBe('not found');
  });
  it("shows a delivery's request body, pretty-printed, in the payload dialog", async () => {
    const fixture = await createComponent();
    const component = fixture.componentInstance;

    component.showPayload(component.attempts()[0]);
    fixture.detectChanges();

    expect(component.formattedPayload()).toBe(
      JSON.stringify({ zoneId: 'z-1', changeKind: 'Updated' }, null, 2),
    );
    const pre: HTMLElement | null = document.querySelector('pre.payload');
    expect(pre?.textContent).toContain('"changeKind": "Updated"');

    component.closePayload();
    expect(component.payloadAttempt()).toBeNull();
  });

  it('copies the payload exactly as sent, not the pretty-printed form', async () => {
    const fixture = await createComponent();
    const component = fixture.componentInstance;
    const copySpy = spyOn(TestBed.inject(Clipboard), 'copy').and.returnValue(true);

    component.showPayload(component.attempts()[0]);
    component.copyPayload();

    expect(copySpy).toHaveBeenCalledWith('{"zoneId":"z-1","changeKind":"Updated"}');
    expect(component.payloadCopied()).toBeTrue();
  });

  it('shows when each delivery completed', async () => {
    const fixture = await createComponent();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(fixture.componentInstance.displayedColumns).toContain('completedAtUtc');
    expect(text).toContain('Completed');
  });

  describe('formatPayload', () => {
    it('shows text that is not valid JSON unchanged', () => {
      expect(formatPayload('not json')).toBe('not json');
    });

    it('shows nothing for a missing payload', () => {
      expect(formatPayload(null)).toBe('');
    });
  });
});
