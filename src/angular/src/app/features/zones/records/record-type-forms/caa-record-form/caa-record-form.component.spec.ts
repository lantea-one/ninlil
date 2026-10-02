import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CaaRecordFormComponent } from './caa-record-form.component';

/**
 * These tests cover the CAA form: defaults, validation, getData() and loading initialData. TestBed
 * with the standalone component only.
 */
describe('CaaRecordFormComponent', () => {
  let fixture: ComponentFixture<CaaRecordFormComponent>;
  let component: CaaRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [CaaRecordFormComponent] }).compileComponents();
    fixture = TestBed.createComponent(CaaRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with an empty value', () => {
    expect(component.form.valid).toBeFalse();
  });

  it('defaults the tag to "issue" and isCritical to false', () => {
    expect(component.form.getRawValue().tag).toBe('issue');
    expect(component.form.getRawValue().isCritical).toBeFalse();
  });

  it('accepts a value and round-trips the full record through getData()', () => {
    component.form.setValue({ tag: 'issuewild', value: 'letsencrypt.org', isCritical: true });
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual({
      tag: 'issuewild',
      value: 'letsencrypt.org',
      isCritical: true,
    });
  });

  it('patches the form from a supplied initialData input', () => {
    fixture.componentRef.setInput('initialData', {
      tag: 'iodef',
      value: 'mailto:security@example.com',
      isCritical: false,
    });
    fixture.detectChanges();
    expect(component.form.getRawValue()).toEqual({
      tag: 'iodef',
      value: 'mailto:security@example.com',
      isCritical: false,
    });
  });
});
