import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Nsec3ParamRecordFormComponent } from './nsec3param-record-form.component';
import { hexToBase64 } from '../record-data-encoding.util';

/**
 * These tests cover the NSEC3PARAM form: defaults, salt validation, the hex salt sent as base64 and
 * decoded from initialData. TestBed with the standalone component only.
 */
describe('Nsec3ParamRecordFormComponent', () => {
  let fixture: ComponentFixture<Nsec3ParamRecordFormComponent>;
  let component: Nsec3ParamRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Nsec3ParamRecordFormComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(Nsec3ParamRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts valid with its own default values (an empty salt is legitimate)', () => {
    expect(component.form.valid).toBeTrue();
  });

  it('accepts a full set and round-trips the hex salt as base64 through getData()', () => {
    component.form.setValue({ hashAlgorithm: 1, iterations: 10, saltHex: 'aabbcc' });
    expect(component.getData()).toEqual({
      hashAlgorithm: 1,
      iterations: 10,
      salt: hexToBase64('aabbcc'),
    });
  });

  it('rejects an odd-length hex salt', () => {
    component.form.controls.saltHex.setValue('abc');
    expect(component.form.valid).toBeFalse();
  });

  it('patches the form from a supplied initialData input, decoding base64 back to hex', () => {
    fixture.componentRef.setInput('initialData', {
      hashAlgorithm: 1,
      iterations: 15,
      salt: hexToBase64('deadbeef'),
    });
    fixture.detectChanges();
    expect(component.form.getRawValue()).toEqual({
      hashAlgorithm: 1,
      iterations: 15,
      saltHex: 'deadbeef',
    });
  });
});
