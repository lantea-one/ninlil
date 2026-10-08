import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { DnssecAlgorithmEnum, DnssecKeyRoleEnum, DnssecKeysService } from '../../../../api-client';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { SIGNING_PROHIBITED_DNSSEC_ALGORITHMS } from '../dnssec-key.model';
import { DnssecKeyFormComponent } from './dnssec-key-form.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the DNSSEC key form: generate-mode defaults, the RSA-only key size, the POST
 * and PUT it sends in each mode, and save errors. TestBed with a spy-backed `DnssecKeysService`,
 * `NotificationService` and a stubbed ActivatedRoute for the `keyId` parameter.
 */
describe('DnssecKeyFormComponent', () => {
  let dnssecKeysApiSpy: {
    apiZonesZoneIdDnssecKeysIdGet: jasmine.Spy;
    apiZonesZoneIdDnssecKeysPost: jasmine.Spy;
    apiZonesZoneIdDnssecKeysIdPut: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let navigateSpy: jasmine.Spy;

  async function createComponent(keyId: string | null, configureApis?: () => void) {
    dnssecKeysApiSpy = jasmine.createSpyObj('DnssecKeysService', [
      'apiZonesZoneIdDnssecKeysIdGet',
      'apiZonesZoneIdDnssecKeysPost',
      'apiZonesZoneIdDnssecKeysIdPut',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [DnssecKeyFormComponent],
      providers: [
        provideRouter([]),
        { provide: DnssecKeysService, useValue: dnssecKeysApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: convertToParamMap(
                keyId ? { zoneId: 'zone-1', keyId } : { zoneId: 'zone-1' },
              ),
            },
          },
        },
      ],
    }).compileComponents();

    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    const fixture = TestBed.createComponent(DnssecKeyFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('generate mode: defaults to ZSK/ECDSAP256SHA256, with no owning key to load', async () => {
    const fixture = await createComponent(null);

    expect(fixture.componentInstance.isEditMode).toBeFalse();
    expect(dnssecKeysApiSpy.apiZonesZoneIdDnssecKeysIdGet).not.toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.role.value).toBe(DnssecKeyRoleEnum.Zsk);
  });

  it('offers none of the algorithms the server refuses to sign with', async () => {
    const fixture = await createComponent(null);
    const options = fixture.componentInstance.algorithmOptions;
    const rendered = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('#dnssec-key-algorithm option'),
    ).map((option) => option.textContent?.trim());

    for (const algorithm of SIGNING_PROHIBITED_DNSSEC_ALGORITHMS) {
      expect(options).not.toContain(algorithm);
      expect(rendered).not.toContain(algorithm);
    }
    expect(rendered).toContain(DnssecAlgorithmEnum.Ecdsap256Sha256);
  });

  it('showKeySize() is true only for an RSA-family algorithm', async () => {
    const fixture = await createComponent(null);

    expect(fixture.componentInstance.showKeySize()).toBeFalse();
    fixture.componentInstance.form.controls.algorithm.setValue(DnssecAlgorithmEnum.Rsasha256);
    expect(fixture.componentInstance.showKeySize()).toBeTrue();
  });

  it("save() in generate mode POSTs the built request and navigates to the zone's own key list", async () => {
    const fixture = await createComponent(null, () => {
      dnssecKeysApiSpy.apiZonesZoneIdDnssecKeysPost.and.returnValue(of({ id: 'key-1' }));
    });
    fixture.componentInstance.form.patchValue({ role: DnssecKeyRoleEnum.Ksk });

    await fixture.componentInstance.save();

    expect(dnssecKeysApiSpy.apiZonesZoneIdDnssecKeysPost).toHaveBeenCalledWith({
      zoneId: 'zone-1',
      dnssecKeyCreateRequest: jasmine.objectContaining({
        role: DnssecKeyRoleEnum.Ksk,
        algorithm: DnssecAlgorithmEnum.Ecdsap256Sha256,
        keySizeBits: 0,
      }),
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/zones', 'zone-1', 'dnssec-keys']);
  });

  it('save() in generate mode includes a non-zero keySizeBits only for an RSA algorithm', async () => {
    const fixture = await createComponent(null, () => {
      dnssecKeysApiSpy.apiZonesZoneIdDnssecKeysPost.and.returnValue(of({ id: 'key-1' }));
    });
    fixture.componentInstance.form.patchValue({
      algorithm: DnssecAlgorithmEnum.Rsasha256,
      keySizeBits: 4096,
    });

    await fixture.componentInstance.save();

    expect(dnssecKeysApiSpy.apiZonesZoneIdDnssecKeysPost).toHaveBeenCalledWith({
      zoneId: 'zone-1',
      dnssecKeyCreateRequest: jasmine.objectContaining({ keySizeBits: 4096 }),
    });
  });

  it('edit mode: loads the existing key as read-only context and only exposes isRetired/comments as writable', async () => {
    const fixture = await createComponent('key-1', () => {
      dnssecKeysApiSpy.apiZonesZoneIdDnssecKeysIdGet.and.returnValue(
        of({
          id: 'key-1',
          role: DnssecKeyRoleEnum.Ksk,
          algorithm: DnssecAlgorithmEnum.Ecdsap256Sha256,
          keyTag: 54321,
          isRetired: false,
        }),
      );
    });

    expect(fixture.componentInstance.isEditMode).toBeTrue();
    expect(fixture.componentInstance.existingRole()).toBe(DnssecKeyRoleEnum.Ksk);
    expect(fixture.componentInstance.existingKeyTag()).toBe(54321);
  });

  it('save() in edit mode PUTs only isRetired/comments, then navigates to the key list', async () => {
    const fixture = await createComponent('key-1', () => {
      dnssecKeysApiSpy.apiZonesZoneIdDnssecKeysIdGet.and.returnValue(
        of({
          id: 'key-1',
          role: DnssecKeyRoleEnum.Zsk,
          algorithm: DnssecAlgorithmEnum.Ecdsap256Sha256,
        }),
      );
      dnssecKeysApiSpy.apiZonesZoneIdDnssecKeysIdPut.and.returnValue(of(undefined));
    });
    fixture.componentInstance.form.patchValue({ isRetired: true, comments: 'superseded' });

    await fixture.componentInstance.save();

    expect(dnssecKeysApiSpy.apiZonesZoneIdDnssecKeysIdPut).toHaveBeenCalledWith({
      zoneId: 'zone-1',
      id: 'key-1',
      dnssecKeyWriteRequest: { isRetired: true, comments: 'superseded' },
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/zones', 'zone-1', 'dnssec-keys']);
  });

  it('shows an error message and does not navigate when saving fails', async () => {
    const fixture = await createComponent(null, () => {
      dnssecKeysApiSpy.apiZonesZoneIdDnssecKeysPost.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'unsupported algorithm' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('unsupported algorithm');
    expect(navigateSpy).not.toHaveBeenCalled();
  });
});
