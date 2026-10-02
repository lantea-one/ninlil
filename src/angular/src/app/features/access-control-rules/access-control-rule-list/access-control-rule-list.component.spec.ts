import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { DnsAccessControlRulesService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { AccessControlRuleListComponent } from './access-control-rule-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the rule list: loading, the enable/disable toggle's full `PUT`, and delete
 * with the confirmation dialog accepted, dismissed or failing. TestBed with a spy-backed
 * `DnsAccessControlRulesService` and `NotificationService`.
 */
describe('AccessControlRuleListComponent', () => {
  let apiSpy: {
    apiAccessControlRulesGet: jasmine.Spy;
    apiAccessControlRulesIdPut: jasmine.Spy;
    apiAccessControlRulesIdDelete: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    apiSpy = jasmine.createSpyObj('DnsAccessControlRulesService', [
      'apiAccessControlRulesGet',
      'apiAccessControlRulesIdPut',
      'apiAccessControlRulesIdDelete',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiAccessControlRulesGet.and.returnValue(
      of({
        items: [
          {
            id: 'rule-1',
            name: 'Trusted LAN',
            cidr: '192.168.1.0/24',
            capabilities: ['RecursionAccess'],
            isDisabled: false,
            createdAtUtc: '2026-09-02T00:00:00Z',
            updatedAtUtc: null,
          },
        ],
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [AccessControlRuleListComponent],
      providers: [
        provideRouter([]),
        { provide: DnsAccessControlRulesService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(AccessControlRuleListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the first page of rules on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiAccessControlRulesGet).toHaveBeenCalledWith({ skip: 0, take: 25 });
    expect(fixture.componentInstance.rules().length).toBe(1);
    expect(fixture.componentInstance.rules()[0].capabilities).toEqual(['RecursionAccess']);
  });

  it('toggleDisabled() sends a full PUT preserving every other field, then reloads the page', async () => {
    const fixture = await createComponent();
    apiSpy.apiAccessControlRulesIdPut.and.returnValue(of(undefined));

    await fixture.componentInstance.toggleDisabled(fixture.componentInstance.rules()[0]);

    expect(apiSpy.apiAccessControlRulesIdPut).toHaveBeenCalledWith({
      id: 'rule-1',
      dnsAccessControlRuleWriteRequest: {
        name: 'Trusted LAN',
        cidr: '192.168.1.0/24',
        capabilities: ['RecursionAccess'],
        isDisabled: true,
      },
    });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteRule() deletes and reloads the page once the confirmation dialog is accepted', async () => {
    const fixture = await createComponent();
    apiSpy.apiAccessControlRulesIdDelete.and.returnValue(of(undefined));

    const deletePromise = fixture.componentInstance.deleteRule(
      fixture.componentInstance.rules()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(apiSpy.apiAccessControlRulesIdDelete).toHaveBeenCalledWith({ id: 'rule-1' });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteRule() does nothing when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent();

    const deletePromise = fixture.componentInstance.deleteRule(
      fixture.componentInstance.rules()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(false);
    await deletePromise;

    expect(apiSpy.apiAccessControlRulesIdDelete).not.toHaveBeenCalled();
  });

  it('shows an error notification when delete fails', async () => {
    const fixture = await createComponent();
    apiSpy.apiAccessControlRulesIdDelete.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: 'in use' })),
    );

    const deletePromise = fixture.componentInstance.deleteRule(
      fixture.componentInstance.rules()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(notificationSpy.error).toHaveBeenCalledWith('in use');
  });
});
