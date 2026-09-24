import { ComponentFixture, TestBed } from '@angular/core/testing';
import { RrSigRecordFormComponent } from './rrsig-record-form.component';
import { DnsResourceRecordTypeEnum, DnssecAlgorithmEnum } from '../../../../../api-client';

describe('RrSigRecordFormComponent', () => {
  let fixture: ComponentFixture<RrSigRecordFormComponent>;
  let component: RrSigRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RrSigRecordFormComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(RrSigRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with an empty signer name and signature', () => {
    expect(component.form.valid).toBeFalse();
  });

  it('accepts a full set and round-trips it through getData()', () => {
    const value = {
      typeCovered: DnsResourceRecordTypeEnum.A,
      algorithm: DnssecAlgorithmEnum.Ecdsap256Sha256,
      labels: 2,
      originalTimeToLiveSeconds: 3600,
      signatureExpiration: 1893456000,
      signatureInception: 1861920000,
      keyTag: 12345,
      signerName: 'example.com.',
      signature: 'bW9jay1zaWduYXR1cmU=',
    };
    component.form.setValue(value);
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual(value);
  });

  it('rejects a labels value above 255, the real byte-sized backend bound', () => {
    component.form.controls.labels.setValue(256);
    expect(component.form.controls.labels.hasError('max')).toBeTrue();

    component.form.controls.labels.setValue(255);
    expect(component.form.controls.labels.hasError('max')).toBeFalse();
  });

  it('rejects a timestamp field above 4294967295, the real 32-bit unsigned backend bound', () => {
    component.form.controls.signatureExpiration.setValue(4294967296);
    expect(component.form.controls.signatureExpiration.hasError('max')).toBeTrue();

    component.form.controls.signatureExpiration.setValue(4294967295);
    expect(component.form.controls.signatureExpiration.hasError('max')).toBeFalse();
  });

  it('patches the form from a supplied initialData input', () => {
    const value = {
      typeCovered: DnsResourceRecordTypeEnum.Mx,
      algorithm: DnssecAlgorithmEnum.Rsasha256,
      labels: 1,
      originalTimeToLiveSeconds: 7200,
      signatureExpiration: 100,
      signatureInception: 50,
      keyTag: 1,
      signerName: 'other.example.com.',
      signature: 'YW5vdGhlcg==',
    };
    fixture.componentRef.setInput('initialData', value);
    fixture.detectChanges();
    expect(component.form.getRawValue()).toEqual(value);
  });
});
