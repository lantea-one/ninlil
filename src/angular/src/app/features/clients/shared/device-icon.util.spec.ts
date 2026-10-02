import { DnsClientDeviceIconEnum } from '../../../api-client';
import { resolveDisplayedDeviceIcon } from './device-icon.util';

/** These tests cover the icon precedence: explicit choice, then suggestion, then generic. */
describe('device-icon.util', () => {
  it('resolveDisplayedDeviceIcon prefers the explicit choice over the suggestion', () => {
    expect(
      resolveDisplayedDeviceIcon(DnsClientDeviceIconEnum.Laptop, DnsClientDeviceIconEnum.Printer)
        .key,
    ).toBe(DnsClientDeviceIconEnum.Laptop);
  });

  it('resolveDisplayedDeviceIcon falls back to the suggestion, then the generic device', () => {
    expect(resolveDisplayedDeviceIcon(null, DnsClientDeviceIconEnum.Printer).key).toBe(
      DnsClientDeviceIconEnum.Printer,
    );
    expect(resolveDisplayedDeviceIcon(null, null).key).toBe(DnsClientDeviceIconEnum.Generic);
  });
});
