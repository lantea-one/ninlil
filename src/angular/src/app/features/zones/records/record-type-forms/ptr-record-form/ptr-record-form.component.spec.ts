import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PtrRecordFormComponent } from './ptr-record-form.component';

/**
 * These tests cover the PTR form: validation, getData() and loading initialData. TestBed with the
 * standalone component only.
 */
describe('PtrRecordFormComponent', () => {
  let fixture: ComponentFixture<PtrRecordFormComponent>;
  let component: PtrRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [PtrRecordFormComponent] }).compileComponents();
    fixture = TestBed.createComponent(PtrRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with an empty target', () => {
    expect(component.form.valid).toBeFalse();
  });

  it('accepts a target and round-trips it through getData()', () => {
    component.form.controls.target.setValue('host.example.com.');
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual({ target: 'host.example.com.' });
  });

  it('patches the form from a supplied initialData input', () => {
    fixture.componentRef.setInput('initialData', { target: 'other.example.com.' });
    fixture.detectChanges();
    expect(component.form.controls.target.value).toBe('other.example.com.');
  });
});
