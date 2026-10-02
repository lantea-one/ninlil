import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DnameRecordFormComponent } from './dname-record-form.component';

/**
 * These tests cover the DNAME form: validation, getData() and loading initialData. TestBed with the
 * standalone component only.
 */
describe('DnameRecordFormComponent', () => {
  let fixture: ComponentFixture<DnameRecordFormComponent>;
  let component: DnameRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DnameRecordFormComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(DnameRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with an empty target', () => {
    expect(component.form.valid).toBeFalse();
  });

  it('accepts a target and round-trips it through getData()', () => {
    component.form.controls.target.setValue('new-location.example.com.');
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual({ target: 'new-location.example.com.' });
  });

  it('patches the form from a supplied initialData input', () => {
    fixture.componentRef.setInput('initialData', { target: 'other.example.com.' });
    fixture.detectChanges();
    expect(component.form.controls.target.value).toBe('other.example.com.');
  });
});
