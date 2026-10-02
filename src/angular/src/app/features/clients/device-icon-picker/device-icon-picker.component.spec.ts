import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { IconSetService } from '@coreui/icons-angular';
import { DnsClientDeviceIconEnum } from '../../../api-client';
import { DEVICE_ICON_CATALOG } from '../shared/device-icon.model';
import { findDeviceIcon } from '../shared/device-icon.util';
import { DeviceIconPickerComponent } from './device-icon-picker.component';

/** A minimal host binding the picker to a real reactive-forms control. */
@Component({
  imports: [ReactiveFormsModule, DeviceIconPickerComponent],
  template: `<app-device-icon-picker [formControl]="control" [suggestedKey]="suggested" />`,
})
class HostComponent {
  control = new FormControl<DnsClientDeviceIconEnum | null>(null);
  suggested: DnsClientDeviceIconEnum | null = null;
}

/**
 * These tests cover the icon picker: the catalog matches the backend enum and uses real CoreUI
 * icons, the form value round trip, "Automatic" and its suggestion, and the disabled state.
 * TestBed with the picker in a reactive form.
 */
describe('DeviceIconPickerComponent', () => {
  async function createHost(configure?: (host: HostComponent) => void) {
    await TestBed.configureTestingModule({ imports: [HostComponent] }).compileComponents();
    const fixture = TestBed.createComponent(HostComponent);
    configure?.(fixture.componentInstance);
    fixture.detectChanges();
    return fixture;
  }

  it('offers every backend icon key exactly once', () => {
    const catalogKeys = DEVICE_ICON_CATALOG.map((option) => option.key);

    expect([...catalogKeys].sort()).toEqual(Object.values(DnsClientDeviceIconEnum).sort());
    expect(new Set(catalogKeys).size).toBe(catalogKeys.length);
  });

  it('draws only icons this app actually registers with CoreUI', async () => {
    await createHost();
    // The test bed does not run the app.config.ts icon registration, so check the names against
    // the @coreui/icons exports.
    const icons = await import('@coreui/icons');
    for (const option of DEVICE_ICON_CATALOG) {
      expect((icons as Record<string, unknown>)[option.icon])
        .withContext(option.icon)
        .toBeDefined();
    }
    expect(TestBed.inject(IconSetService)).toBeTruthy();
  });

  it('reflects the bound value and reports a picked icon back to the form', async () => {
    const fixture = await createHost((host) => host.control.setValue('Printer'));
    const element: HTMLElement = fixture.nativeElement;

    expect(element.querySelector('[data-icon-key="Printer"]')?.getAttribute('aria-checked')).toBe(
      'true',
    );

    (element.querySelector('[data-icon-key="Router"]') as HTMLButtonElement).click();
    expect(fixture.componentInstance.control.value).toBe('Router');
    expect(fixture.componentInstance.control.touched).toBeTrue();
  });

  it('maps "Automatic" to null and names the vendor suggestion on it', async () => {
    const fixture = await createHost((host) => {
      host.control.setValue('Laptop');
      host.suggested = 'Speaker';
    });
    const element: HTMLElement = fixture.nativeElement;
    const automatic = element.querySelector('.icon-tile') as HTMLButtonElement;

    expect(automatic.textContent).toContain('Speaker');
    automatic.click();
    expect(fixture.componentInstance.control.value).toBeNull();
  });

  it('ignores clicks while the control is disabled', async () => {
    const fixture = await createHost((host) => host.control.disable());

    (
      fixture.nativeElement.querySelector('[data-icon-key="Television"]') as HTMLButtonElement
    ).click();

    expect(fixture.componentInstance.control.value).toBeNull();
  });

  it('findDeviceIcon resolves a key and tolerates an unset one', () => {
    expect(findDeviceIcon('Camera')?.icon).toBe('cilCamera');
    expect(findDeviceIcon(null)).toBeUndefined();
    expect(findDeviceIcon(undefined)).toBeUndefined();
  });
});
