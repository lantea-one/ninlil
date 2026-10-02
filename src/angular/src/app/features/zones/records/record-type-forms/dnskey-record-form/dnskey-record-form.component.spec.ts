import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DnsKeyRecordFormComponent } from './dnskey-record-form.component';
import { DnssecAlgorithmEnum } from '../../../../../api-client';

/**
 * These tests cover the DNSKEY/CDNSKEY form: defaults, validation, getData() and loading
 * initialData. TestBed with the standalone component only.
 */
describe('DnsKeyRecordFormComponent', () => {
  let fixture: ComponentFixture<DnsKeyRecordFormComponent>;
  let component: DnsKeyRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DnsKeyRecordFormComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(DnsKeyRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with an empty public key, defaulting isZoneKey true and the rest false', () => {
    expect(component.form.valid).toBeFalse();
    expect(component.form.getRawValue().isZoneKey).toBeTrue();
    expect(component.form.getRawValue().isSecureEntryPoint).toBeFalse();
    expect(component.form.getRawValue().isRevoked).toBeFalse();
  });

  it('accepts a full set and round-trips it through getData()', () => {
    const value = {
      isZoneKey: true,
      isSecureEntryPoint: true,
      algorithm: DnssecAlgorithmEnum.Ed25519,
      publicKey: 'bW9jay1wdWJsaWMta2V5',
      isRevoked: false,
    };
    component.form.setValue(value);
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual(value);
  });

  it('patches the form from a supplied initialData input', () => {
    const value = {
      isZoneKey: false,
      isSecureEntryPoint: false,
      algorithm: DnssecAlgorithmEnum.Rsasha256,
      publicKey: 'YW5vdGhlci1rZXk=',
      isRevoked: true,
    };
    fixture.componentRef.setInput('initialData', value);
    fixture.detectChanges();
    expect(component.form.getRawValue()).toEqual(value);
  });
});
