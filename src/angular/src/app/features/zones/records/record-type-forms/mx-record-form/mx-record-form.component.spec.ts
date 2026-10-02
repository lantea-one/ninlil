import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MxRecordFormComponent } from './mx-record-form.component';

/**
 * These tests cover the MX form: validation, getData() and loading initialData. TestBed with the
 * standalone component only.
 */
describe('MxRecordFormComponent', () => {
  let fixture: ComponentFixture<MxRecordFormComponent>;
  let component: MxRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [MxRecordFormComponent] }).compileComponents();
    fixture = TestBed.createComponent(MxRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with an empty exchange host', () => {
    expect(component.form.valid).toBeFalse();
  });

  it('accepts a preference/exchange pair and round-trips it through getData()', () => {
    component.form.setValue({ preference: 10, exchange: 'mail.example.com.' });
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual({ preference: 10, exchange: 'mail.example.com.' });
  });

  it('rejects a preference outside the 0-65535 range', () => {
    component.form.setValue({ preference: 70000, exchange: 'mail.example.com.' });
    expect(component.form.valid).toBeFalse();
  });

  it('patches the form from a supplied initialData input', () => {
    fixture.componentRef.setInput('initialData', {
      preference: 20,
      exchange: 'backup.example.com.',
    });
    fixture.detectChanges();
    expect(component.form.getRawValue()).toEqual({
      preference: 20,
      exchange: 'backup.example.com.',
    });
  });
});
