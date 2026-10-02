import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Eui48RecordFormComponent } from './eui48-record-form.component';
import { macToBase64 } from '../record-data-encoding.util';

/**
 * These tests cover the EUI48 form: MAC validation, the base64 round trip and loading initialData.
 * TestBed with the standalone component only.
 */
describe('Eui48RecordFormComponent', () => {
  let fixture: ComponentFixture<Eui48RecordFormComponent>;
  let component: Eui48RecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Eui48RecordFormComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(Eui48RecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with an empty identifier', () => {
    expect(component.form.valid).toBeFalse();
  });

  it('accepts a well-formed MAC address and round-trips it as base64 through getData()', () => {
    component.form.controls.identifierMac.setValue('00:1a:2b:3c:4d:5e');
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual({ identifier: macToBase64('00:1a:2b:3c:4d:5e') });
  });

  it('rejects a malformed MAC address', () => {
    component.form.controls.identifierMac.setValue('not-a-mac');
    expect(component.form.valid).toBeFalse();
  });

  it('patches the form from a supplied initialData input, decoding base64 back to MAC form', () => {
    fixture.componentRef.setInput('initialData', {
      identifier: macToBase64('aa:bb:cc:dd:ee:ff'),
    });
    fixture.detectChanges();
    expect(component.form.controls.identifierMac.value).toBe('aa:bb:cc:dd:ee:ff');
  });
});
