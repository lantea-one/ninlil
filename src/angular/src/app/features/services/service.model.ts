import { ManagedServiceCategoryEnum, ManagedServiceSummary } from '../../api-client';

/** The three lifecycle actions {@link ServiceListComponent} can invoke against a service. */
export type ServiceLifecycleAction = 'start' | 'stop' | 'restart';

/** This constant maps each {@link ManagedServiceCategoryEnum} onto its human-readable list heading. */
const CATEGORY_LABELS: Readonly<Record<ManagedServiceCategoryEnum, string>> = {
  [ManagedServiceCategoryEnum.Transport]: 'Transport services',
  [ManagedServiceCategoryEnum.Background]: 'Background services',
};

/** This function returns the section heading a service's own category is grouped under. */
export function categoryLabel(category: ManagedServiceCategoryEnum | undefined): string {
  return category ? (CATEGORY_LABELS[category] ?? category) : 'Uncategorized';
}

/** This function reports which badge color represents a service's own current running state. */
export function statusBadgeColor(isRunning: boolean | undefined): string {
  return isRunning ? 'success' : 'danger';
}

/** This function reports the label a service's own running-state badge shows. */
export function statusLabel(isRunning: boolean | undefined): string {
  return isRunning ? 'Running' : 'Stopped';
}

/** This interface describes the title/message/confirm-label text one confirmation dialog shows. */
export interface ServiceLifecycleConfirmation {
  title: string;
  message: string;
  confirmLabel: string;
}

/**
 * This function builds the risk-aware confirmation text one lifecycle action against one service
 * shows before it fires, per `ServicesController`'s own doc comment describing exactly what each
 * category risks: stopping a {@link ManagedServiceCategoryEnum.Transport} service immediately cuts
 * off real client DNS resolution over that transport, and stopping a
 * {@link ManagedServiceCategoryEnum.Background} service can leave a zone's DNSSEC signatures to
 * silently expire, a secondary zone to drift stale, or a filter list to stop refreshing, with no
 * other guardrail in front of it. A generic "are you sure?" would hide exactly the risk this
 * dedicated `services:manage` permission exists to gate, so every action/category pairing gets its
 * own real, specific wording instead.
 */
export function lifecycleConfirmation(
  action: ServiceLifecycleAction,
  service: ManagedServiceSummary,
): ServiceLifecycleConfirmation {
  const name = service.name ?? '(unknown service)';
  const isTransport = service.category === ManagedServiceCategoryEnum.Transport;

  const title = `${actionVerb(action)} "${name}"?`;
  const confirmLabel = actionVerb(action);
  const message = isTransport ? transportMessage(action, name) : backgroundMessage(action, name);

  return { title, message, confirmLabel };
}

/** This function capitalizes one lifecycle action into the verb its dialog title/button use. */
function actionVerb(action: ServiceLifecycleAction): string {
  switch (action) {
    case 'start':
      return 'Start';
    case 'stop':
      return 'Stop';
    case 'restart':
      return 'Restart';
  }
}

/** This function builds the risk message for a {@link ManagedServiceCategoryEnum.Transport} service. */
function transportMessage(action: ServiceLifecycleAction, name: string): string {
  switch (action) {
    case 'start':
      return `This binds "${name}"'s own socket and immediately begins answering real client DNS queries over this transport.`;
    case 'stop':
      return `This immediately unbinds "${name}"'s own socket. This server will stop answering real client DNS queries over this transport until it is started again -- with no other guardrail in front of it.`;
    case 'restart':
      return `This briefly unbinds, then immediately rebinds, "${name}"'s own socket. Real client DNS queries over this transport will go unanswered for the short gap in between.`;
  }
}

/** This function builds the risk message for a {@link ManagedServiceCategoryEnum.Background} service. */
function backgroundMessage(action: ServiceLifecycleAction, name: string): string {
  switch (action) {
    case 'start':
      return `This resumes "${name}"'s own background maintenance loop immediately.`;
    case 'stop':
      return `This halts "${name}"'s own background maintenance loop. Depending on which loop this is, DNSSEC signatures could silently expire, a secondary zone could drift stale, or a filter list could stop refreshing -- with no other guardrail in front of it -- until it is started again.`;
    case 'restart':
      return `This briefly halts, then immediately resumes, "${name}"'s own background maintenance loop.`;
  }
}
