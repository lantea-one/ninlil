import { DnsClientDeviceIconEnum } from '../../../api-client';
import { DEVICE_ICON_CATALOG, DeviceIconOption } from './device-icon.model';

/**
 * This function finds a key's entry in `DEVICE_ICON_CATALOG`, or `undefined` for an unset or
 * unknown key. The picker and the client list both use it, so the mapping lives in one place.
 */
export function findDeviceIcon(
  key: DnsClientDeviceIconEnum | null | undefined,
): DeviceIconOption | undefined {
  return key ? DEVICE_ICON_CATALOG.find((option) => option.key === key) : undefined;
}

/**
 * This function picks the icon a device is drawn with: the operator's choice, else the backend's
 * vendor-based suggestion, else the generic device. This is the order the picker's "Automatic"
 * tile describes, so a device looks the same in the list as on its form.
 *
 * @param explicitKey The device's stored `deviceIconKey`, if any.
 * @param suggestedKey The device's read-time `suggestedDeviceIconKey`, if any.
 */
export function resolveDisplayedDeviceIcon(
  explicitKey: DnsClientDeviceIconEnum | null | undefined,
  suggestedKey: DnsClientDeviceIconEnum | null | undefined,
): DeviceIconOption {
  return (
    findDeviceIcon(explicitKey) ??
    findDeviceIcon(suggestedKey) ??
    // The catalog's first entry is always the generic device.
    DEVICE_ICON_CATALOG[0]
  );
}
