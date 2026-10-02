import {
  ApiQueryLogGetRequestParams,
  DnsQueryLogEntryDetail,
  InternetProtocolVersionEnum,
} from '../../api-client';

/**
 * This interface is the value of the Query Log page's filter form. Every field is an optional
 * string, the dates as `datetime-local` gives them. The page does not validate them; the backend
 * interprets them and answers 400 for, say, an unknown `queriedType`, which the page shows.
 */
export interface QueryLogFilterFormValue {
  clientAddress: string;
  domain: string;
  queriedType: string;
  responseCode: string;
  fromUtc: string;
  toUtc: string;
  /** A MAC address or DUID, matched exactly against the client identity the entry was resolved to. */
  clientIdentifier: string;
  /** `''` for either kind, or `IPv4`/`IPv6` to match only MAC-address or only DUID identities. */
  clientProtocolVersion: '' | InternetProtocolVersionEnum;
}

/**
 * This function builds the `QueryLogService.apiQueryLogGet` parameters from the filters, offset
 * and page size. Blank fields are left out rather than sent empty, which keeps the request
 * readable (the backend treats both the same). A `datetime-local` value has no offset, so it is
 * read as browser local time and sent as a UTC ISO string.
 */
export function buildQueryLogSearchParams(
  filters: QueryLogFilterFormValue,
  skip: number,
  take: number,
): ApiQueryLogGetRequestParams {
  const params: ApiQueryLogGetRequestParams = { skip, take };

  const clientAddress = filters.clientAddress.trim();
  if (clientAddress.length > 0) params.clientAddress = clientAddress;

  const domain = filters.domain.trim();
  if (domain.length > 0) params.domain = domain;

  const queriedType = filters.queriedType.trim();
  if (queriedType.length > 0) params.queriedType = queriedType;

  const responseCode = filters.responseCode.trim();
  if (responseCode.length > 0) params.responseCode = responseCode;

  const clientIdentifier = filters.clientIdentifier.trim();
  if (clientIdentifier.length > 0) params.clientIdentifier = clientIdentifier;

  if (filters.clientProtocolVersion) params.clientProtocolVersion = filters.clientProtocolVersion;

  if (filters.fromUtc.length > 0) params.fromUtc = new Date(filters.fromUtc).toISOString();
  if (filters.toUtc.length > 0) params.toUtc = new Date(filters.toUtc).toISOString();

  return params;
}

/** The empty filter value the Query Log page starts from and resets to. */
export const EMPTY_QUERY_LOG_FILTERS: QueryLogFilterFormValue = {
  clientAddress: '',
  domain: '',
  queriedType: '',
  responseCode: '',
  fromUtc: '',
  toUtc: '',
  clientIdentifier: '',
  clientProtocolVersion: '',
};

/**
 * This function names the kind of client identity an entry was resolved to: DHCPv4 identifies a
 * client by its MAC address, DHCPv6 by its DUID.
 */
export function clientIdentifierKind(entry: DnsQueryLogEntryDetail): 'MAC' | 'DUID' {
  return entry.clientProtocolVersion === InternetProtocolVersionEnum.Ipv6 ? 'DUID' : 'MAC';
}
