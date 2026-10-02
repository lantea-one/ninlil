import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AfsdbRecordFormComponent } from './afsdb-record-form.component';

/**
 * These tests cover the AFSDB form: validation, getData() and loading initialData. TestBed with the
 * standalone component only.
 */
describe('AfsdbRecordFormComponent', () => {
  let fixture: ComponentFixture<AfsdbRecordFormComponent>;
  let component: AfsdbRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AfsdbRecordFormComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(AfsdbRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with an empty hostname', () => {
    expect(component.form.valid).toBeFalse();
  });

  it('accepts a subtype/hostname pair and round-trips it through getData()', () => {
    component.form.setValue({ subtype: 2, hostname: 'dce.example.com.' });
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual({ subtype: 2, hostname: 'dce.example.com.' });
  });

  it('patches the form from a supplied initialData input', () => {
    fixture.componentRef.setInput('initialData', { subtype: 1, hostname: 'afs.example.com.' });
    fixture.detectChanges();
    expect(component.form.getRawValue()).toEqual({ subtype: 1, hostname: 'afs.example.com.' });
  });
});
