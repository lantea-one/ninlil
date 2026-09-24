import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { DnsAccessControlRulesService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { AccessControlRuleFormComponent } from './access-control-rule-form.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('AccessControlRuleFormComponent', () => {
  let apiSpy: {
    apiAccessControlRulesIdGet: jasmine.Spy;
    apiAccessControlRulesPost: jasmine.Spy;
    apiAccessControlRulesIdPut: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let navigateSpy: jasmine.Spy;

  async function createComponent(ruleId: string | null, configureApis?: () => void) {
    apiSpy = jasmine.createSpyObj('DnsAccessControlRulesService', [
      'apiAccessControlRulesIdGet',
      'apiAccessControlRulesPost',
      'apiAccessControlRulesIdPut',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [AccessControlRuleFormComponent],
      providers: [
        provideRouter([]),
        { provide: DnsAccessControlRulesService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap(ruleId ? { id: ruleId } : {}) } },
        },
      ],
    }).compileComponents();

    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    const fixture = TestBed.createComponent(AccessControlRuleFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('create mode: does not load any rule', async () => {
    const fixture = await createComponent(null);

    expect(fixture.componentInstance.isEditMode).toBeFalse();
    expect(apiSpy.apiAccessControlRulesIdGet).not.toHaveBeenCalled();
  });

  it('edit mode: loads the existing rule and expands its capabilities array back into the two checkboxes', async () => {
    const fixture = await createComponent('rule-1', () => {
      apiSpy.apiAccessControlRulesIdGet.and.returnValue(
        of({
          id: 'rule-1',
          name: 'Trusted LAN',
          cidr: '192.168.1.0/24',
          capabilities: ['RecursionAccess'],
          isDisabled: false,
        }),
      );
    });

    expect(fixture.componentInstance.form.controls.name.value).toBe('Trusted LAN');
    expect(fixture.componentInstance.form.controls.cidr.value).toBe('192.168.1.0/24');
    expect(fixture.componentInstance.form.controls.grantsRecursionAccess.value).toBeTrue();
    expect(fixture.componentInstance.form.controls.grantsRateLimitBypass.value).toBeFalse();
  });

  it('save() in create mode collapses both checkboxes into the capabilities array and POSTs it', async () => {
    const fixture = await createComponent(null, () => {
      apiSpy.apiAccessControlRulesPost.and.returnValue(of({ id: 'new-rule' }));
    });
    fixture.componentInstance.form.patchValue({
      name: 'Trusted LAN',
      cidr: '192.168.1.0/24',
      grantsRecursionAccess: true,
      grantsRateLimitBypass: true,
    });

    await fixture.componentInstance.save();

    expect(apiSpy.apiAccessControlRulesPost).toHaveBeenCalledWith({
      dnsAccessControlRuleWriteRequest: {
        name: 'Trusted LAN',
        cidr: '192.168.1.0/24',
        capabilities: ['RecursionAccess', 'RateLimitBypass'],
        isDisabled: false,
      },
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/access-control-rules']);
  });

  it('save() with neither checkbox checked sends an empty capabilities array', async () => {
    const fixture = await createComponent(null, () => {
      apiSpy.apiAccessControlRulesPost.and.returnValue(of({ id: 'new-rule' }));
    });
    fixture.componentInstance.form.patchValue({ name: 'Nothing granted', cidr: '10.0.0.0/8' });

    await fixture.componentInstance.save();

    expect(apiSpy.apiAccessControlRulesPost).toHaveBeenCalledWith({
      dnsAccessControlRuleWriteRequest: {
        name: 'Nothing granted',
        cidr: '10.0.0.0/8',
        capabilities: [],
        isDisabled: false,
      },
    });
  });

  it('save() in edit mode PUTs the built request', async () => {
    const fixture = await createComponent('rule-1', () => {
      apiSpy.apiAccessControlRulesIdGet.and.returnValue(
        of({
          id: 'rule-1',
          name: 'Trusted LAN',
          cidr: '192.168.1.0/24',
          capabilities: ['RateLimitBypass'],
          isDisabled: false,
        }),
      );
      apiSpy.apiAccessControlRulesIdPut.and.returnValue(of(undefined));
    });

    await fixture.componentInstance.save();

    expect(apiSpy.apiAccessControlRulesIdPut).toHaveBeenCalledWith({
      id: 'rule-1',
      dnsAccessControlRuleWriteRequest: {
        name: 'Trusted LAN',
        cidr: '192.168.1.0/24',
        capabilities: ['RateLimitBypass'],
        isDisabled: false,
      },
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/access-control-rules']);
  });

  it('shows an error message and does not navigate when saving fails', async () => {
    const fixture = await createComponent(null, () => {
      apiSpy.apiAccessControlRulesPost.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({ status: 400, error: "'10.0.0.0/33' is not a valid network." }),
        ),
      );
    });
    fixture.componentInstance.form.patchValue({ name: 'Bad', cidr: '10.0.0.0/33' });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe("'10.0.0.0/33' is not a valid network.");
    expect(navigateSpy).not.toHaveBeenCalled();
  });

  it('save() does nothing while the form is invalid', async () => {
    const fixture = await createComponent(null);
    fixture.componentInstance.form.patchValue({ name: '', cidr: '' });

    await fixture.componentInstance.save();

    expect(apiSpy.apiAccessControlRulesPost).not.toHaveBeenCalled();
  });
});
