import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TlsaRecordFormComponent } from './tlsa-record-form.component';
import { hexToBase64 } from '../record-data-encoding.util';

describe('TlsaRecordFormComponent', () => {
  let fixture: ComponentFixture<TlsaRecordFormComponent>;
  let component: TlsaRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TlsaRecordFormComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(TlsaRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with empty certificate association data', () => {
    expect(component.form.valid).toBeFalse();
  });

  it('accepts a full set and round-trips the hex data as base64 through getData()', () => {
    component.form.setValue({
      certificateUsage: 3,
      selector: 1,
      matchingType: 1,
      certificateAssociationDataHex: 'deadbeefcafe',
    });
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual({
      certificateUsage: 3,
      selector: 1,
      matchingType: 1,
      certificateAssociationData: hexToBase64('deadbeefcafe'),
    });
  });

  it('rejects an odd-length hex value', () => {
    component.form.controls.certificateAssociationDataHex.setValue('abc');
    expect(component.form.valid).toBeFalse();
  });

  it('patches the form from a supplied initialData input, decoding base64 back to hex', () => {
    fixture.componentRef.setInput('initialData', {
      certificateUsage: 0,
      selector: 0,
      matchingType: 0,
      certificateAssociationData: hexToBase64('aabbcc'),
    });
    fixture.detectChanges();
    expect(component.form.getRawValue()).toEqual({
      certificateUsage: 0,
      selector: 0,
      matchingType: 0,
      certificateAssociationDataHex: 'aabbcc',
    });
  });
});
