import { DnsClientDeviceIconEnum } from '../../../api-client';
import { DEVICE_ICON_CATALOG, DeviceIconOption } from './device-icon.model';

/**
 * This function finds a device icon's catalog entry, or `undefined` for an unset (or unknown) key
 * -- shared by the picker and any page that draws a client's icon, so the key-to-glyph mapping
 * lives in exactly one place (`DEVICE_ICON_CATALOG`).
 */
export function findDeviceIcon(
  key: DnsClientDeviceIconEnum | null | undefined,
): DeviceIconOption | undefined {
  return key ? DEVICE_ICON_CATALOG.find((option) => option.key === key) : undefined;
}

/**
 * This function picks the icon a device is drawn with anywhere in a list or summary: the
 * operator's explicit choice when there is one, otherwise the backend's vendor-based suggestion,
 * otherwise the generic device glyph -- the same precedence the icon picker's "Automatic" tile
 * describes, so a device looks the same on the list as it does on its own form.
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
