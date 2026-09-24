import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CnameRecordFormComponent } from './cname-record-form.component';

describe('CnameRecordFormComponent', () => {
  let fixture: ComponentFixture<CnameRecordFormComponent>;
  let component: CnameRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CnameRecordFormComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(CnameRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with an empty target', () => {
    expect(component.form.valid).toBeFalse();
  });

  it('accepts a target and round-trips it through getData()', () => {
    component.form.controls.target.setValue('www.example.com.');
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual({ target: 'www.example.com.' });
  });

  it('patches the form from a supplied initialData input', () => {
    fixture.componentRef.setInput('initialData', { target: 'alias.example.com.' });
    fixture.detectChanges();
    expect(component.form.controls.target.value).toBe('alias.example.com.');
  });
});
