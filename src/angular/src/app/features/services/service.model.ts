import { ManagedServiceCategoryEnum, ManagedServiceSummary } from '../../api-client';

/** The three actions the Services page can run on a service. */
export type ServiceLifecycleAction = 'start' | 'stop' | 'restart';

/** This constant maps each {@link ManagedServiceCategoryEnum} to its section heading. */
const CATEGORY_LABELS: Readonly<Record<ManagedServiceCategoryEnum, string>> = {
  [ManagedServiceCategoryEnum.Transport]: 'Transport services',
  [ManagedServiceCategoryEnum.Background]: 'Background services',
};

/** This function returns the section heading for a service's category. */
export function categoryLabel(category: ManagedServiceCategoryEnum | undefined): string {
  return category ? (CATEGORY_LABELS[category] ?? category) : 'Uncategorized';
}

/** This function returns the badge color for whether a service is running. */
export function statusBadgeColor(isRunning: boolean | undefined): string {
  return isRunning ? 'success' : 'danger';
}

/** This function returns the label of the running-state badge. */
export function statusLabel(isRunning: boolean | undefined): string {
  return isRunning ? 'Running' : 'Stopped';
}

/** This interface describes the title, message and button label of a confirmation dialog. */
export interface ServiceLifecycleConfirmation {
  title: string;
  message: string;
  confirmLabel: string;
}

/**
 * This function builds the confirmation text for an action on a service. It names the risk of
 * the service's category instead of asking "are you sure?": stopping a transport cuts off DNS
 * over it at once, and stopping a background loop can let DNSSEC signatures expire, a secondary
 * zone go stale or a filter list stop refreshing.
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

/** This function returns the capitalized verb used in the dialog title and button. */
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

/** This function builds the message for a transport service. */
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

/** This function builds the message for a background service. */
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
