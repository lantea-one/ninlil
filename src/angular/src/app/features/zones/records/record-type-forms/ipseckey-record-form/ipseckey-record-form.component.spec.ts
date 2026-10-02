import { ComponentFixture, TestBed } from '@angular/core/testing';
import { IpSecKeyRecordFormComponent } from './ipseckey-record-form.component';

/**
 * These tests cover the IPSECKEY form: which gateway field shows for each gateway type, the null
 * sent for the other, and loading initialData. TestBed with the standalone component only.
 */
describe('IpSecKeyRecordFormComponent', () => {
  let fixture: ComponentFixture<IpSecKeyRecordFormComponent>;
  let component: IpSecKeyRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [IpSecKeyRecordFormComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(IpSecKeyRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts valid with gateway type 0 (no gateway) and shows neither gateway field', () => {
    expect(component.form.valid).toBeTrue();
    expect(component.showGatewayAddress()).toBeFalse();
    expect(component.showGatewayName()).toBeFalse();
  });

  it('shows the gateway address field for gateway type 1 and sends null for gatewayName', () => {
    component.form.controls.gatewayType.setValue(1);
    component.form.controls.gatewayAddressText.setValue('203.0.113.1');
    fixture.detectChanges();
    expect(component.showGatewayAddress()).toBeTrue();
    expect(component.showGatewayName()).toBeFalse();
    expect(component.getData()).toEqual({
      precedence: 10,
      gatewayType: 1,
      algorithm: 0,
      gatewayAddress: '203.0.113.1',
      gatewayName: null,
      publicKey: '',
    });
  });

  it('shows the gateway name field for gateway type 3 and sends null for gatewayAddress', () => {
    component.form.controls.gatewayType.setValue(3);
    component.form.controls.gatewayNameText.setValue('gw.example.com.');
    component.form.controls.algorithm.setValue(2);
    component.form.controls.publicKey.setValue('bW9jay1rZXk=');
    fixture.detectChanges();
    expect(component.showGatewayName()).toBeTrue();
    expect(component.getData()).toEqual({
      precedence: 10,
      gatewayType: 3,
      algorithm: 2,
      gatewayAddress: null,
      gatewayName: 'gw.example.com.',
      publicKey: 'bW9jay1rZXk=',
    });
  });

  it('patches the form from a supplied initialData input and re-derives the visible gateway field', () => {
    fixture.componentRef.setInput('initialData', {
      precedence: 5,
      gatewayType: 2,
      algorithm: 1,
      gatewayAddress: '2001:db8::1',
      gatewayName: null,
      publicKey: 'a2V5',
    });
    fixture.detectChanges();
    expect(component.showGatewayAddress()).toBeTrue();
    expect(component.form.controls.gatewayAddressText.value).toBe('2001:db8::1');
  });
});
