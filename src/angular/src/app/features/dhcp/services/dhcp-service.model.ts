import { DhcpServiceKindEnum } from '../../../api-client';

/** The two actions the DHCP Services page can run on a service. */
export type DhcpServiceLifecycleAction = 'start' | 'stop';

/** This constant maps each {@link DhcpServiceKindEnum} to its display name. */
const KIND_LABELS: Readonly<Record<DhcpServiceKindEnum, string>> = {
  [DhcpServiceKindEnum.Dhcpv4]: 'DHCPv4',
  [DhcpServiceKindEnum.Dhcpv6]: 'DHCPv6',
  [DhcpServiceKindEnum.RouterAdvertisement]: 'Router Advertisement',
  [DhcpServiceKindEnum.Tftp]: 'TFTP',
};

/** This function returns the human-readable name shown for one {@link DhcpServiceKindEnum}. */
export function kindLabel(kind: DhcpServiceKindEnum | undefined): string {
  return kind ? (KIND_LABELS[kind] ?? kind) : 'Unknown service';
}

/** This function returns the badge color for whether a service is running. */
export function runningBadgeColor(isRunning: boolean | undefined): string {
  return isRunning ? 'success' : 'danger';
}

/** This function returns the label of the running-state badge. */
export function runningLabel(isRunning: boolean | undefined): string {
  return isRunning ? 'Running' : 'Stopped';
}

/**
 * This function returns the badge color for the configured `Enable` flag. It uses muted colors
 * (`info`/`secondary`), unlike {@link runningBadgeColor}'s `success`/`danger`, so "starts at boot"
 * and "running now" never look like the same fact.
 */
export function enabledBadgeColor(enabled: boolean | undefined): string {
  return enabled ? 'info' : 'secondary';
}

/** This function returns the label of the configured-`Enable` badge. */
export function enabledLabel(enabled: boolean | undefined): string {
  return enabled ? 'Enabled at boot' : 'Disabled at boot';
}

/** This interface describes the title, message and button label of a confirmation dialog. */
export interface DhcpServiceLifecycleConfirmation {
  title: string;
  message: string;
  confirmLabel: string;
}

/**
 * This function builds the confirmation text for starting or stopping a service. As on the
 * Services page, it names the consequence instead of asking "are you sure?": DHCPv4 and DHCPv6
 * stop issuing and renewing leases, Router Advertisement stops advertising this server as a
 * default router, and stopping TFTP aborts transfers in progress, such as a PXE boot download.
 */
export function dhcpLifecycleConfirmation(
  action: DhcpServiceLifecycleAction,
  kind: DhcpServiceKindEnum,
): DhcpServiceLifecycleConfirmation {
  const name = kindLabel(kind);
  const title = `${actionVerb(action)} ${name}?`;
  const confirmLabel = actionVerb(action);
  const message = action === 'start' ? startMessage(kind, name) : stopMessage(kind, name);

  return { title, message, confirmLabel };
}

/** This function returns the capitalized verb used in the dialog title and button. */
function actionVerb(action: DhcpServiceLifecycleAction): string {
  return action === 'start' ? 'Start' : 'Stop';
}

/** This function builds the message for starting a service. */
function startMessage(kind: DhcpServiceKindEnum, name: string): string {
  switch (kind) {
    case DhcpServiceKindEnum.Dhcpv4:
    case DhcpServiceKindEnum.Dhcpv6:
      return `This binds ${name}'s own socket right now and immediately begins issuing and renewing real client leases over this protocol, regardless of its configured "enable at boot" setting.`;
    case DhcpServiceKindEnum.RouterAdvertisement:
      return `This resumes sending real Router Advertisements on every usable interface right now, immediately re-advertising this server as a default router (and, if configured, delegated prefixes) to every IPv6 host on the link.`;
    case DhcpServiceKindEnum.Tftp:
      return `This binds the TFTP listener right now and immediately begins answering real read requests -- typically PXE boot-file downloads -- from any client on the network.`;
  }
}

/** This function builds the message for stopping a service. */
function stopMessage(kind: DhcpServiceKindEnum, name: string): string {
  switch (kind) {
    case DhcpServiceKindEnum.Dhcpv4:
    case DhcpServiceKindEnum.Dhcpv6:
      return `This immediately unbinds ${name}'s own socket. Real clients on this protocol will stop being able to obtain or renew a lease from this server the moment this completes, with no other guardrail in front of it, until it is started again.`;
    case DhcpServiceKindEnum.RouterAdvertisement:
      return `This immediately stops sending Router Advertisements on every interface. Any IPv6 host relying on SLAAC will lose this server as its default router once its current router lifetime elapses, with no other guardrail in front of it, until it is started again.`;
    case DhcpServiceKindEnum.Tftp:
      return `This immediately unbinds the TFTP listener and aborts any file transfer already in progress -- a PXE boot download in flight will fail outright rather than finish. No client will be able to start a new transfer until it is started again.`;
  }
}
