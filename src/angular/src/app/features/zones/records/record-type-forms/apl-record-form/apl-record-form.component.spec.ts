import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AplRecordFormComponent } from './apl-record-form.component';
import { parseCidr } from './apl-cidr.util';

/**
 * These tests cover the APL form: adding and removing prefix rows, CIDR validation, the data
 * getData() returns and loading initialData. TestBed with the standalone component only.
 */
describe('AplRecordFormComponent', () => {
  let fixture: ComponentFixture<AplRecordFormComponent>;
  let component: AplRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AplRecordFormComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(AplRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts valid with an empty item list', () => {
    expect(component.form.valid).toBeTrue();
    expect(component.items.length).toBe(0);
  });

  it('adds and removes a prefix row', () => {
    component.addItem();
    expect(component.items.length).toBe(1);
    component.removeItem(0);
    expect(component.items.length).toBe(0);
  });

  it('converts a CIDR prefix into address-family/prefix-length/trimmed-bytes shape through getData()', () => {
    component.addItem();
    component.items.at(0).patchValue({ cidr: '192.0.2.0/24', negated: false });

    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual({
      items: [
        {
          addressFamily: 1,
          prefixLength: 24,
          negated: false,
          addressFamilyDependentPart: parseCidr('192.0.2.0/24').addressFamilyDependentPart,
        },
      ],
    });
  });

  it('rejects a malformed CIDR prefix', () => {
    component.addItem();
    component.items.at(0).patchValue({ cidr: 'not-a-cidr', negated: false });
    expect(component.form.valid).toBeFalse();
  });

  it('patches the form from a supplied initialData input, formatting bytes back into CIDR text', () => {
    const parts = parseCidr('2001:db8::/32');
    fixture.componentRef.setInput('initialData', {
      items: [
        {
          addressFamily: parts.addressFamily,
          prefixLength: parts.prefixLength,
          negated: true,
          addressFamilyDependentPart: parts.addressFamilyDependentPart,
        },
      ],
    });
    fixture.detectChanges();

    expect(component.items.length).toBe(1);
    expect(component.items.at(0).getRawValue()).toEqual({
      cidr: '2001:db8:0:0:0:0:0:0/32',
      negated: true,
    });
  });

  it('shows an item with an unknown address family read-only and sends it back unchanged', () => {
    const unknown = {
      addressFamily: 3,
      prefixLength: 16,
      negated: true,
      addressFamilyDependentPart: btoa(String.fromCharCode(0x0a, 0x01)),
    };
    const known = parseCidr('192.0.2.0/24');
    fixture.componentRef.setInput('initialData', {
      items: [
        unknown,
        {
          addressFamily: known.addressFamily,
          prefixLength: known.prefixLength,
          negated: false,
          addressFamilyDependentPart: known.addressFamilyDependentPart,
        },
      ],
    });
    fixture.detectChanges();

    const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(
      '#apl-cidr-0',
    );
    expect(input?.value).toBe('family 3, prefix 16, 0a01');
    expect(input?.disabled).toBeTrue();
    expect(component.form.valid).toBeTrue();
    expect(component.getData().items[0]).toEqual(unknown);
    expect(component.getData().items[1].addressFamily).toBe(1);
  });
});
