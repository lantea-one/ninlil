import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { DnsRewritesService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { DnsRewriteFormComponent } from './dns-rewrite-form.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the rewrite form: create and edit, saving, load and save errors and
 * validation. TestBed with a spy-backed `DnsRewritesService` and a stubbed route.
 */
describe('DnsRewriteFormComponent', () => {
  let rewritesApiSpy: {
    apiDnsRewritesIdGet: jasmine.Spy;
    apiDnsRewritesPost: jasmine.Spy;
    apiDnsRewritesIdPut: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let navigateSpy: jasmine.Spy;

  async function createComponent(rewriteId: string | null, configureApis?: () => void) {
    rewritesApiSpy = jasmine.createSpyObj('DnsRewritesService', [
      'apiDnsRewritesIdGet',
      'apiDnsRewritesPost',
      'apiDnsRewritesIdPut',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [DnsRewriteFormComponent],
      providers: [
        provideRouter([]),
        { provide: DnsRewritesService, useValue: rewritesApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: convertToParamMap(rewriteId ? { id: rewriteId } : {}) },
          },
        },
      ],
    }).compileComponents();

    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    const fixture = TestBed.createComponent(DnsRewriteFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('create mode: does not load any rewrite', async () => {
    const fixture = await createComponent(null);

    expect(fixture.componentInstance.isEditMode).toBeFalse();
    expect(rewritesApiSpy.apiDnsRewritesIdGet).not.toHaveBeenCalled();
  });

  it('edit mode: loads the existing rewrite and populates every field', async () => {
    const fixture = await createComponent('rewrite-1', () => {
      rewritesApiSpy.apiDnsRewritesIdGet.and.returnValue(
        of({
          id: 'rewrite-1',
          domain: 'host.example.com',
          target: '203.0.113.5',
          isDisabled: false,
        }),
      );
    });

    expect(fixture.componentInstance.form.controls.domain.value).toBe('host.example.com');
    expect(fixture.componentInstance.form.controls.target.value).toBe('203.0.113.5');
  });

  it('save() in create mode POSTs the built request and navigates back to the list', async () => {
    const fixture = await createComponent(null, () => {
      rewritesApiSpy.apiDnsRewritesPost.and.returnValue(of({ id: 'new-rewrite' }));
    });
    fixture.componentInstance.form.patchValue({
      domain: 'host.example.com',
      target: '203.0.113.5',
    });

    await fixture.componentInstance.save();

    expect(rewritesApiSpy.apiDnsRewritesPost).toHaveBeenCalledWith({
      dnsRewriteWriteRequest: jasmine.objectContaining({
        domain: 'host.example.com',
        target: '203.0.113.5',
      }),
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/dns-rewrites']);
  });

  it('save() in edit mode PUTs the built request', async () => {
    const fixture = await createComponent('rewrite-1', () => {
      rewritesApiSpy.apiDnsRewritesIdGet.and.returnValue(
        of({ id: 'rewrite-1', domain: 'host.example.com', target: '203.0.113.5' }),
      );
      rewritesApiSpy.apiDnsRewritesIdPut.and.returnValue(of(undefined));
    });

    await fixture.componentInstance.save();

    expect(rewritesApiSpy.apiDnsRewritesIdPut).toHaveBeenCalledWith({
      id: 'rewrite-1',
      dnsRewriteWriteRequest: jasmine.objectContaining({ domain: 'host.example.com' }),
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/dns-rewrites']);
  });

  it('shows an error message and does not navigate when saving fails', async () => {
    const fixture = await createComponent(null, () => {
      rewritesApiSpy.apiDnsRewritesPost.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 400,
              error: "'garbage' is neither a valid IP address nor a valid domain name.",
            }),
        ),
      );
    });
    fixture.componentInstance.form.patchValue({ domain: 'host.example.com', target: 'garbage' });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe(
      "'garbage' is neither a valid IP address nor a valid domain name.",
    );
    expect(navigateSpy).not.toHaveBeenCalled();
  });

  it('save() does nothing while the form is invalid', async () => {
    const fixture = await createComponent(null);
    fixture.componentInstance.form.patchValue({ domain: '', target: '' });

    await fixture.componentInstance.save();

    expect(rewritesApiSpy.apiDnsRewritesPost).not.toHaveBeenCalled();
  });

  it('shows an error message when loading the existing rewrite fails', async () => {
    const fixture = await createComponent('rewrite-1', () => {
      rewritesApiSpy.apiDnsRewritesIdGet.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 404 })),
      );
    });

    expect(fixture.componentInstance.errorMessage()).toContain('could not be found');
    expect(fixture.componentInstance.loading()).toBeFalse();
  });
});
