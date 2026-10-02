import {
  DnsFilterEntryKindEnum,
  DnsFilterListActionEnum,
  DnsFilterListKindEnum,
  DnsFilterListSourceEnum,
} from '../../api-client';

/** This constant lists every {@link DnsFilterListKindEnum} value for the kind select in `FilterListFormComponent`. */
export const FILTER_LIST_KIND_OPTIONS: readonly DnsFilterListKindEnum[] =
  Object.values(DnsFilterListKindEnum);

/** This constant lists every {@link DnsFilterListActionEnum} value for the action select in `FilterListFormComponent`. */
export const FILTER_LIST_ACTION_OPTIONS: readonly DnsFilterListActionEnum[] =
  Object.values(DnsFilterListActionEnum);

/** This constant lists every {@link DnsFilterListSourceEnum} value for the source select in `FilterListFormComponent`. */
export const FILTER_LIST_SOURCE_OPTIONS: readonly DnsFilterListSourceEnum[] =
  Object.values(DnsFilterListSourceEnum);

/** This constant lists every {@link DnsFilterEntryKindEnum} value for the kind select in `FilterListEntryFormComponent`. */
export const FILTER_ENTRY_KIND_OPTIONS: readonly DnsFilterEntryKindEnum[] =
  Object.values(DnsFilterEntryKindEnum);

/**
 * This function reports whether an entry kind is valid only in a
 * {@link DnsFilterListKindEnum.Domain} list. `Wildcard` and `Regex` patterns match names, so the
 * backend refuses them in a `ResponseIp` or `ClientSource` list, whose entries are addresses or
 * networks.
 */
export function isPatternEntryKind(kind: DnsFilterEntryKindEnum | undefined): boolean {
  return kind === DnsFilterEntryKindEnum.Wildcard || kind === DnsFilterEntryKindEnum.Regex;
}

/**
 * This function tries to compile a `Wildcard` or `Regex` pattern in the browser, to reject an
 * obviously malformed one before submitting. The backend still compiles it on save, and may
 * reject a pattern this accepts, since JavaScript and .NET regex grammars differ.
 */
export function isValidEntryPattern(kind: DnsFilterEntryKindEnum, value: string): boolean {
  if (!isPatternEntryKind(kind) || value.trim().length === 0) return true;

  const pattern =
    kind === DnsFilterEntryKindEnum.Wildcard ? wildcardGlobToRegexSource(value) : value;

  try {
    new RegExp(pattern);
    return true;
  } catch {
    return false;
  }
}

/**
 * This function turns a glob (`*` for any run of characters, `?` for one character) into an
 * anchored regex source, like the backend's `DnsFilterEngine.BuildWildcardRegexPattern`. It is
 * used only to check a wildcard entry before submit; matching happens on the server.
 */
function wildcardGlobToRegexSource(glob: string): string {
  const escaped = glob
    .replace(/[.+^${}()|[\]\\]/g, '\\$&')
    .replace(/\*/g, '.*')
    .replace(/\?/g, '.');
  return `^${escaped}$`;
}

/**
 * This function reports whether a list's entries come from its subscription's scheduled refresh
 * (`DnsFilterListExternalRefreshService`). The backend answers 409 to a manual entry change in
 * such a list, so the entry pages disable or replace their write controls instead.
 */
export function isSubscribedList(source: DnsFilterListSourceEnum | undefined): boolean {
  return source === DnsFilterListSourceEnum.Subscribed;
}

/**
 * One public block list offered in the filter list form's dropdown, shown only when creating a
 * {@link DnsFilterListSourceEnum.Subscribed} list. Each {@link value} is a subscription URL the
 * list's project publishes for this use.
 */
export interface KnownBlocklistOption {
  /** The text shown for this option in the dropdown. */
  readonly label: string;
  /** The subscription URL appended to the textarea when this option is picked. */
  readonly value: string;
  /** The filter list name used when a bulk-create line equals this option's {@link value}. */
  readonly name: string;
}

/**
 * This constant lists the block lists the filter list form's dropdown offers: StevenBlack's
 * "Unified hosts" (https://github.com/StevenBlack/hosts) and OISD's domain lists
 * (https://oisd.nl).
 */
export const KNOWN_BLOCKLIST_OPTIONS: readonly KnownBlocklistOption[] = [
  {
    label: 'StevenBlack -- Unified hosts (ads + malware)',
    value: 'https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts',
    name: 'StevenBlack Unified',
  },
  {
    label: 'StevenBlack -- Unified + Fakenews + Gambling',
    value:
      'https://raw.githubusercontent.com/StevenBlack/hosts/master/alternates/fakenews-gambling/hosts',
    name: 'StevenBlack Unified + Fakenews + Gambling',
  },
  {
    label: 'OISD -- Big blocklist',
    value: 'https://big.oisd.nl/',
    name: 'OISD Big',
  },
  {
    label: 'OISD -- Small blocklist',
    value: 'https://small.oisd.nl/',
    name: 'OISD Small',
  },
];

/**
 * This function returns the name for one bulk-subscribe line. A line equal to a
 * {@link KnownBlocklistOption.value} gets that option's name, whether it was picked or typed.
 * Any other line gets the trimmed `namePrefix` (or "Filter list" when blank) when it is the only
 * line, or `"<prefix> <lineNumber>"` when several lines are saved, so they do not clash.
 */
export function deriveBlocklistName(
  line: string,
  lineNumber: number,
  totalLines: number,
  namePrefix: string,
): string {
  const known = KNOWN_BLOCKLIST_OPTIONS.find((option) => option.value === line);
  if (known) return known.name;

  const prefix = namePrefix.trim().length > 0 ? namePrefix.trim() : 'Filter list';
  return totalLines > 1 ? `${prefix} ${lineNumber}` : prefix;
}
