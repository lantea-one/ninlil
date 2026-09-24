import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SvcbHttpsRecordFormComponent } from './svcb-https-record-form.component';
import { hexToBase64 } from '../record-data-encoding.util';

describe('SvcbHttpsRecordFormComponent', () => {
  let fixture: ComponentFixture<SvcbHttpsRecordFormComponent>;
  let component: SvcbHttpsRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SvcbHttpsRecordFormComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(SvcbHttpsRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts valid with priority 0 (alias form) and no parameters', () => {
    expect(component.form.valid).toBeTrue();
    expect(component.parameters.length).toBe(0);
  });

  it('adds and removes a connection-parameter row', () => {
    component.addParameter();
    expect(component.parameters.length).toBe(1);
    component.removeParameter(0);
    expect(component.parameters.length).toBe(0);
  });

  it('round-trips priority/targetName/parameters through getData(), hex-encoding each value', () => {
    component.addParameter();
    component.parameters.at(0).patchValue({ key: 1, valueHex: '026832' });
    component.form.patchValue({ priority: 1, targetName: 'svc.example.com.' });

    expect(component.getData()).toEqual({
      priority: 1,
      targetName: 'svc.example.com.',
      parameters: [{ key: 1, value: hexToBase64('026832') }],
    });
  });

  it('patches the form from a supplied initialData input, decoding each value back to hex', () => {
    fixture.componentRef.setInput('initialData', {
      priority: 1,
      targetName: 'svc.example.com.',
      parameters: [{ key: 3, value: hexToBase64('0050') }],
    });
    fixture.detectChanges();

    expect(component.form.controls.priority.value).toBe(1);
    expect(component.parameters.length).toBe(1);
    expect(component.parameters.at(0).getRawValue()).toEqual({ key: 3, valueHex: '0050' });
  });
});
