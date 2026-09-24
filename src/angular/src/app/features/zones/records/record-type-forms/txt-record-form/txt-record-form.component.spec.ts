import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TxtRecordFormComponent } from './txt-record-form.component';

describe('TxtRecordFormComponent', () => {
  let fixture: ComponentFixture<TxtRecordFormComponent>;
  let component: TxtRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [TxtRecordFormComponent] }).compileComponents();
    fixture = TestBed.createComponent(TxtRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with no text entered', () => {
    expect(component.form.valid).toBeFalse();
  });

  it('splits one value per textarea line into the values array', () => {
    component.form.controls.valuesText.setValue('v=spf1 -all\nsecond-value');
    expect(component.getData()).toEqual({ values: ['v=spf1 -all', 'second-value'] });
  });

  it('drops blank lines rather than including empty string values', () => {
    component.form.controls.valuesText.setValue('first\n\n\nsecond');
    expect(component.getData()).toEqual({ values: ['first', 'second'] });
  });

  it('joins an existing values array back into newline-separated text via initialData', () => {
    fixture.componentRef.setInput('initialData', { values: ['one', 'two'] });
    fixture.detectChanges();
    expect(component.form.controls.valuesText.value).toBe('one\ntwo');
  });
});
