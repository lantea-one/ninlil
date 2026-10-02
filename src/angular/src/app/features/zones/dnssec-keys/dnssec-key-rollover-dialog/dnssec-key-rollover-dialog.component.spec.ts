import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import {
  DnssecAlgorithmEnum,
  DnssecKeyDetail,
  DnssecKeyLifecycleStateEnum,
  DnssecKeyRoleEnum,
  DnssecKeysService,
} from '../../../../api-client';
import { DnssecKeyRolloverDialogComponent } from './dnssec-key-rollover-dialog.component';

const KSK: DnssecKeyDetail = {
  id: 'key-1',
  zoneId: 'zone-1',
  role: DnssecKeyRoleEnum.Ksk,
  algorithm: DnssecAlgorithmEnum.Ecdsap256Sha256,
  keyTag: 12345,
  lifecycleState: DnssecKeyLifecycleStateEnum.Active,
};

/**
 * These tests cover the rollover dialog: the request it sends with and without a new algorithm or
 * key size, closing and emitting the replacement on success, and showing a refusal. TestBed with a
 * spy-backed `DnssecKeysService`.
 */
describe('DnssecKeyRolloverDialogComponent', () => {
  let apiSpy: { apiZonesZoneIdDnssecKeysIdRollPost: jasmine.Spy };

  async function createComponent(key: DnssecKeyDetail = KSK) {
    apiSpy = jasmine.createSpyObj('DnssecKeysService', ['apiZonesZoneIdDnssecKeysIdRollPost']);

    await TestBed.configureTestingModule({
      imports: [DnssecKeyRolloverDialogComponent],
      providers: [{ provide: DnssecKeysService, useValue: apiSpy }],
    }).compileComponents();

    const fixture = TestBed.createComponent(DnssecKeyRolloverDialogComponent);
    fixture.componentRef.setInput('zoneId', 'zone-1');
    fixture.componentRef.setInput('key', key);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    return fixture;
  }

  it('keeps the current algorithm by default, sending no algorithm at all', async () => {
    const fixture = await createComponent();
    apiSpy.apiZonesZoneIdDnssecKeysIdRollPost.and.returnValue(of({ id: 'key-2' }));

    await fixture.componentInstance.submit();

    expect(apiSpy.apiZonesZoneIdDnssecKeysIdRollPost).toHaveBeenCalledWith({
      zoneId: 'zone-1',
      id: 'key-1',
      dnssecKeyRolloverRequest: { keySizeBits: 0 },
    });
  });

  it('sends a newly chosen algorithm, performing an algorithm rollover', async () => {
    const fixture = await createComponent();
    apiSpy.apiZonesZoneIdDnssecKeysIdRollPost.and.returnValue(of({ id: 'key-2' }));

    fixture.componentInstance.form.controls.algorithm.setValue(DnssecAlgorithmEnum.Ed25519);
    await fixture.componentInstance.submit();

    expect(apiSpy.apiZonesZoneIdDnssecKeysIdRollPost).toHaveBeenCalledWith({
      zoneId: 'zone-1',
      id: 'key-1',
      dnssecKeyRolloverRequest: { algorithm: DnssecAlgorithmEnum.Ed25519, keySizeBits: 0 },
    });
  });

  it('asks for a key size only when the new key will be RSA', async () => {
    const fixture = await createComponent();
    const keySizeField = () => fixture.nativeElement.querySelector('#dnssec-rollover-key-size');

    expect(fixture.componentInstance.showKeySize()).toBeFalse();

    fixture.componentInstance.form.controls.algorithm.setValue(DnssecAlgorithmEnum.Rsasha256);
    fixture.detectChanges();

    expect(fixture.componentInstance.showKeySize()).toBeTrue();
    expect(keySizeField()).not.toBeNull();
  });

  it('sends the key size for an RSA rollover', async () => {
    const fixture = await createComponent();
    apiSpy.apiZonesZoneIdDnssecKeysIdRollPost.and.returnValue(of({ id: 'key-2' }));

    fixture.componentInstance.form.setValue({
      algorithm: DnssecAlgorithmEnum.Rsasha256,
      keySizeBits: 4096,
    });
    await fixture.componentInstance.submit();

    expect(apiSpy.apiZonesZoneIdDnssecKeysIdRollPost.calls.mostRecent().args[0]).toEqual(
      jasmine.objectContaining({
        dnssecKeyRolloverRequest: { algorithm: DnssecAlgorithmEnum.Rsasha256, keySizeBits: 4096 },
      }),
    );
  });

  it('emits the replacement and closes once the rollover has started', async () => {
    const fixture = await createComponent();
    apiSpy.apiZonesZoneIdDnssecKeysIdRollPost.and.returnValue(of({ id: 'key-2', keyTag: 7 }));
    const rolled = jasmine.createSpy('rolled');
    const visibleChange = jasmine.createSpy('visibleChange');
    fixture.componentInstance.rolled.subscribe(rolled);
    fixture.componentInstance.visibleChange.subscribe(visibleChange);

    await fixture.componentInstance.submit();

    expect(rolled).toHaveBeenCalledWith({ id: 'key-2', keyTag: 7 });
    expect(visibleChange).toHaveBeenCalledWith(false);
  });

  it('stays open and shows the error when the rollover is refused', async () => {
    const fixture = await createComponent();
    apiSpy.apiZonesZoneIdDnssecKeysIdRollPost.and.returnValue(
      throwError(
        () => new HttpErrorResponse({ status: 409, error: 'rollover already in progress' }),
      ),
    );
    const rolled = jasmine.createSpy('rolled');
    fixture.componentInstance.rolled.subscribe(rolled);

    await fixture.componentInstance.submit();

    expect(fixture.componentInstance.errorMessage()).toBe('rollover already in progress');
    expect(rolled).not.toHaveBeenCalled();
  });

  it('tells the operator a KSK needs its DS record handed to the parent', async () => {
    const fixture = await createComponent();

    expect(fixture.componentInstance.scheme()).toBe('Double-Signature');
    expect(fixture.nativeElement.textContent).toContain('give the parent zone its DS record');
  });
});
