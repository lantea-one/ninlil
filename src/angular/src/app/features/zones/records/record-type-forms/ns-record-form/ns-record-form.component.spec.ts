import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NsRecordFormComponent } from './ns-record-form.component';

describe('NsRecordFormComponent', () => {
  let fixture: ComponentFixture<NsRecordFormComponent>;
  let component: NsRecordFormComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [NsRecordFormComponent] }).compileComponents();
    fixture = TestBed.createComponent(NsRecordFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts invalid with an empty name server', () => {
    expect(component.form.valid).toBeFalse();
  });

  it('accepts a name server and round-trips it through getData()', () => {
    component.form.controls.nameServer.setValue('ns1.example.com.');
    expect(component.form.valid).toBeTrue();
    expect(component.getData()).toEqual({ nameServer: 'ns1.example.com.' });
  });

  it('patches the form from a supplied initialData input', () => {
    fixture.componentRef.setInput('initialData', { nameServer: 'ns2.example.com.' });
    fixture.detectChanges();
    expect(component.form.controls.nameServer.value).toBe('ns2.example.com.');
  });
});
