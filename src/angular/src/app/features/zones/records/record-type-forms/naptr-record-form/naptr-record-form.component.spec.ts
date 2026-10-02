import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NaptrRecordFormComponent } from './naptr-record-form.component';

/**
 * These tests cover the NAPTR form: defaults, range validation, getData() and loading initialData.
 * TestBed with the standalone component only.
 */
describe('NaptrRecordFormComponent', () => {
  let fixture: ComponentFixture<NaptrRecordFormComponent>;
  let component: NaptrRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [NaptrRecordFormComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(NaptrRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('defaults replacement to the root name and starts valid with its own other defaults', () => {
    expect(component.form.controls.replacement.value).toBe('.');
    expect(component.form.valid).toBeTrue();
  });

  it('accepts a full set and round-trips it through getData()', () => {
    const value = {
      order: 100,
      preference: 10,
      flags: 'U',
      services: 'E2U+sip',
      regularExpression: '!^.*$!sip:info@example.com!',
      replacement: '.',
    };
    component.form.setValue(value);
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual(value);
  });

  it('rejects an order outside the 0-65535 range', () => {
    component.form.controls.order.setValue(-1);
    expect(component.form.valid).toBeFalse();
  });

  it('patches the form from a supplied initialData input', () => {
    const value = {
      order: 1,
      preference: 2,
      flags: 'S',
      services: 'SIP+D2U',
      regularExpression: '',
      replacement: '_sip._udp.example.com.',
    };
    fixture.componentRef.setInput('initialData', value);
    fixture.detectChanges();
    expect(component.form.getRawValue()).toEqual(value);
  });
});
