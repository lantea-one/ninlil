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

/**
 * These tests cover the forwarder form: create and edit, appending lines, naming known and typed
 * lines, bulk create with partial failure, errors and validation. TestBed with a spy-backed
 * `ForwardersService` and a stubbed route.
 */
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
    // 203.0.113.5 is an RFC 5737 documentation address, not a known resolver, so this tests the
    // typed single-line path, which uses the 'name' field. The known-resolver naming has its own
    // test below.
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
    // A typed line equal to Cloudflare's plain endpoint looks the same as a picked one, so it
    // takes the known option's name; the 'name' field is filled in only to show it is ignored.
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

  it('save() retrying the failed line of a three-line bulk create reuses the number the line had, not its new position', async () => {
    let failSecond = true;
    const fixture = await createComponent(null, () => {
      forwardersApiSpy.apiForwardersPost.and.callFake(
        (args: { forwarderWriteRequest: { definitionString: string } }) =>
          args.forwarderWriteRequest.definitionString === '203.0.113.2' && failSecond
            ? throwError(() => new HttpErrorResponse({ status: 500, error: 'boom' }))
            : of({ id: 'created' }),
      );
    });
    fixture.componentInstance.form.patchValue({
      name: 'custom',
      definitionString: '203.0.113.1\n203.0.113.2\n203.0.113.3',
    });
    await fixture.componentInstance.save();
    expect(fixture.componentInstance.form.controls.definitionString.value).toBe('203.0.113.2');

    failSecond = false;
    forwardersApiSpy.apiForwardersPost.calls.reset();
    await fixture.componentInstance.save();

    expect(forwardersApiSpy.apiForwardersPost).toHaveBeenCalledTimes(1);
    expect(forwardersApiSpy.apiForwardersPost).toHaveBeenCalledWith({
      forwarderWriteRequest: jasmine.objectContaining({
        name: 'custom 2',
        definitionString: '203.0.113.2',
      }),
    });
  });

  it('save() gives a line added before a retry the next unused number', async () => {
    const fixture = await createComponent(null, () => {
      forwardersApiSpy.apiForwardersPost.and.callFake(
        (args: { forwarderWriteRequest: { definitionString: string } }) =>
          args.forwarderWriteRequest.definitionString === '203.0.113.2'
            ? throwError(() => new HttpErrorResponse({ status: 500, error: 'boom' }))
            : of({ id: 'created' }),
      );
    });
    fixture.componentInstance.form.patchValue({
      name: 'custom',
      definitionString: '203.0.113.1\n203.0.113.2',
    });
    await fixture.componentInstance.save();

    fixture.componentInstance.form.patchValue({ definitionString: '203.0.113.2\n203.0.113.9' });
    forwardersApiSpy.apiForwardersPost.calls.reset();
    await fixture.componentInstance.save();

    const names = forwardersApiSpy.apiForwardersPost.calls
      .allArgs()
      .map(
        (args: unknown[]) =>
          (args[0] as { forwarderWriteRequest: { name: string } }).forwarderWriteRequest.name,
      );
    expect(names).toEqual(['custom 2', 'custom 3']);
  });
});
