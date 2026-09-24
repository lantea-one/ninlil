import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { PresetsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { PresetListComponent } from './preset-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('PresetListComponent', () => {
  let presetsApiSpy: { apiPresetsGet: jasmine.Spy; apiPresetsNameApplyPost: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    presetsApiSpy = jasmine.createSpyObj('PresetsService', [
      'apiPresetsGet',
      'apiPresetsNameApplyPost',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    presetsApiSpy.apiPresetsGet.and.returnValue(
      of([
        {
          name: 'Baseline',
          description: 'NIST SP 800-81 Rev. 3 baseline.',
          configurationSettingCount: 3,
          filterListSubscriptionCount: 0,
        },
      ]),
    );

    await TestBed.configureTestingModule({
      imports: [PresetListComponent],
      providers: [
        { provide: PresetsService, useValue: presetsApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(PresetListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads every preset on init', async () => {
    const fixture = await createComponent();

    expect(presetsApiSpy.apiPresetsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.presets().length).toBe(1);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('apply() does nothing when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent();

    const applyPromise = fixture.componentInstance.apply(fixture.componentInstance.presets()[0]);
    fixture.componentInstance.onConfirmDialogResolved(false);
    await applyPromise;

    expect(presetsApiSpy.apiPresetsNameApplyPost).not.toHaveBeenCalled();
  });

  it('apply() applies the preset once confirmed, and surfaces the backend own restart-required note', async () => {
    const fixture = await createComponent();
    presetsApiSpy.apiPresetsNameApplyPost.and.returnValue(
      of({
        name: 'Baseline',
        appliedAtUtc: '2026-08-30T00:00:00Z',
        note: 'Applied. Settings bound via IOptionsMonitor take effect immediately; every other setting this preset touched needs a restart to take effect.',
      }),
    );

    const applyPromise = fixture.componentInstance.apply(fixture.componentInstance.presets()[0]);
    fixture.componentInstance.onConfirmDialogResolved(true);
    await applyPromise;

    expect(presetsApiSpy.apiPresetsNameApplyPost).toHaveBeenCalledWith({ name: 'Baseline' });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('needs a restart to take effect'),
    );
  });

  it('shows an error message when applying fails', async () => {
    const fixture = await createComponent();
    presetsApiSpy.apiPresetsNameApplyPost.and.returnValue(
      throwError(
        () => new HttpErrorResponse({ status: 404, error: "No preset named 'x' exists." }),
      ),
    );

    const applyPromise = fixture.componentInstance.apply(fixture.componentInstance.presets()[0]);
    fixture.componentInstance.onConfirmDialogResolved(true);
    await applyPromise;

    expect(fixture.componentInstance.errorMessage()).toBe("No preset named 'x' exists.");
  });
});
