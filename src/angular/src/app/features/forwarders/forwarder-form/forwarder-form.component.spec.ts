import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { ForwardersService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { ForwarderFormComponent } from './forwarder-form.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('ForwarderFormComponent', () => {
  let forwardersApiSpy: {
    apiForwardersIdGet: jasmine.Spy;
    apiForwardersPost: jasmine.Spy;
    apiForwardersIdPut: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let navigateSpy: jasmine.Spy;

  async function createComponent(forwarderId: string | null, configureApis?: () => void) {
    forwardersApiSpy = jasmine.createSpyObj('ForwardersService', [
      'apiForwardersIdGet',
      'apiForwardersPost',
      'apiForwardersIdPut',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [ForwarderFormComponent],
      providers: [
        provideRouter([]),
        { provide: ForwardersService, useValue: forwardersApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: convertToParamMap(forwarderId ? { id: forwarderId } : {}) },
          },
        },
      ],
    }).compileComponents();

    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    const fixture = TestBed.createComponent(ForwarderFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('create mode: does not load any forwarder', async () => {
    const fixture = await createComponent(null);

    expect(fixture.componentInstance.isEditMode).toBeFalse();
    expect(forwardersApiSpy.apiForwardersIdGet).not.toHaveBeenCalled();
  });

  it('edit mode: loads the existing forwarder and populates every field', async () => {
    const fixture = await createComponent('forwarder-1', () => {
      forwardersApiSpy.apiForwardersIdGet.and.returnValue(
        of({
          id: 'forwarder-1',
          name: 'cloudflare',
          definitionString: '1.1.1.1',
          sortOrder: 5,
          isDisabled: false,
        }),
      );
    });

    expect(fixture.componentInstance.form.controls.definitionString.value).toBe('1.1.1.1');
    expect(fixture.componentInstance.form.controls.sortOrder.value).toBe(5);
  });

  it('save() in create mode POSTs the built request and navigates back to the list', async () => {
    // '203.0.113.5' is an RFC 5737 TEST-NET-3 documentation address, deliberately not one of
    // KNOWN_FORWARDER_OPTIONS' own real public-resolver values -- this exercises the manually-
    // typed, single-line path (the patched 'name' is used verbatim), not the known-resolver
    // name-derivation path (covered by its own dedicated test below).
    const fixture = await createComponent(null, () => {
      forwardersApiSpy.apiForwardersPost.and.returnValue(of({ id: 'new-forwarder' }));
    });
    fixture.componentInstance.form.patchValue({
      name: 'cloudflare',
      definitionString: '203.0.113.5',
    });

    await fixture.componentInstance.save();

    expect(forwardersApiSpy.apiForwardersPost).toHaveBeenCalledWith({
      forwarderWriteRequest: jasmine.objectContaining({
        name: 'cloudflare',
        definitionString: '203.0.113.5',
      }),
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/forwarders']);
  });

  it("save() in create mode with a line matching a known resolver derives that resolver's own name, regardless of how the line was entered", async () => {
    const fixture = await createComponent(null, () => {
      forwardersApiSpy.apiForwardersPost.and.returnValue(of({ id: 'new-forwarder' }));
    });
    // Typing the exact value KNOWN_FORWARDER_OPTIONS lists for Cloudflare's plain endpoint is
    // indistinguishable, once it sits in the textarea, from having picked it off the dropdown --
    // both take the known option's own descriptive name, never the 'name' field (still required,
    // and still ignored here, to prove the known-value match wins).
    fixture.componentInstance.form.patchValue({ name: 'ignored', definitionString: '1.1.1.1' });

    await fixture.componentInstance.save();

    expect(forwardersApiSpy.apiForwardersPost).toHaveBeenCalledWith({
      forwarderWriteRequest: jasmine.objectContaining({
        name: 'Cloudflare (Plain)',
        definitionString: '1.1.1.1',
      }),
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/forwarders']);
  });

  it('appendLine() adds a known resolver value as a new textarea line without replacing existing content', async () => {
    const fixture = await createComponent(null);
    fixture.componentInstance.form.controls.definitionString.setValue('203.0.113.5');

    fixture.componentInstance.appendLine('tls://1.1.1.1');

    expect(fixture.componentInstance.form.controls.definitionString.value).toBe(
      '203.0.113.5\ntls://1.1.1.1',
    );
  });

  it('appendLine() skips an exact duplicate of an existing line', async () => {
    const fixture = await createComponent(null);
    fixture.componentInstance.form.controls.definitionString.setValue('1.1.1.1');

    fixture.componentInstance.appendLine('1.1.1.1');

    expect(fixture.componentInstance.form.controls.definitionString.value).toBe('1.1.1.1');
  });

  it('save() in create mode with multiple lines POSTs one forwarder per non-blank line, deriving a numbered name for each unmatched line', async () => {
    const fixture = await createComponent(null, () => {
      forwardersApiSpy.apiForwardersPost.and.returnValue(of({ id: 'created' }));
    });
    fixture.componentInstance.form.patchValue({
      name: 'custom',
      definitionString: '1.1.1.1\n203.0.113.5\n203.0.113.6',
    });

    await fixture.componentInstance.save();

    expect(forwardersApiSpy.apiForwardersPost).toHaveBeenCalledTimes(3);
    expect(forwardersApiSpy.apiForwardersPost.calls.argsFor(0)[0].forwarderWriteRequest).toEqual(
      jasmine.objectContaining({ name: 'Cloudflare (Plain)', definitionString: '1.1.1.1' }),
    );
    expect(forwardersApiSpy.apiForwardersPost.calls.argsFor(1)[0].forwarderWriteRequest).toEqual(
      jasmine.objectContaining({ name: 'custom 2', definitionString: '203.0.113.5' }),
    );
    expect(forwardersApiSpy.apiForwardersPost.calls.argsFor(2)[0].forwarderWriteRequest).toEqual(
      jasmine.objectContaining({ name: 'custom 3', definitionString: '203.0.113.6' }),
    );
    expect(navigateSpy).toHaveBeenCalledWith(['/forwarders']);
  });

  it('save() in create mode with multiple lines reports a partial failure clearly and keeps only the failed lines for retry', async () => {
    const fixture = await createComponent(null, () => {
      forwardersApiSpy.apiForwardersPost.and.callFake(
        (args: { forwarderWriteRequest: { definitionString: string } }) =>
          args.forwarderWriteRequest.definitionString === '203.0.113.5'
            ? throwError(
                () =>
                  new HttpErrorResponse({
                    status: 409,
                    error: "A forwarder named 'custom 2' already exists.",
                  }),
              )
            : of({ id: 'created' }),
      );
    });
    fixture.componentInstance.form.patchValue({
      name: 'custom',
      definitionString: '203.0.113.6\n203.0.113.5',
    });

    await fixture.componentInstance.save();

    expect(forwardersApiSpy.apiForwardersPost).toHaveBeenCalledTimes(2);
    expect(navigateSpy).not.toHaveBeenCalled();
    expect(fixture.componentInstance.errorMessage()).toContain('1 of 2 forwarders created');
    expect(fixture.componentInstance.errorMessage()).toContain('203.0.113.5');
    expect(fixture.componentInstance.errorMessage()).toContain(
      "A forwarder named 'custom 2' already exists.",
    );
    expect(fixture.componentInstance.form.controls.definitionString.value).toBe('203.0.113.5');
    expect(notificationSpy.success).toHaveBeenCalledWith('1 of 2 forwarders created.');
  });

  it('save() in edit mode PUTs the built request', async () => {
    const fixture = await createComponent('forwarder-1', () => {
      forwardersApiSpy.apiForwardersIdGet.and.returnValue(
        of({ id: 'forwarder-1', name: 'cloudflare', definitionString: '1.1.1.1' }),
      );
      forwardersApiSpy.apiForwardersIdPut.and.returnValue(of(undefined));
    });

    await fixture.componentInstance.save();

    expect(forwardersApiSpy.apiForwardersIdPut).toHaveBeenCalledWith({
      id: 'forwarder-1',
      forwarderWriteRequest: jasmine.objectContaining({ name: 'cloudflare' }),
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/forwarders']);
  });

  it('shows an error message and does not navigate when saving fails', async () => {
    const fixture = await createComponent(null, () => {
      forwardersApiSpy.apiForwardersPost.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 400,
              error: "'garbage' is not a valid forwarder definition.",
            }),
        ),
      );
    });
    fixture.componentInstance.form.patchValue({ name: 'x', definitionString: 'garbage' });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe(
      "'garbage' is not a valid forwarder definition.",
    );
    expect(navigateSpy).not.toHaveBeenCalled();
  });

  it('save() does nothing while the form is invalid', async () => {
    const fixture = await createComponent(null);
    fixture.componentInstance.form.patchValue({ name: '', definitionString: '' });

    await fixture.componentInstance.save();

    expect(forwardersApiSpy.apiForwardersPost).not.toHaveBeenCalled();
  });
});
