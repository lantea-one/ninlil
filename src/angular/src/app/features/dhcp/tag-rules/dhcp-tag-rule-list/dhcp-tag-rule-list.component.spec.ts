import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { DhcpScopesService, DhcpTagRulesService } from '../../../../api-client';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { DhcpTagRuleListComponent } from './dhcp-tag-rule-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('DhcpTagRuleListComponent', () => {
  let tagRulesApiSpy: {
    apiDhcpTagRulesGet: jasmine.Spy;
    apiDhcpTagRulesIdPatch: jasmine.Spy;
    apiDhcpTagRulesIdDelete: jasmine.Spy;
  };
  let scopesApiSpy: { apiDhcpScopesGet: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    tagRulesApiSpy = jasmine.createSpyObj('DhcpTagRulesService', [
      'apiDhcpTagRulesGet',
      'apiDhcpTagRulesIdPatch',
      'apiDhcpTagRulesIdDelete',
    ]);
    scopesApiSpy = jasmine.createSpyObj('DhcpScopesService', ['apiDhcpScopesGet']);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    scopesApiSpy.apiDhcpScopesGet.and.returnValue(
      of({ items: [{ id: 'scope-1', name: 'lan' }], skip: 0, take: 200, totalCount: 1 }),
    );
    tagRulesApiSpy.apiDhcpTagRulesGet.and.returnValue(
      of({
        items: [
          {
            id: 'rule-1',
            name: 'pxe',
            scopeId: 'scope-1',
            priority: 0,
            isDisabled: false,
          },
        ],
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [DhcpTagRuleListComponent],
      providers: [
        provideRouter([]),
        { provide: DhcpTagRulesService, useValue: tagRulesApiSpy },
        { provide: DhcpScopesService, useValue: scopesApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(DhcpTagRuleListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the first page of tag rules and resolves the scope name', async () => {
    const fixture = await createComponent();

    expect(tagRulesApiSpy.apiDhcpTagRulesGet).toHaveBeenCalledWith({ skip: 0, take: 25 });
    expect(fixture.componentInstance.rules().length).toBe(1);
    expect(fixture.componentInstance.scopeLabel(fixture.componentInstance.rules()[0])).toBe('lan');
  });

  it('scopeLabel() falls back to "Any scope" for a rule with no scopeId', async () => {
    const fixture = await createComponent();
    expect(fixture.componentInstance.scopeLabel({ scopeId: null })).toBe('Any scope');
  });

  it('scopeLabel() falls back to the raw id when the scope name cannot be resolved', async () => {
    const fixture = await createComponent();
    expect(fixture.componentInstance.scopeLabel({ scopeId: 'unknown-scope' })).toBe(
      'unknown-scope',
    );
  });

  it('toggleDisabled() sends a single-field merge patch, then reloads the page', async () => {
    const fixture = await createComponent();
    tagRulesApiSpy.apiDhcpTagRulesIdPatch.and.returnValue(of(undefined));

    await fixture.componentInstance.toggleDisabled(fixture.componentInstance.rules()[0]);

    expect(tagRulesApiSpy.apiDhcpTagRulesIdPatch).toHaveBeenCalledWith({
      id: 'rule-1',
      body: { isDisabled: true },
    });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteRule() deletes and reloads the page once confirmed', async () => {
    const fixture = await createComponent();
    tagRulesApiSpy.apiDhcpTagRulesIdDelete.and.returnValue(of(undefined));

    const deletePromise = fixture.componentInstance.deleteRule(
      fixture.componentInstance.rules()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(tagRulesApiSpy.apiDhcpTagRulesIdDelete).toHaveBeenCalledWith({ id: 'rule-1' });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('shows an error notification when delete fails', async () => {
    const fixture = await createComponent();
    tagRulesApiSpy.apiDhcpTagRulesIdDelete.and.returnValue(
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
