import { DnsClientDeviceIconEnum } from '../../../api-client';

/**
 * One entry of the device icon catalog: the backend's `DnsClientDeviceIconEnum` key, its label,
 * and the `@coreui/icons` icon it is drawn with (which must be registered in `app.config.ts`).
 */
export interface DeviceIconOption {
  readonly key: DnsClientDeviceIconEnum;
  readonly label: string;
  readonly icon: string;
}

/**
 * The device icon catalog, in the order the picker offers it. The backend stores only the `key`,
 * so the icon for each kind of device can change here without touching stored data. Every
 * `DnsClientDeviceIconEnum` member must appear once; the picker's spec checks this. The first
 * entry must stay the generic device, the fallback in `resolveDisplayedDeviceIcon`.
 */
export const DEVICE_ICON_CATALOG: readonly DeviceIconOption[] = [
  { key: DnsClientDeviceIconEnum.Generic, label: 'Generic device', icon: 'cilDevices' },
  { key: DnsClientDeviceIconEnum.Laptop, label: 'Laptop', icon: 'cilLaptop' },
  { key: DnsClientDeviceIconEnum.Desktop, label: 'Desktop', icon: 'cilScreenDesktop' },
  { key: DnsClientDeviceIconEnum.Phone, label: 'Phone', icon: 'cilMobile' },
  { key: DnsClientDeviceIconEnum.Tablet, label: 'Tablet', icon: 'cilTablet' },
  { key: DnsClientDeviceIconEnum.Television, label: 'TV / media player', icon: 'cilTv' },
  { key: DnsClientDeviceIconEnum.Router, label: 'Router', icon: 'cilRouter' },
  { key: DnsClientDeviceIconEnum.AccessPoint, label: 'Access point', icon: 'cilWifiSignal4' },
  { key: DnsClientDeviceIconEnum.NetworkSwitch, label: 'Network switch', icon: 'cilEthernet' },
  { key: DnsClientDeviceIconEnum.Printer, label: 'Printer', icon: 'cilPrint' },
  { key: DnsClientDeviceIconEnum.GameConsole, label: 'Game console', icon: 'cilGamepad' },
  { key: DnsClientDeviceIconEnum.Speaker, label: 'Speaker', icon: 'cilSpeaker' },
  { key: DnsClientDeviceIconEnum.Camera, label: 'Camera', icon: 'cilCamera' },
  { key: DnsClientDeviceIconEnum.Watch, label: 'Watch / wearable', icon: 'cilWatch' },
  { key: DnsClientDeviceIconEnum.SmartHome, label: 'Smart home', icon: 'cilLightbulb' },
  { key: DnsClientDeviceIconEnum.Server, label: 'Server / NAS', icon: 'cilStorage' },
  {
    key: DnsClientDeviceIconEnum.SingleBoardComputer,
    label: 'Single-board computer',
    icon: 'cilMemory',
  },
  { key: DnsClientDeviceIconEnum.Headphones, label: 'Headphones', icon: 'cilHeadphones' },
  { key: DnsClientDeviceIconEnum.Appliance, label: 'Appliance', icon: 'cilHome' },
];
