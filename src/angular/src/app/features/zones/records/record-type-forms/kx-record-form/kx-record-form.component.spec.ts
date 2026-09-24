import { ComponentFixture, TestBed } from '@angular/core/testing';
import { KxRecordFormComponent } from './kx-record-form.component';

describe('KxRecordFormComponent', () => {
  let fixture: ComponentFixture<KxRecordFormComponent>;
  let component: KxRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [KxRecordFormComponent] }).compileComponents();
    fixture = TestBed.createComponent(KxRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with an empty exchanger', () => {
    expect(component.form.valid).toBeFalse();
  });

  it('accepts a preference/exchanger pair and round-trips it through getData()', () => {
    component.form.setValue({ preference: 5, exchanger: 'kx.example.com.' });
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual({ preference: 5, exchanger: 'kx.example.com.' });
  });

  it('rejects a preference outside the 0-65535 range', () => {
    component.form.setValue({ preference: -1, exchanger: 'kx.example.com.' });
    expect(component.form.valid).toBeFalse();
  });

  it('patches the form from a supplied initialData input', () => {
    fixture.componentRef.setInput('initialData', {
      preference: 1,
      exchanger: 'other.example.com.',
    });
    fixture.detectChanges();
    expect(component.form.getRawValue()).toEqual({
      preference: 1,
      exchanger: 'other.example.com.',
    });
  });
});
