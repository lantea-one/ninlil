import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CertRecordFormComponent } from './cert-record-form.component';
import { DnssecAlgorithmEnum } from '../../../../../api-client';

describe('CertRecordFormComponent', () => {
  let fixture: ComponentFixture<CertRecordFormComponent>;
  let component: CertRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CertRecordFormComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(CertRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with an empty certificate and defaults algorithm to "(none)"', () => {
    expect(component.form.valid).toBeFalse();
    expect(component.form.getRawValue().algorithm).toBe(component.noAlgorithmValue);
  });

  it('sends null for algorithm when "(none)" remains selected', () => {
    component.form.setValue({
      certificateType: 3,
      keyTag: 0,
      algorithm: component.noAlgorithmValue,
      certificate: 'bW9jay1jZXJ0',
    });
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual({
      certificateType: 3,
      keyTag: 0,
      algorithm: null,
      certificate: 'bW9jay1jZXJ0',
    });
  });

  it('sends the selected algorithm when one is chosen', () => {
    component.form.setValue({
      certificateType: 1,
      keyTag: 12345,
      algorithm: DnssecAlgorithmEnum.Ecdsap256Sha256,
      certificate: 'bW9jay1jZXJ0',
    });
    expect(component.getData().algorithm).toBe(DnssecAlgorithmEnum.Ecdsap256Sha256);
  });

  it('patches the form from a supplied initialData input with a null algorithm', () => {
    fixture.componentRef.setInput('initialData', {
      certificateType: 1,
      keyTag: 1,
      algorithm: null,
      certificate: 'YW5vdGhlcg==',
    });
    fixture.detectChanges();
    expect(component.form.getRawValue().algorithm).toBe(component.noAlgorithmValue);
  });
});
