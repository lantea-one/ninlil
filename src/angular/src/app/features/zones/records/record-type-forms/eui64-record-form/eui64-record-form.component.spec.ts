import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Eui64RecordFormComponent } from './eui64-record-form.component';
import { macToBase64 } from '../record-data-encoding.util';

describe('Eui64RecordFormComponent', () => {
  let fixture: ComponentFixture<Eui64RecordFormComponent>;
  let component: Eui64RecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Eui64RecordFormComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(Eui64RecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with an empty identifier', () => {
    expect(component.form.valid).toBeFalse();
  });

  it('accepts a well-formed 8-octet address and round-trips it as base64 through getData()', () => {
    component.form.controls.identifierMac.setValue('00:1a:2b:3c:4d:5e:6f:70');
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual({
      identifier: macToBase64('00:1a:2b:3c:4d:5e:6f:70'),
    });
  });

  it('rejects a 6-octet (EUI48-shaped) address', () => {
    component.form.controls.identifierMac.setValue('00:1a:2b:3c:4d:5e');
    expect(component.form.valid).toBeFalse();
  });

  it('patches the form from a supplied initialData input, decoding base64 back to hex form', () => {
    fixture.componentRef.setInput('initialData', {
      identifier: macToBase64('aa:bb:cc:dd:ee:ff:00:11'),
    });
    fixture.detectChanges();
    expect(component.form.controls.identifierMac.value).toBe('aa:bb:cc:dd:ee:ff:00:11');
  });
});
