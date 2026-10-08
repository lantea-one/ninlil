import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { DnssecAlgorithmEnum, DnssecPolicySettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { SIGNING_PROHIBITED_DNSSEC_ALGORITHMS } from '../../zones/dnssec-keys/dnssec-key.model';
import { DnssecPolicySettingsComponent } from './dnssec-policy-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the DNSSEC Policy page: loading the allowlist, toggling an algorithm, saving
 * and a save error. TestBed with a spy-backed settings service and `NotificationService`.
 */
describe('DnssecPolicySettingsComponent', () => {
  let apiSpy: {
    apiDnssecPolicySettingsGet: jasmine.Spy;
    apiDnssecPolicySettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('DnssecPolicySettingsService', [
      'apiDnssecPolicySettingsGet',
      'apiDnssecPolicySettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiDnssecPolicySettingsGet.and.returnValue(of({ allowedAlgorithms: [] }));
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [DnssecPolicySettingsComponent],
      providers: [
        { provide: DnssecPolicySettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(DnssecPolicySettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective allowlist on init', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiDnssecPolicySettingsGet.and.returnValue(
        of({ allowedAlgorithms: [DnssecAlgorithmEnum.Rsasha256] }),
      );
    });

    expect(apiSpy.apiDnssecPolicySettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.isSelected(DnssecAlgorithmEnum.Rsasha256)).toBeTrue();
    expect(fixture.componentInstance.isSelected(DnssecAlgorithmEnum.Ed25519)).toBeFalse();
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('offers no checkbox for an algorithm the server refuses to sign with', async () => {
    const fixture = await createComponent();
    const rendered = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('input[id^="dnssec-policy-"]'),
    ).map((input) => input.id.replace('dnssec-policy-', ''));

    for (const algorithm of SIGNING_PROHIBITED_DNSSEC_ALGORITHMS) {
      expect(fixture.componentInstance.algorithmOptions).not.toContain(algorithm);
      expect(rendered).not.toContain(algorithm);
    }
    expect(rendered).toContain(DnssecAlgorithmEnum.Ed25519);
  });

  it('drops a prohibited algorithm that is already in the stored allowlist', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiDnssecPolicySettingsGet.and.returnValue(
        of({ allowedAlgorithms: [DnssecAlgorithmEnum.Rsasha1, DnssecAlgorithmEnum.Ed25519] }),
      );
    });

    expect(fixture.componentInstance.isSelected(DnssecAlgorithmEnum.Rsasha1)).toBeFalse();
    expect(fixture.componentInstance.isSelected(DnssecAlgorithmEnum.Ed25519)).toBeTrue();
  });

  it('toggle() flips a single algorithm', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.toggle(DnssecAlgorithmEnum.Ed25519);
    expect(fixture.componentInstance.isSelected(DnssecAlgorithmEnum.Ed25519)).toBeTrue();

    fixture.componentInstance.toggle(DnssecAlgorithmEnum.Ed25519);
    expect(fixture.componentInstance.isSelected(DnssecAlgorithmEnum.Ed25519)).toBeFalse();
  });

  it('save() PATCHes the selected algorithms, then surfaces the hot-reloaded note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiDnssecPolicySettingsPatch.and.returnValue(
        of({ note: 'Written to the configuration file. ... no restart required.' }),
      );
    });

    fixture.componentInstance.toggle(DnssecAlgorithmEnum.Rsasha256);
    await fixture.componentInstance.save();

    expect(apiSpy.apiDnssecPolicySettingsPatch).toHaveBeenCalledWith({
      body: { allowedAlgorithms: [DnssecAlgorithmEnum.Rsasha256] },
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('no restart required'),
    );
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiDnssecPolicySettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });
});
