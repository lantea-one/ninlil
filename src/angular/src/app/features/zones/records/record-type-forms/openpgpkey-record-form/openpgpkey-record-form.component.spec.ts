import { ComponentFixture, TestBed } from '@angular/core/testing';
import { OpenPgpKeyRecordFormComponent } from './openpgpkey-record-form.component';

describe('OpenPgpKeyRecordFormComponent', () => {
  let fixture: ComponentFixture<OpenPgpKeyRecordFormComponent>;
  let component: OpenPgpKeyRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [OpenPgpKeyRecordFormComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(OpenPgpKeyRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with empty key data', () => {
    expect(component.form.valid).toBeFalse();
  });

  it('accepts base64 key data and round-trips it through getData()', () => {
    component.form.controls.keyData.setValue('bW9jay1wZ3Ata2V5');
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual({ keyData: 'bW9jay1wZ3Ata2V5' });
  });

  it('patches the form from a supplied initialData input', () => {
    fixture.componentRef.setInput('initialData', { keyData: 'YW5vdGhlci1rZXk=' });
    fixture.detectChanges();
    expect(component.form.controls.keyData.value).toBe('YW5vdGhlci1rZXk=');
  });
});
