import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Nsec3RecordFormComponent } from './nsec3-record-form.component';
import { DnsResourceRecordTypeEnum } from '../../../../../api-client';
import { hexToBase64 } from '../record-data-encoding.util';

/**
 * These tests cover the NSEC3 form: validation, an empty salt, the hex fields sent as base64 and
 * decoded from initialData. TestBed with the standalone component only.
 */
describe('Nsec3RecordFormComponent', () => {
  let fixture: ComponentFixture<Nsec3RecordFormComponent>;
  let component: Nsec3RecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Nsec3RecordFormComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(Nsec3RecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with an empty next hashed owner name', () => {
    expect(component.form.valid).toBeFalse();
  });

  it('allows an empty salt (no salt configured)', () => {
    component.form.setValue({
      hashAlgorithm: 1,
      isOptOut: false,
      iterations: 10,
      saltHex: '',
      nextHashedOwnerNameHex: 'deadbeef',
      typesText: '',
    });
    expect(component.form.valid).toBeTrue();
    expect(component.getData().salt).toBe('');
  });

  it('accepts a full set and round-trips the hex fields as base64 through getData()', () => {
    component.form.setValue({
      hashAlgorithm: 1,
      isOptOut: true,
      iterations: 5,
      saltHex: 'aabb',
      nextHashedOwnerNameHex: 'deadbeef',
      typesText: 'A, RRSIG',
    });
    expect(component.getData()).toEqual({
      hashAlgorithm: 1,
      isOptOut: true,
      iterations: 5,
      salt: hexToBase64('aabb'),
      nextHashedOwnerName: hexToBase64('deadbeef'),
      types: [DnsResourceRecordTypeEnum.A, DnsResourceRecordTypeEnum.Rrsig],
    });
  });

  it('patches the form from a supplied initialData input, decoding base64 back to hex', () => {
    fixture.componentRef.setInput('initialData', {
      hashAlgorithm: 1,
      isOptOut: false,
      iterations: 3,
      salt: hexToBase64('1234'),
      nextHashedOwnerName: hexToBase64('5678'),
      types: [DnsResourceRecordTypeEnum.Ns],
    });
    fixture.detectChanges();
    expect(component.form.getRawValue()).toEqual({
      hashAlgorithm: 1,
      isOptOut: false,
      iterations: 3,
      saltHex: '1234',
      nextHashedOwnerNameHex: '5678',
      typesText: 'NS',
    });
  });
});
