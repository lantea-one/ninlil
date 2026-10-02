import { ComponentFixture, TestBed } from '@angular/core/testing';
import { UriRecordFormComponent } from './uri-record-form.component';

/**
 * These tests cover the URI form: validation, getData() and loading initialData. TestBed with the
 * standalone component only.
 */
describe('UriRecordFormComponent', () => {
  let fixture: ComponentFixture<UriRecordFormComponent>;
  let component: UriRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [UriRecordFormComponent] }).compileComponents();
    fixture = TestBed.createComponent(UriRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with an empty target', () => {
    expect(component.form.valid).toBeFalse();
  });

  it('accepts a full set and round-trips it through getData()', () => {
    component.form.setValue({ priority: 1, weight: 5, target: 'https://example.com/' });
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual({
      priority: 1,
      weight: 5,
      target: 'https://example.com/',
    });
  });

  it('rejects a weight outside the 0-65535 range', () => {
    component.form.setValue({ priority: 0, weight: -1, target: 'https://example.com/' });
    expect(component.form.valid).toBeFalse();
  });

  it('patches the form from a supplied initialData input', () => {
    fixture.componentRef.setInput('initialData', {
      priority: 2,
      weight: 3,
      target: 'https://other.example.com/',
    });
    fixture.detectChanges();
    expect(component.form.getRawValue()).toEqual({
      priority: 2,
      weight: 3,
      target: 'https://other.example.com/',
    });
  });
});
