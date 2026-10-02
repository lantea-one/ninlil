import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SoaRecordFormComponent } from './soa-record-form.component';

/**
 * These tests cover the SOA form: defaults, validation including the 32-bit bound, getData() and
 * loading initialData. TestBed with the standalone component only.
 */
describe('SoaRecordFormComponent', () => {
  let fixture: ComponentFixture<SoaRecordFormComponent>;
  let component: SoaRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [SoaRecordFormComponent] }).compileComponents();
    fixture = TestBed.createComponent(SoaRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with an empty primary name server and mailbox', () => {
    expect(component.form.valid).toBeFalse();
  });

  it('defaults refresh/retry/expire/minimum TTL to sensible RFC 1035-style values', () => {
    const value = component.form.getRawValue();
    expect(value.refreshSeconds).toBe(3600);
    expect(value.retrySeconds).toBe(900);
    expect(value.expireSeconds).toBe(1209600);
    expect(value.minimumTtlSeconds).toBe(3600);
  });

  it('becomes valid once the two required name fields are set, and round-trips through getData()', () => {
    component.form.patchValue({
      primaryNameServer: 'ns1.example.com.',
      responsiblePersonMailbox: 'admin.example.com.',
    });
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual({
      primaryNameServer: 'ns1.example.com.',
      responsiblePersonMailbox: 'admin.example.com.',
      serial: 1,
      refreshSeconds: 3600,
      retrySeconds: 900,
      expireSeconds: 1209600,
      minimumTtlSeconds: 3600,
    });
  });

  it('rejects a serial value above 4294967295, the real 32-bit unsigned backend bound', () => {
    component.form.controls.serial.setValue(4294967296);
    expect(component.form.controls.serial.hasError('max')).toBeTrue();

    component.form.controls.serial.setValue(4294967295);
    expect(component.form.controls.serial.hasError('max')).toBeFalse();
  });

  it('patches every field from a supplied initialData input, for editing an existing SOA', () => {
    fixture.componentRef.setInput('initialData', {
      primaryNameServer: 'ns1.example.com.',
      responsiblePersonMailbox: 'admin.example.com.',
      serial: 42,
      refreshSeconds: 7200,
      retrySeconds: 1800,
      expireSeconds: 2419200,
      minimumTtlSeconds: 300,
    });
    fixture.detectChanges();
    expect(component.form.getRawValue().serial).toBe(42);
    expect(component.form.getRawValue().minimumTtlSeconds).toBe(300);
  });
});
