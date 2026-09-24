import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ARecordFormComponent } from './a-record-form.component';

describe('ARecordFormComponent', () => {
  let fixture: ComponentFixture<ARecordFormComponent>;
  let component: ARecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ARecordFormComponent] }).compileComponents();
    fixture = TestBed.createComponent(ARecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with an empty address', () => {
    expect(component.form.valid).toBeFalse();
  });

  it('accepts a well-formed IPv4 address and round-trips it through getData()', () => {
    component.form.controls.address.setValue('203.0.113.5');
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual({ address: '203.0.113.5' });
  });

  it('rejects a non-IPv4-shaped value', () => {
    component.form.controls.address.setValue('not-an-address');
    expect(component.form.valid).toBeFalse();
  });

  it('patches the form from a supplied initialData input, for editing an existing record', () => {
    fixture.componentRef.setInput('initialData', { address: '198.51.100.7' });
    fixture.detectChanges();
    expect(component.form.controls.address.value).toBe('198.51.100.7');
  });
});
