import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HinfoRecordFormComponent } from './hinfo-record-form.component';

describe('HinfoRecordFormComponent', () => {
  let fixture: ComponentFixture<HinfoRecordFormComponent>;
  let component: HinfoRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HinfoRecordFormComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(HinfoRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with empty fields', () => {
    expect(component.form.valid).toBeFalse();
  });

  it('accepts a cpu/operatingSystem pair and round-trips it through getData()', () => {
    component.form.setValue({ cpu: 'ARM64', operatingSystem: 'Linux' });
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual({ cpu: 'ARM64', operatingSystem: 'Linux' });
  });

  it('patches the form from a supplied initialData input', () => {
    fixture.componentRef.setInput('initialData', { cpu: 'x86_64', operatingSystem: 'FreeBSD' });
    fixture.detectChanges();
    expect(component.form.getRawValue()).toEqual({ cpu: 'x86_64', operatingSystem: 'FreeBSD' });
  });
});
