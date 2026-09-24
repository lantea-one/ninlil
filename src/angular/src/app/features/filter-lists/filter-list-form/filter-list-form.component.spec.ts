import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import {
  DnsFilterListActionEnum,
  DnsFilterListKindEnum,
  DnsFilterListSourceEnum,
  FilterListsService,
} from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { FilterListFormComponent } from './filter-list-form.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('FilterListFormComponent', () => {
  let filterListsApiSpy: {
    apiFilterListsIdGet: jasmine.Spy;
    apiFilterListsPost: jasmine.Spy;
    apiFilterListsIdPut: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let navigateSpy: jasmine.Spy;

  /** See `record-form.component.spec.ts`'s own identical helper for why `configureApis` must run before construction. */
  async function createComponent(listId: string | null, configureApis?: () => void) {
    filterListsApiSpy = jasmine.createSpyObj('FilterListsService', [
      'apiFilterListsIdGet',
      'apiFilterListsPost',
      'apiFilterListsIdPut',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [FilterListFormComponent],
      providers: [
        provideRouter([]),
        { provide: FilterListsService, useValue: filterListsApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: convertToParamMap(listId ? { listId } : {}) },
          },
        },
      ],
    }).compileComponents();

    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    const fixture = TestBed.createComponent(FilterListFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('accepts a refreshIntervalText value with a days prefix, a real TimeSpan "c" format shape the backend accepts', async () => {
    const fixture = await createComponent(null);

    fixture.componentInstance.form.controls.refreshIntervalText.setValue('2.12:00:00');
    expect(
      fixture.componentInstance.form.controls.refreshIntervalText.hasError('pattern'),
    ).toBeFalse();
  });

  it('create mode: defaults to Local source, with no owning list to load', async () => {
    const fixture = await createComponent(null);

    expect(fixture.componentInstance.isEditMode).toBeFalse();
    expect(fixture.componentInstance.selectedSource()).toBe(DnsFilterListSourceEnum.Local);
    expect(filterListsApiSpy.apiFilterListsIdGet).not.toHaveBeenCalled();
  });

  it('showSubscriptionSection() is true only when source is Subscribed', async () => {
    const fixture = await createComponent(null);

    expect(fixture.componentInstance.showSubscriptionSection()).toBeFalse();
    fixture.componentInstance.form.controls.source.setValue(DnsFilterListSourceEnum.Subscribed);
    expect(fixture.componentInstance.showSubscriptionSection()).toBeTrue();
  });

  it('edit mode: loads the existing filter list and populates every field', async () => {
    const fixture = await createComponent('list-1', () => {
      filterListsApiSpy.apiFilterListsIdGet.and.returnValue(
        of({
          id: 'list-1',
          name: 'threat-intel',
          kind: DnsFilterListKindEnum.Domain,
          action: DnsFilterListActionEnum.Block,
          source: DnsFilterListSourceEnum.Subscribed,
          subscriptionUrl: 'https://example.com/list.txt',
          refreshInterval: '01:00:00',
          isDisabled: false,
        }),
      );
    });

    expect(fixture.componentInstance.isEditMode).toBeTrue();
    expect(fixture.componentInstance.selectedSource()).toBe(DnsFilterListSourceEnum.Subscribed);
    expect(fixture.componentInstance.form.controls.subscriptionUrl.value).toBe(
      'https://example.com/list.txt',
    );
  });

  it('save() rejects a Subscribed source with no subscription URL before ever calling the API', async () => {
    const fixture = await createComponent(null);
    fixture.componentInstance.form.patchValue({
      name: 'threat-intel',
      source: DnsFilterListSourceEnum.Subscribed,
    });

    await fixture.componentInstance.save();

    expect(filterListsApiSpy.apiFilterListsPost).not.toHaveBeenCalled();
    expect(fixture.componentInstance.errorMessage()).toContain('subscriptionUrl');
  });

  it('save() in create mode POSTs the built request and navigates to the list', async () => {
    const fixture = await createComponent(null, () => {
      filterListsApiSpy.apiFilterListsPost.and.returnValue(of({ id: 'new-list' }));
    });

    fixture.componentInstance.form.patchValue({ name: 'threat-intel' });
    await fixture.componentInstance.save();

    expect(filterListsApiSpy.apiFilterListsPost).toHaveBeenCalledWith({
      filterListWriteRequest: jasmine.objectContaining({ name: 'threat-intel' }),
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/filter-lists']);
  });

  it('save() in edit mode PUTs the built request and navigates back to the list', async () => {
    const fixture = await createComponent('list-1', () => {
      filterListsApiSpy.apiFilterListsIdGet.and.returnValue(
        of({ id: 'list-1', name: 'threat-intel', source: DnsFilterListSourceEnum.Local }),
      );
      filterListsApiSpy.apiFilterListsIdPut.and.returnValue(of(undefined));
    });

    await fixture.componentInstance.save();

    expect(filterListsApiSpy.apiFilterListsIdPut).toHaveBeenCalledWith({
      id: 'list-1',
      filterListWriteRequest: jasmine.objectContaining({ name: 'threat-intel' }),
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/filter-lists']);
  });

  it('shows an error message and does not navigate when saving fails', async () => {
    const fixture = await createComponent(null, () => {
      filterListsApiSpy.apiFilterListsPost.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({ status: 409, error: 'A filter list named x already exists.' }),
        ),
      );
    });
    fixture.componentInstance.form.patchValue({ name: 'threat-intel' });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('A filter list named x already exists.');
    expect(navigateSpy).not.toHaveBeenCalled();
  });

  it('save() does nothing while the form is invalid', async () => {
    const fixture = await createComponent(null);
    fixture.componentInstance.form.patchValue({ name: '' });

    await fixture.componentInstance.save();

    expect(filterListsApiSpy.apiFilterListsPost).not.toHaveBeenCalled();
  });

  it('isBulkSubscriptionMode() is true for a new Subscribed list', async () => {
    const fixture = await createComponent(null);
    expect(fixture.componentInstance.isBulkSubscriptionMode()).toBeFalse();
    fixture.componentInstance.form.controls.source.setValue(DnsFilterListSourceEnum.Subscribed);
    expect(fixture.componentInstance.isBulkSubscriptionMode()).toBeTrue();
  });

  it('isBulkSubscriptionMode() is false in edit mode, even for a Subscribed list', async () => {
    const fixture = await createComponent('list-1', () => {
      filterListsApiSpy.apiFilterListsIdGet.and.returnValue(
        of({ id: 'list-1', name: 'threat-intel', source: DnsFilterListSourceEnum.Subscribed }),
      );
    });
    expect(fixture.componentInstance.isBulkSubscriptionMode()).toBeFalse();
  });

  it('appendLine() adds a known block-list URL as a new textarea line without replacing existing content', async () => {
    const fixture = await createComponent(null);
    fixture.componentInstance.form.controls.source.setValue(DnsFilterListSourceEnum.Subscribed);
    fixture.componentInstance.form.controls.subscriptionUrl.setValue(
      'https://example.com/custom.txt',
    );

    fixture.componentInstance.appendLine('https://big.oisd.nl/');

    expect(fixture.componentInstance.form.controls.subscriptionUrl.value).toBe(
      'https://example.com/custom.txt\nhttps://big.oisd.nl/',
    );
  });

  it("save() in bulk-subscription create mode with a line matching a known block list derives that list's own name", async () => {
    const fixture = await createComponent(null, () => {
      filterListsApiSpy.apiFilterListsPost.and.returnValue(of({ id: 'new-list' }));
    });
    fixture.componentInstance.form.patchValue({
      name: 'ignored',
      source: DnsFilterListSourceEnum.Subscribed,
      subscriptionUrl: 'https://big.oisd.nl/',
    });

    await fixture.componentInstance.save();

    expect(filterListsApiSpy.apiFilterListsPost).toHaveBeenCalledWith({
      filterListWriteRequest: jasmine.objectContaining({
        name: 'OISD Big',
        subscriptionUrl: 'https://big.oisd.nl/',
        source: DnsFilterListSourceEnum.Subscribed,
      }),
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/filter-lists']);
  });

  it('save() in bulk-subscription create mode with multiple lines POSTs one filter list per non-blank line', async () => {
    const fixture = await createComponent(null, () => {
      filterListsApiSpy.apiFilterListsPost.and.returnValue(of({ id: 'created' }));
    });
    fixture.componentInstance.form.patchValue({
      name: 'custom',
      source: DnsFilterListSourceEnum.Subscribed,
      subscriptionUrl: 'https://big.oisd.nl/\nhttps://example.com/a.txt\nhttps://example.com/b.txt',
    });

    await fixture.componentInstance.save();

    expect(filterListsApiSpy.apiFilterListsPost).toHaveBeenCalledTimes(3);
    expect(filterListsApiSpy.apiFilterListsPost.calls.argsFor(0)[0].filterListWriteRequest).toEqual(
      jasmine.objectContaining({ name: 'OISD Big', subscriptionUrl: 'https://big.oisd.nl/' }),
    );
    expect(filterListsApiSpy.apiFilterListsPost.calls.argsFor(1)[0].filterListWriteRequest).toEqual(
      jasmine.objectContaining({
        name: 'custom 2',
        subscriptionUrl: 'https://example.com/a.txt',
      }),
    );
    expect(filterListsApiSpy.apiFilterListsPost.calls.argsFor(2)[0].filterListWriteRequest).toEqual(
      jasmine.objectContaining({
        name: 'custom 3',
        subscriptionUrl: 'https://example.com/b.txt',
      }),
    );
    expect(navigateSpy).toHaveBeenCalledWith(['/filter-lists']);
  });

  it('save() in bulk-subscription create mode with multiple lines reports a partial failure clearly and keeps only the failed lines for retry', async () => {
    const fixture = await createComponent(null, () => {
      filterListsApiSpy.apiFilterListsPost.and.callFake(
        (args: { filterListWriteRequest: { subscriptionUrl: string } }) =>
          args.filterListWriteRequest.subscriptionUrl === 'https://example.com/a.txt'
            ? throwError(
                () =>
                  new HttpErrorResponse({
                    status: 409,
                    error: "A filter list named 'custom 2' already exists.",
                  }),
              )
            : of({ id: 'created' }),
      );
    });
    fixture.componentInstance.form.patchValue({
      name: 'custom',
      source: DnsFilterListSourceEnum.Subscribed,
      subscriptionUrl: 'https://big.oisd.nl/\nhttps://example.com/a.txt',
    });

    await fixture.componentInstance.save();

    expect(filterListsApiSpy.apiFilterListsPost).toHaveBeenCalledTimes(2);
    expect(navigateSpy).not.toHaveBeenCalled();
    expect(fixture.componentInstance.errorMessage()).toContain('1 of 2 filter lists created');
    expect(fixture.componentInstance.errorMessage()).toContain('https://example.com/a.txt');
    expect(fixture.componentInstance.errorMessage()).toContain(
      "A filter list named 'custom 2' already exists.",
    );
    expect(fixture.componentInstance.form.controls.subscriptionUrl.value).toBe(
      'https://example.com/a.txt',
    );
    expect(notificationSpy.success).toHaveBeenCalledWith('1 of 2 filter lists created.');
  });
});
