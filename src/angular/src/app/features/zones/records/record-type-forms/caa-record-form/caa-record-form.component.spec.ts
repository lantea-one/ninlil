import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CAA_PROPERTY_TAGS, CaaRecordFormComponent } from './caa-record-form.component';

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

  /** This helper returns the tag select's option texts. */
  function optionTexts(): string[] {
    const options: HTMLOptionElement[] = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('#caa-tag option'),
    );
    return options.map((option) => option.textContent?.trim() ?? '');
  }

  it('offers every in-use tag of the IANA CAA property registry', () => {
    expect(optionTexts()).toEqual([...CAA_PROPERTY_TAGS]);
    expect(optionTexts()).toContain('issuemail');
  });

  it('loads and saves a record with the issuemail tag unchanged', () => {
    fixture.componentRef.setInput('initialData', {
      tag: 'issuemail',
      value: 'ca.example.net',
      isCritical: false,
    });
    fixture.detectChanges();

    const select = (fixture.nativeElement as HTMLElement).querySelector<HTMLSelectElement>(
      '#caa-tag',
    );
    expect(select?.value).toBe('issuemail');
    expect(component.getData().tag).toBe('issuemail');
  });

  it('adds an unregistered tag as an extra option and saves the record unchanged', () => {
    fixture.componentRef.setInput('initialData', {
      tag: 'future-tag',
      value: 'x',
      isCritical: true,
    });
    fixture.detectChanges();

    const select = (fixture.nativeElement as HTMLElement).querySelector<HTMLSelectElement>(
      '#caa-tag',
    );
    expect(optionTexts()).toEqual([...CAA_PROPERTY_TAGS, 'future-tag']);
    expect(select?.value).toBe('future-tag');
    expect(component.getData()).toEqual({ tag: 'future-tag', value: 'x', isCritical: true });
  });
});
