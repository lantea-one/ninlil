import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NsecRecordFormComponent } from './nsec-record-form.component';
import { DnsResourceRecordTypeEnum } from '../../../../../api-client';

/**
 * These tests cover the NSEC form: validation, parsing the types text into an array and loading
 * initialData. TestBed with the standalone component only.
 */
describe('NsecRecordFormComponent', () => {
  let fixture: ComponentFixture<NsecRecordFormComponent>;
  let component: NsecRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [NsecRecordFormComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(NsecRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with an empty next domain name', () => {
    expect(component.form.valid).toBeFalse();
  });

  it('parses a comma/space-separated types field into an array through getData()', () => {
    component.form.setValue({
      nextDomainName: 'next.example.com.',
      typesText: 'A, AAAA  RRSIG',
    });
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual({
      nextDomainName: 'next.example.com.',
      types: [
        DnsResourceRecordTypeEnum.A,
        DnsResourceRecordTypeEnum.Aaaa,
        DnsResourceRecordTypeEnum.Rrsig,
      ],
    });
  });

  it('produces an empty types array when the types field is blank', () => {
    component.form.setValue({ nextDomainName: 'next.example.com.', typesText: '' });
    expect(component.getData()).toEqual({ nextDomainName: 'next.example.com.', types: [] });
  });

  it('patches the form from a supplied initialData input, joining types with commas', () => {
    fixture.componentRef.setInput('initialData', {
      nextDomainName: 'other.example.com.',
      types: [DnsResourceRecordTypeEnum.A, DnsResourceRecordTypeEnum.Mx],
    });
    fixture.detectChanges();
    expect(component.form.getRawValue()).toEqual({
      nextDomainName: 'other.example.com.',
      typesText: 'A, MX',
    });
  });
});
