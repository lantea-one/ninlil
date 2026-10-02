import { TestBed } from '@angular/core/testing';
import { PermissionPickerComponent } from './permission-picker.component';

/**
 * These tests cover the permission picker: grouping, checked state, the toggle event and
 * checkbox ids. TestBed with inputs set on the fixture.
 */
describe('PermissionPickerComponent', () => {
  async function createComponent(
    allPermissions: string[],
    selectedPermissions: string[],
    disabled = false,
  ) {
    await TestBed.configureTestingModule({
      imports: [PermissionPickerComponent],
    }).compileComponents();

    const fixture = TestBed.createComponent(PermissionPickerComponent);
    fixture.componentRef.setInput('allPermissions', allPermissions);
    fixture.componentRef.setInput('selectedPermissions', selectedPermissions);
    fixture.componentRef.setInput('disabled', disabled);
    fixture.detectChanges();
    return fixture;
  }

  it('groups the supplied permissions for display', async () => {
    const fixture = await createComponent(['zones:read', 'zones:write', 'roles:read'], []);

    expect(fixture.componentInstance.groups()).toEqual([
      { resource: 'roles', permissions: ['roles:read'] },
      { resource: 'zones', permissions: ['zones:read', 'zones:write'] },
    ]);
  });

  it('isChecked() reports true only for a permission present in selectedPermissions', async () => {
    const fixture = await createComponent(['zones:read', 'zones:write'], ['zones:read']);

    expect(fixture.componentInstance.isChecked('zones:read')).toBeTrue();
    expect(fixture.componentInstance.isChecked('zones:write')).toBeFalse();
  });

  it('onChange() emits a toggled event carrying the permission and the new checked state', async () => {
    const fixture = await createComponent(['zones:read'], []);
    const emitted: { permission: string; checked: boolean }[] = [];
    fixture.componentInstance.toggled.subscribe((event) => emitted.push(event));

    fixture.componentInstance.onChange('zones:read', {
      target: { checked: true },
    } as unknown as Event);

    expect(emitted).toEqual([{ permission: 'zones:read', checked: true }]);
  });

  it('checkboxId() replaces every non-alphanumeric character with a hyphen', async () => {
    const fixture = await createComponent(['zones:read'], []);

    expect(fixture.componentInstance.checkboxId('zones:read')).toBe('permission-zones-read');
  });

  it('renders one checkbox per permission, checked according to selectedPermissions', async () => {
    const fixture = await createComponent(['zones:read', 'zones:write'], ['zones:read']);

    const checkboxes = fixture.nativeElement.querySelectorAll('input[cFormCheckInput]');
    expect(checkboxes.length).toBe(2);
  });
});
