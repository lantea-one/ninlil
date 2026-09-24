import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DsRecordFormComponent } from './ds-record-form.component';
import { DnssecAlgorithmEnum } from '../../../../../api-client';
import { hexToBase64 } from '../record-data-encoding.util';

describe('DsRecordFormComponent', () => {
  let fixture: ComponentFixture<DsRecordFormComponent>;
  let component: DsRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [DsRecordFormComponent] }).compileComponents();
    fixture = TestBed.createComponent(DsRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with an empty digest', () => {
    expect(component.form.valid).toBeFalse();
  });

  it('accepts a full set and round-trips the hex digest as base64 through getData()', () => {
    component.form.setValue({
      keyTag: 12345,
      algorithm: DnssecAlgorithmEnum.Rsasha256,
      digestType: 2,
      digestHex: 'deadbeefcafefeed',
    });
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual({
      keyTag: 12345,
      algorithm: DnssecAlgorithmEnum.Rsasha256,
      digestType: 2,
      digest: hexToBase64('deadbeefcafefeed'),
    });
  });

  it('rejects an odd-length hex digest', () => {
    component.form.controls.digestHex.setValue('abc');
    expect(component.form.valid).toBeFalse();
  });

  it('patches the form from a supplied initialData input, decoding base64 back to hex', () => {
    fixture.componentRef.setInput('initialData', {
      keyTag: 999,
      algorithm: DnssecAlgorithmEnum.Ed25519,
      digestType: 4,
      digest: hexToBase64('aabbccdd'),
    });
    fixture.detectChanges();
    expect(component.form.getRawValue()).toEqual({
      keyTag: 999,
      algorithm: DnssecAlgorithmEnum.Ed25519,
      digestType: 4,
      digestHex: 'aabbccdd',
    });
  });
});
