import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AaaaRecordFormComponent } from './aaaa-record-form.component';

/**
 * These tests cover the AAAA form: address validation, getData() and loading initialData. TestBed
 * with the standalone component only.
 */
describe('AaaaRecordFormComponent', () => {
  let fixture: ComponentFixture<AaaaRecordFormComponent>;
  let component: AaaaRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AaaaRecordFormComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(AaaaRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with an empty address', () => {
    expect(component.form.valid).toBeFalse();
  });

  it('accepts a well-formed IPv6 address and round-trips it through getData()', () => {
    component.form.controls.address.setValue('2001:db8::1');
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual({ address: '2001:db8::1' });
  });

  it('rejects a value containing characters no IPv6 address can have', () => {
    component.form.controls.address.setValue('not-an-address!');
    expect(component.form.valid).toBeFalse();
  });

  it('patches the form from a supplied initialData input', () => {
    fixture.componentRef.setInput('initialData', { address: 'fe80::1' });
    fixture.detectChanges();
    expect(component.form.controls.address.value).toBe('fe80::1');
  });
});
