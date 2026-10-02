import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SshfpRecordFormComponent } from './sshfp-record-form.component';
import { hexToBase64 } from '../record-data-encoding.util';

/**
 * These tests cover the SSHFP form: defaults, validation, the hex fingerprint sent as base64 and
 * decoded from initialData. TestBed with the standalone component only.
 */
describe('SshfpRecordFormComponent', () => {
  let fixture: ComponentFixture<SshfpRecordFormComponent>;
  let component: SshfpRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SshfpRecordFormComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(SshfpRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with an empty fingerprint', () => {
    expect(component.form.valid).toBeFalse();
  });

  it('defaults algorithm to RSA and fingerprintType to SHA-256', () => {
    expect(component.form.getRawValue().algorithm).toBe(1);
    expect(component.form.getRawValue().fingerprintType).toBe(2);
  });

  it('accepts a hex fingerprint and round-trips it as base64 through getData()', () => {
    component.form.setValue({ algorithm: 4, fingerprintType: 2, fingerprintHex: 'deadbeef' });
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual({
      algorithm: 4,
      fingerprintType: 2,
      fingerprint: hexToBase64('deadbeef'),
    });
  });

  it('rejects an odd-length hex fingerprint', () => {
    component.form.setValue({ algorithm: 1, fingerprintType: 1, fingerprintHex: 'abc' });
    expect(component.form.valid).toBeFalse();
  });

  it('patches the form from a supplied initialData input, decoding base64 back to hex', () => {
    fixture.componentRef.setInput('initialData', {
      algorithm: 3,
      fingerprintType: 1,
      fingerprint: hexToBase64('cafebabe'),
    });
    fixture.detectChanges();
    expect(component.form.getRawValue()).toEqual({
      algorithm: 3,
      fingerprintType: 1,
      fingerprintHex: 'cafebabe',
    });
  });
});
