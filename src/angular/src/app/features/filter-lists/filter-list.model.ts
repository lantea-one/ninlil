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

/**
 * This constant lists every {@link DnsFilterEntryKindEnum} value for the kind select in
 * `FilterListEntryFormComponent`, matching or exceeding Pi-hole (regex lists) and AdGuard Home
 * (extended/wildcard syntax) in expressiveness.
 */
export const FILTER_ENTRY_KIND_OPTIONS: readonly DnsFilterEntryKindEnum[] =
  Object.values(DnsFilterEntryKindEnum);

/**
 * This function reports whether an entry kind is only ever meaningful in a
 * {@link DnsFilterListKindEnum.Domain} list -- the UI-side mirror of
 * `FilterListEntriesController`'s own rejection of a `Wildcard`/`Regex` entry kind against a
 * `ResponseIp`/`ClientSource` list, whose own entries are always IP addresses/CIDR networks that
 * neither syntax applies to.
 */
export function isPatternEntryKind(kind: DnsFilterEntryKindEnum | undefined): boolean {
  return kind === DnsFilterEntryKindEnum.Wildcard || kind === DnsFilterEntryKindEnum.Regex;
}

/**
 * This function attempts to compile a `Wildcard`/`Regex` entry's own pattern client-side,
 * mirroring (not replacing) `FilterListEntriesController`'s own real compile-before-persist
 * validation -- a cheap, immediate rejection of an obviously malformed pattern before ever
 * submitting it. The backend remains the actual source of truth: a
 * pattern this function accepts can still be rejected server-side (a JavaScript `RegExp` and a
 * .NET `Regex` do not accept byte-for-byte the same grammar), and this function's own result is
 * never trusted to skip that real validation.
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
 * This function translates a shell-style glob (`*` matches any run of characters, `?` matches
 * exactly one character) into an anchored regex source string -- the client-side analogue of
 * `DnsFilterEngine.BuildWildcardRegexPattern`, used here only to validate a wildcard entry's own
 * pattern before submit, never to actually perform any query-time matching (which only ever
 * happens server-side).
 */
function wildcardGlobToRegexSource(glob: string): string {
  const escaped = glob
    .replace(/[.+^${}()|[\]\\]/g, '\\$&')
    .replace(/\*/g, '.*')
    .replace(/\?/g, '.');
  return `^${escaped}$`;
}

/**
 * This function reports whether a filter list's own entries are managed automatically by its
 * subscription's own scheduled refresh (`DnsFilterListExternalRefreshService`), rather than by
 * hand -- the UI-side mirror of `FilterListEntriesController`'s own `409 Conflict` rejection of a
 * manual entry write against such a list. `FilterListEntryListComponent` uses this to disable its
 * own "add entry"/edit/delete controls outright, with an explanatory message, rather than letting
 * an operator attempt a write only to be rejected by the backend after the fact.
 */
export function isSubscribedList(source: DnsFilterListSourceEnum | undefined): boolean {
  return source === DnsFilterListSourceEnum.Subscribed;
}

/**
 * One well-known public block-list subscription URL offered by `FilterListFormComponent`'s own
 * "known block lists" dropdown, shown only for a new {@link DnsFilterListSourceEnum.Subscribed}
 * list.
 *
 * Every {@link value} here is a publicly documented subscription URL a real, independently-
 * operated third-party project itself publishes for exactly this purpose, sourced from this
 * project's own general knowledge of these well-known lists -- never read, copied, or derived
 * from Technitium's or dnsmasq's own source or bundled configuration, per the clean-room
 * rule in `NOTICE.md`.
 */
export interface KnownBlocklistOption {
  /** The text shown for this option in the dropdown. */
  readonly label: string;
  /** The literal subscription URL appended to the textarea when this option is picked. */
  readonly value: string;
  /** The filter list name auto-derived when a bulk-create line's content exactly matches this option's {@link value}. */
  readonly name: string;
}

/**
 * This constant lists every known-public-block-list option `FilterListFormComponent`'s dropdown
 * offers -- StevenBlack's community-maintained "Unified hosts" project
 * (https://github.com/StevenBlack/hosts) and OISD's own curated domain lists
 * (https://oisd.nl), both long-established, independently-operated public block-list projects
 * unrelated to either Technitium or dnsmasq.
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
 * This function derives the `name` a bulk-create line gets when `FilterListFormComponent.save()`
 * turns one non-blank textarea line into its own `POST /api/filter-lists` call, for a new
 * {@link DnsFilterListSourceEnum.Subscribed} list. A line whose trimmed content exactly matches a
 * {@link KnownBlocklistOption.value} always takes that option's own descriptive
 * {@link KnownBlocklistOption.name}, regardless of whether the operator reached it by picking the
 * dropdown or by typing the identical URL by hand. An unrecognized (manually typed) line falls
 * back to `namePrefix` verbatim when it is the only line being created (preserving this form's
 * original single-create behavior exactly), or `"<namePrefix> <lineNumber>"` when multiple lines
 * are being created in the same bulk save, so no two unrecognized lines in the same save collide
 * on name.
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
