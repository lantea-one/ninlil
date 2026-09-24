import { DhcpServiceKindEnum } from '../../../api-client';

/** The two lifecycle actions {@link DhcpServiceListComponent} can invoke against a DHCP-family service. */
export type DhcpServiceLifecycleAction = 'start' | 'stop';

/** This constant maps each {@link DhcpServiceKindEnum} onto its human-readable display name. */
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

/** This function reports which badge color represents a service's own live running state. */
export function runningBadgeColor(isRunning: boolean | undefined): string {
  return isRunning ? 'success' : 'danger';
}

/** This function reports the label a service's own live running-state badge shows. */
export function runningLabel(isRunning: boolean | undefined): string {
  return isRunning ? 'Running' : 'Stopped';
}

/**
 * This function reports which badge color represents a service's own configured `Enable` flag --
 * deliberately a distinct, muted pairing (`info`/`secondary`) from {@link runningBadgeColor}'s own
 * `success`/`danger` pairing, so the two signals this page's entire premise rests on -- "will this
 * start automatically at the next boot" versus "is this bound and serving traffic right now" --
 * never look like restatements of the same fact at a glance, even when they happen to agree.
 */
export function enabledBadgeColor(enabled: boolean | undefined): string {
  return enabled ? 'info' : 'secondary';
}

/** This function reports the label a service's own configured-`Enable` badge shows. */
export function enabledLabel(enabled: boolean | undefined): string {
  return enabled ? 'Enabled at boot' : 'Disabled at boot';
}

/** This interface describes the title/message/confirm-label text one confirmation dialog shows. */
export interface DhcpServiceLifecycleConfirmation {
  title: string;
  message: string;
  confirmLabel: string;
}

/**
 * This function builds the risk-aware confirmation text one lifecycle action against one DHCP-family
 * service shows before it fires -- the same "name the real, specific consequence, never a generic
 * 'are you sure?'" precedent {@link lifecycleConfirmation} (this app's general Services page)
 * establishes, reworded for what each of these four services specifically does: DHCPv4/DHCPv6
 * genuinely stop issuing/renewing real client leases, Router Advertisement stops SLAAC-configuring
 * IPv6 hosts and withdrawing itself as a default router, and TFTP stops answering PXE boot-file
 * requests (aborting any transfer already in progress, per `TftpServer`'s own documented live-stop
 * contract).
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

/** This function capitalizes one lifecycle action into the verb its dialog title/button use. */
function actionVerb(action: DhcpServiceLifecycleAction): string {
  return action === 'start' ? 'Start' : 'Stop';
}

/** This function builds the risk message for starting one DHCP-family service live. */
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

/** This function builds the risk message for stopping one DHCP-family service live. */
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
