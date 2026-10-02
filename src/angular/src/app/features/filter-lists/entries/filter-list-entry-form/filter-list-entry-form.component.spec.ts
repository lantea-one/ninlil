import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import {
  DnsFilterEntryKindEnum,
  DnsFilterListKindEnum,
  DnsFilterListSourceEnum,
  FilterListEntriesService,
  FilterListsService,
  FilterPatternPreviewService,
} from '../../../../api-client';
import { NotificationService } from '../../../../shared/notification/notification.service';
import {
  FilterListEntryFormComponent,
  MAX_SAMPLE_DOMAINS,
  parseSampleDomains,
} from './filter-list-entry-form.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/** Waits past the 300 ms preview debounce and the preview call that follows. */
function flushPreviewDebounce(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 320));
}

/**
 * These tests cover the entry form: the subscribed-list notice, create and edit, wildcard and
 * regex kinds and their validation, hiding the kind for non-domain lists, and the debounced
 * pattern preview with its sample domains. TestBed with spy-backed entry, list and preview APIs.
 */
describe('FilterListEntryFormComponent', () => {
  let entriesApiSpy: {
    apiFilterListsListIdEntriesIdGet: jasmine.Spy;
    apiFilterListsListIdEntriesPost: jasmine.Spy;
    apiFilterListsListIdEntriesIdPut: jasmine.Spy;
  };
  let filterListsApiSpy: { apiFilterListsIdGet: jasmine.Spy };
  let previewApiSpy: { apiFilterPatternPreviewPost: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let navigateSpy: jasmine.Spy;

  async function createComponent(
    entryId: string | null,
    source: DnsFilterListSourceEnum,
    configureApis?: () => void,
    listKind: DnsFilterListKindEnum = DnsFilterListKindEnum.Domain,
  ) {
    entriesApiSpy = jasmine.createSpyObj('FilterListEntriesService', [
      'apiFilterListsListIdEntriesIdGet',
      'apiFilterListsListIdEntriesPost',
      'apiFilterListsListIdEntriesIdPut',
    ]);
    filterListsApiSpy = jasmine.createSpyObj('FilterListsService', ['apiFilterListsIdGet']);
    previewApiSpy = jasmine.createSpyObj('FilterPatternPreviewService', [
      'apiFilterPatternPreviewPost',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    filterListsApiSpy.apiFilterListsIdGet.and.returnValue(
      of({ id: 'list-1', name: 'threat-intel', source, kind: listKind }),
    );
    previewApiSpy.apiFilterPatternPreviewPost.and.returnValue(
      of({ isValid: true, error: null, matches: [] }),
    );

    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [FilterListEntryFormComponent],
      providers: [
        provideRouter([]),
        { provide: FilterListEntriesService, useValue: entriesApiSpy },
        { provide: FilterListsService, useValue: filterListsApiSpy },
        { provide: FilterPatternPreviewService, useValue: previewApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: convertToParamMap({ listId: 'list-1', ...(entryId ? { entryId } : {}) }),
            },
          },
        },
      ],
    }).compileComponents();

    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    const fixture = TestBed.createComponent(FilterListEntryFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('is read-only, and never loads the entry, for a subscribed list', async () => {
    const fixture = await createComponent('entry-1', DnsFilterListSourceEnum.Subscribed);

    expect(fixture.componentInstance.isReadOnly()).toBeTrue();
    expect(entriesApiSpy.apiFilterListsListIdEntriesIdGet).not.toHaveBeenCalled();
  });

  it('save() does nothing at all for a read-only (subscribed) list', async () => {
    const fixture = await createComponent(null, DnsFilterListSourceEnum.Subscribed);
    fixture.componentInstance.form.patchValue({ value: 'bad.example' });

    await fixture.componentInstance.save();

    expect(entriesApiSpy.apiFilterListsListIdEntriesPost).not.toHaveBeenCalled();
  });

  it('edit mode: loads the existing entry for a Local list', async () => {
    const fixture = await createComponent('entry-1', DnsFilterListSourceEnum.Local, () => {
      entriesApiSpy.apiFilterListsListIdEntriesIdGet.and.returnValue(
        of({ id: 'entry-1', value: 'bad.example', comment: 'seen in the wild' }),
      );
    });

    expect(fixture.componentInstance.form.controls.value.value).toBe('bad.example');
    expect(fixture.componentInstance.form.controls.comment.value).toBe('seen in the wild');
  });

  it('save() in create mode POSTs the built request and navigates back to the entry list', async () => {
    const fixture = await createComponent(null, DnsFilterListSourceEnum.Local, () => {
      entriesApiSpy.apiFilterListsListIdEntriesPost.and.returnValue(of({ id: 'entry-1' }));
    });
    fixture.componentInstance.form.patchValue({ value: 'bad.example' });

    await fixture.componentInstance.save();

    expect(entriesApiSpy.apiFilterListsListIdEntriesPost).toHaveBeenCalledWith({
      listId: 'list-1',
      filterListEntryWriteRequest: {
        value: 'bad.example',
        comment: null,
        kind: DnsFilterEntryKindEnum.Domain,
      },
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/filter-lists', 'list-1', 'entries']);
  });

  it('save() in edit mode PUTs the built request', async () => {
    const fixture = await createComponent('entry-1', DnsFilterListSourceEnum.Local, () => {
      entriesApiSpy.apiFilterListsListIdEntriesIdGet.and.returnValue(
        of({ id: 'entry-1', value: 'bad.example', comment: null }),
      );
      entriesApiSpy.apiFilterListsListIdEntriesIdPut.and.returnValue(of(undefined));
    });

    await fixture.componentInstance.save();

    expect(entriesApiSpy.apiFilterListsListIdEntriesIdPut).toHaveBeenCalledWith({
      listId: 'list-1',
      id: 'entry-1',
      filterListEntryWriteRequest: {
        value: 'bad.example',
        comment: null,
        kind: DnsFilterEntryKindEnum.Domain,
      },
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/filter-lists', 'list-1', 'entries']);
  });

  it('edit mode: loads an existing wildcard/regex entry with its own kind', async () => {
    const fixture = await createComponent('entry-1', DnsFilterListSourceEnum.Local, () => {
      entriesApiSpy.apiFilterListsListIdEntriesIdGet.and.returnValue(
        of({
          id: 'entry-1',
          value: '^ads?\\.example\\.com$',
          comment: null,
          kind: DnsFilterEntryKindEnum.Regex,
        }),
      );
    });

    expect(fixture.componentInstance.form.controls.kind.value).toBe(DnsFilterEntryKindEnum.Regex);
    expect(fixture.componentInstance.isPatternKind()).toBeTrue();
  });

  it('save() POSTs a wildcard entry with its own kind', async () => {
    const fixture = await createComponent(null, DnsFilterListSourceEnum.Local, () => {
      entriesApiSpy.apiFilterListsListIdEntriesPost.and.returnValue(of({ id: 'entry-1' }));
    });
    fixture.componentInstance.form.patchValue({
      value: 'ads.*.example.com',
      kind: DnsFilterEntryKindEnum.Wildcard,
    });

    await fixture.componentInstance.save();

    expect(entriesApiSpy.apiFilterListsListIdEntriesPost).toHaveBeenCalledWith({
      listId: 'list-1',
      filterListEntryWriteRequest: {
        value: 'ads.*.example.com',
        comment: null,
        kind: DnsFilterEntryKindEnum.Wildcard,
      },
    });
  });

  it('a malformed regex value is rejected by the form itself, before submit', async () => {
    const fixture = await createComponent(null, DnsFilterListSourceEnum.Local);
    fixture.componentInstance.form.patchValue({
      value: '(',
      kind: DnsFilterEntryKindEnum.Regex,
    });

    expect(fixture.componentInstance.form.controls.value.hasError('invalidPattern')).toBeTrue();

    await fixture.componentInstance.save();

    expect(entriesApiSpy.apiFilterListsListIdEntriesPost).not.toHaveBeenCalled();
  });

  it('switching kind re-validates the value control against the new pattern grammar', async () => {
    const fixture = await createComponent(null, DnsFilterListSourceEnum.Local);
    fixture.componentInstance.form.patchValue({ value: '(', kind: DnsFilterEntryKindEnum.Domain });
    expect(fixture.componentInstance.form.controls.value.hasError('invalidPattern')).toBeFalse();

    fixture.componentInstance.form.controls.kind.setValue(DnsFilterEntryKindEnum.Regex);

    expect(fixture.componentInstance.form.controls.value.hasError('invalidPattern')).toBeTrue();
  });

  it('the kind select is hidden entirely for a non-Domain (e.g. ClientSource) list', async () => {
    const fixture = await createComponent(
      null,
      DnsFilterListSourceEnum.Local,
      undefined,
      DnsFilterListKindEnum.ClientSource,
    );

    expect(fixture.componentInstance.isDomainKindList()).toBeFalse();
    expect(fixture.componentInstance.form.controls.kind.value).toBe(DnsFilterEntryKindEnum.Domain);
  });

  it('typing a pattern calls the preview endpoint, debounced, and populates previewResult()', async () => {
    const fixture = await createComponent(null, DnsFilterListSourceEnum.Local);
    previewApiSpy.apiFilterPatternPreviewPost.and.returnValue(
      of({
        isValid: true,
        error: null,
        matches: [
          { domain: 'ads.example.com', isMatch: true },
          { domain: 'example.com', isMatch: false },
        ],
      }),
    );

    fixture.componentInstance.form.patchValue({ value: '*.example.com' });
    await flushPreviewDebounce();

    expect(previewApiSpy.apiFilterPatternPreviewPost).toHaveBeenCalledWith({
      filterPatternPreviewRequest: {
        kind: DnsFilterEntryKindEnum.Domain,
        value: '*.example.com',
        sampleDomains: null,
      },
    });
    expect(fixture.componentInstance.previewResult()?.matches.length).toBe(2);
  });

  it('clearing the value clears previewResult() without calling the endpoint again', async () => {
    const fixture = await createComponent(null, DnsFilterListSourceEnum.Local);

    fixture.componentInstance.form.patchValue({ value: 'ads.example.com' });
    await flushPreviewDebounce();
    previewApiSpy.apiFilterPatternPreviewPost.calls.reset();

    fixture.componentInstance.form.patchValue({ value: '' });
    await flushPreviewDebounce();

    expect(fixture.componentInstance.previewResult()).toBeNull();
    expect(previewApiSpy.apiFilterPatternPreviewPost).not.toHaveBeenCalled();
  });

  it('a preview endpoint failure is swallowed, leaving the form itself usable', async () => {
    const fixture = await createComponent(null, DnsFilterListSourceEnum.Local);
    previewApiSpy.apiFilterPatternPreviewPost.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500, error: 'boom' })),
    );

    fixture.componentInstance.form.patchValue({ value: 'ads.example.com' });
    await flushPreviewDebounce();

    expect(fixture.componentInstance.previewResult()).toBeNull();
    expect(fixture.componentInstance.form.controls.value.value).toBe('ads.example.com');
  });
  it("sends the operator's own test domains with the preview", async () => {
    const fixture = await createComponent(null, DnsFilterListSourceEnum.Local);
    const component = fixture.componentInstance;

    component.form.controls.value.setValue('ads.example.com');
    component.sampleDomainsControl.setValue('ads.example.com, cdn.example.net');
    await flushPreviewDebounce();

    const request =
      previewApiSpy.apiFilterPatternPreviewPost.calls.mostRecent().args[0]
        .filterPatternPreviewRequest;
    expect(request.sampleDomains).toEqual(['ads.example.com', 'cdn.example.net']);
  });

  it('lets the server use its own samples when no test domains are given', async () => {
    const fixture = await createComponent(null, DnsFilterListSourceEnum.Local);

    fixture.componentInstance.form.controls.value.setValue('ads.example.com');
    await flushPreviewDebounce();

    const request =
      previewApiSpy.apiFilterPatternPreviewPost.calls.mostRecent().args[0]
        .filterPatternPreviewRequest;
    expect(request.sampleDomains).toBeNull();
  });

  it('re-runs the preview when only the test domains change', async () => {
    const fixture = await createComponent(null, DnsFilterListSourceEnum.Local);
    const component = fixture.componentInstance;

    component.form.controls.value.setValue('ads.example.com');
    await flushPreviewDebounce();
    const callsBefore = previewApiSpy.apiFilterPatternPreviewPost.calls.count();

    component.sampleDomainsControl.setValue('tracker.example.org');
    await flushPreviewDebounce();

    expect(previewApiSpy.apiFilterPatternPreviewPost.calls.count()).toBe(callsBefore + 1);
  });

  describe('parseSampleDomains', () => {
    it('splits on spaces, commas and new lines and drops blanks and duplicates', () => {
      expect(parseSampleDomains(' a.example ,b.example\n\na.example  c.example ')).toEqual([
        'a.example',
        'b.example',
        'c.example',
      ]);
    });

    it('keeps at most the cap', () => {
      const many = Array.from({ length: MAX_SAMPLE_DOMAINS + 5 }, (_, i) => `d${i}.example`);
      expect(parseSampleDomains(many.join(' ')).length).toBe(MAX_SAMPLE_DOMAINS);
    });
  });
});
