import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SrvRecordFormComponent } from './srv-record-form.component';

describe('SrvRecordFormComponent', () => {
  let fixture: ComponentFixture<SrvRecordFormComponent>;
  let component: SrvRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [SrvRecordFormComponent] }).compileComponents();
    fixture = TestBed.createComponent(SrvRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with an empty target host', () => {
    expect(component.form.valid).toBeFalse();
  });

  it('accepts a full priority/weight/port/target set and round-trips it through getData()', () => {
    component.form.setValue({ priority: 10, weight: 60, port: 5060, target: 'sip.example.com.' });
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual({
      priority: 10,
      weight: 60,
      port: 5060,
      target: 'sip.example.com.',
    });
  });

  it('rejects a port outside the 0-65535 range', () => {
    component.form.setValue({ priority: 0, weight: 0, port: 99999, target: 'sip.example.com.' });
    expect(component.form.valid).toBeFalse();
  });

  it('patches the form from a supplied initialData input', () => {
    fixture.componentRef.setInput('initialData', {
      priority: 1,
      weight: 2,
      port: 3,
      target: 'other.example.com.',
    });
    fixture.detectChanges();
    expect(component.form.getRawValue()).toEqual({
      priority: 1,
      weight: 2,
      port: 3,
      target: 'other.example.com.',
    });
  });
});
