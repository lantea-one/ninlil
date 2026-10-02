import { ComponentFixture, TestBed } from '@angular/core/testing';
import { RpRecordFormComponent } from './rp-record-form.component';

/**
 * These tests cover the RP form: defaults, validation, getData() and loading initialData. TestBed
 * with the standalone component only.
 */
describe('RpRecordFormComponent', () => {
  let fixture: ComponentFixture<RpRecordFormComponent>;
  let component: RpRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [RpRecordFormComponent] }).compileComponents();
    fixture = TestBed.createComponent(RpRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('defaults textReference to the root name and starts invalid with an empty mailbox', () => {
    expect(component.form.controls.textReference.value).toBe('.');
    expect(component.form.valid).toBeFalse();
  });

  it('accepts a full set and round-trips it through getData()', () => {
    component.form.setValue({
      mailbox: 'admin.example.com.',
      textReference: 'info.example.com.',
    });
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual({
      mailbox: 'admin.example.com.',
      textReference: 'info.example.com.',
    });
  });

  it('patches the form from a supplied initialData input', () => {
    fixture.componentRef.setInput('initialData', {
      mailbox: 'other.example.com.',
      textReference: '.',
    });
    fixture.detectChanges();
    expect(component.form.getRawValue()).toEqual({
      mailbox: 'other.example.com.',
      textReference: '.',
    });
  });
});
