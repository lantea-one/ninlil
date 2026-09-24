import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { DnsRewritesService, DnsRewriteTargetKindEnum } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { DnsRewriteListComponent } from './dns-rewrite-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('DnsRewriteListComponent', () => {
  let rewritesApiSpy: {
    apiDnsRewritesGet: jasmine.Spy;
    apiDnsRewritesIdPatch: jasmine.Spy;
    apiDnsRewritesIdDelete: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    rewritesApiSpy = jasmine.createSpyObj('DnsRewritesService', [
      'apiDnsRewritesGet',
      'apiDnsRewritesIdPatch',
      'apiDnsRewritesIdDelete',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    rewritesApiSpy.apiDnsRewritesGet.and.returnValue(
      of({
        items: [
          {
            id: 'rewrite-1',
            domain: 'host.example.com',
            target: '203.0.113.5',
            targetKind: DnsRewriteTargetKindEnum.A,
            isDisabled: false,
          },
        ],
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [DnsRewriteListComponent],
      providers: [
        provideRouter([]),
        { provide: DnsRewritesService, useValue: rewritesApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(DnsRewriteListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the first page of rewrites on init', async () => {
    const fixture = await createComponent();

    expect(rewritesApiSpy.apiDnsRewritesGet).toHaveBeenCalledWith({ skip: 0, take: 25 });
    expect(fixture.componentInstance.rewrites().length).toBe(1);
    expect(fixture.componentInstance.rewrites()[0].targetKind).toBe(DnsRewriteTargetKindEnum.A);
  });

  it('toggleDisabled() sends a single-field merge patch, then reloads the page', async () => {
    const fixture = await createComponent();
    rewritesApiSpy.apiDnsRewritesIdPatch.and.returnValue(of(undefined));

    await fixture.componentInstance.toggleDisabled(fixture.componentInstance.rewrites()[0]);

    expect(rewritesApiSpy.apiDnsRewritesIdPatch).toHaveBeenCalledWith({
      id: 'rewrite-1',
      body: { isDisabled: true },
    });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteRewrite() deletes and reloads the page once the confirmation dialog is accepted', async () => {
    const fixture = await createComponent();
    rewritesApiSpy.apiDnsRewritesIdDelete.and.returnValue(of(undefined));

    const deletePromise = fixture.componentInstance.deleteRewrite(
      fixture.componentInstance.rewrites()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(rewritesApiSpy.apiDnsRewritesIdDelete).toHaveBeenCalledWith({ id: 'rewrite-1' });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteRewrite() does nothing when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent();

    const deletePromise = fixture.componentInstance.deleteRewrite(
      fixture.componentInstance.rewrites()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(false);
    await deletePromise;

    expect(rewritesApiSpy.apiDnsRewritesIdDelete).not.toHaveBeenCalled();
  });

  it('shows an error notification when delete fails', async () => {
    const fixture = await createComponent();
    rewritesApiSpy.apiDnsRewritesIdDelete.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: 'in use' })),
    );

    const deletePromise = fixture.componentInstance.deleteRewrite(
      fixture.componentInstance.rewrites()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(notificationSpy.error).toHaveBeenCalledWith('in use');
  });

  it('shows an error notification when the initial load fails', async () => {
    rewritesApiSpy = jasmine.createSpyObj('DnsRewritesService', [
      'apiDnsRewritesGet',
      'apiDnsRewritesIdPatch',
      'apiDnsRewritesIdDelete',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);
    rewritesApiSpy.apiDnsRewritesGet.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500, error: 'boom' })),
    );

    await TestBed.configureTestingModule({
      imports: [DnsRewriteListComponent],
      providers: [
        provideRouter([]),
        { provide: DnsRewritesService, useValue: rewritesApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(DnsRewriteListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();

    expect(fixture.componentInstance.errorMessage()).toBe('boom');
  });
});
