import { ComponentFixture, TestBed } from '@angular/core/testing';
import { LocRecordFormComponent } from './loc-record-form.component';

describe('LocRecordFormComponent', () => {
  let fixture: ComponentFixture<LocRecordFormComponent>;
  let component: LocRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [LocRecordFormComponent] }).compileComponents();
    fixture = TestBed.createComponent(LocRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts valid with its own default values', () => {
    expect(component.form.valid).toBeTrue();
  });

  it('accepts a full set and round-trips it through getData()', () => {
    const value = {
      latitudeDegrees: 42.3601,
      longitudeDegrees: -71.0589,
      altitudeMeters: 15,
      diameterMeters: 1,
      horizontalPrecisionMeters: 5000,
      verticalPrecisionMeters: 10,
    };
    component.form.setValue(value);
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual(value);
  });

  it('rejects a latitude outside the -90 to 90 range', () => {
    component.form.controls.latitudeDegrees.setValue(200);
    expect(component.form.valid).toBeFalse();
  });

  it('patches the form from a supplied initialData input', () => {
    const value = {
      latitudeDegrees: 1,
      longitudeDegrees: 2,
      altitudeMeters: 3,
      diameterMeters: 4,
      horizontalPrecisionMeters: 5,
      verticalPrecisionMeters: 6,
    };
    fixture.componentRef.setInput('initialData', value);
    fixture.detectChanges();
    expect(component.form.getRawValue()).toEqual(value);
  });
});
