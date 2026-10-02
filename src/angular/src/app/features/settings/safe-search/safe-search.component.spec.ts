import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { SafeSearchService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { SafeSearchComponent } from './safe-search.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the safe search panel: loading the catalog, enabling (with the rewrite-switch
 * notice) and disabling an entry, errors, and scoping to a client group. TestBed with a spy-backed
 * `SafeSearchService` and `NotificationService`.
 */
describe('SafeSearchComponent', () => {
  let apiSpy: {
    apiSafeSearchGet: jasmine.Spy;
    apiSafeSearchKeyPost: jasmine.Spy;
    apiSafeSearchKeyDelete: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    apiSpy = jasmine.createSpyObj('SafeSearchService', [
      'apiSafeSearchGet',
      'apiSafeSearchKeyPost',
      'apiSafeSearchKeyDelete',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiSafeSearchGet.and.returnValue(
      of([
        {
          key: 'google',
          displayName: 'Google Search',
          description: "Forces Google's own SafeSearch filtering.",
          sourceDocumentationUrl: 'https://support.google.com/websearch/answer/186669',
          hostnameCount: 2,
          isEnabled: false,
        },
      ]),
    );

    await TestBed.configureTestingModule({
      imports: [SafeSearchComponent],
      providers: [
        { provide: SafeSearchService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SafeSearchComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the catalog on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiSafeSearchGet).toHaveBeenCalled();
    expect(fixture.componentInstance.engines().length).toBe(1);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('toggle() enables a disabled entry and mentions the rewrite-toggle side effect', async () => {
    const fixture = await createComponent();
    apiSpy.apiSafeSearchKeyPost.and.returnValue(
      of({ key: 'google', displayName: 'Google Search', isEnabled: true }),
    );
    apiSpy.apiSafeSearchGet.and.returnValue(
      of([{ key: 'google', displayName: 'Google Search', isEnabled: true }]),
    );

    await fixture.componentInstance.toggle(fixture.componentInstance.engines()[0]);

    expect(apiSpy.apiSafeSearchKeyPost).toHaveBeenCalledWith(
      jasmine.objectContaining({ key: 'google' }),
    );
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('turned on network-wide'),
    );
  });

  it('toggle() disables an already-enabled entry', async () => {
    const fixture = await createComponent();
    fixture.componentInstance.engines.set([
      { key: 'google', displayName: 'Google Search', isEnabled: true },
    ]);
    apiSpy.apiSafeSearchKeyDelete.and.returnValue(of(undefined));
    apiSpy.apiSafeSearchGet.and.returnValue(
      of([{ key: 'google', displayName: 'Google Search', isEnabled: false }]),
    );

    await fixture.componentInstance.toggle(fixture.componentInstance.engines()[0]);

    expect(apiSpy.apiSafeSearchKeyDelete).toHaveBeenCalledWith(
      jasmine.objectContaining({ key: 'google' }),
    );
    expect(notificationSpy.success).toHaveBeenCalledWith(jasmine.stringContaining('disabled'));
  });

  it('shows an error message when a toggle fails', async () => {
    const fixture = await createComponent();
    apiSpy.apiSafeSearchKeyPost.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: 'conflict' })),
    );

    await fixture.componentInstance.toggle(fixture.componentInstance.engines()[0]);

    expect(fixture.componentInstance.errorMessage()).toBeTruthy();
  });

  it('forwards an optional clientGroupId input on every load and toggle call', async () => {
    apiSpy = jasmine.createSpyObj('SafeSearchService', [
      'apiSafeSearchGet',
      'apiSafeSearchKeyPost',
      'apiSafeSearchKeyDelete',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);
    apiSpy.apiSafeSearchGet.and.returnValue(
      of([{ key: 'google', displayName: 'Google Search', isEnabled: false }]),
    );

    await TestBed.configureTestingModule({
      imports: [SafeSearchComponent],
      providers: [
        { provide: SafeSearchService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SafeSearchComponent);
    fixture.componentInstance.clientGroupId = 'group-456';
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();

    expect(apiSpy.apiSafeSearchGet).toHaveBeenCalledWith(
      jasmine.objectContaining({ clientGroupId: 'group-456' }),
    );

    apiSpy.apiSafeSearchKeyPost.and.returnValue(
      of({ key: 'google', displayName: 'Google Search', isEnabled: true }),
    );
    await fixture.componentInstance.toggle(fixture.componentInstance.engines()[0]);

    expect(apiSpy.apiSafeSearchKeyPost).toHaveBeenCalledWith(
      jasmine.objectContaining({ key: 'google', clientGroupId: 'group-456' }),
    );
  });
});
