import { ApiQueryLogGetRequestParams } from '../../api-client';

/**
 * This interface describes the shape of {@link QueryLogListComponent}'s own filter form value --
 * every field is a plain string (including the two date/time fields, which come straight out of
 * an `<input type="datetime-local">` control) since every one of them is optional and this
 * component never validates a field's own syntax beyond "is it non-empty" -- the real backend
 * search endpoint is the only place any of these values are ever actually interpreted (an
 * unparseable `queriedType`/`responseCode` comes back as a real, surfaced `400`, matching this
 * project's own established "accept a string, let the real backend validate it" convention).
 */
export interface QueryLogFilterFormValue {
  clientAddress: string;
  domain: string;
  queriedType: string;
  responseCode: string;
  fromUtc: string;
  toUtc: string;
}

/**
 * This function converts one page request's own filter values, offset, and page size into the
 * exact request-parameter shape `QueryLogService.apiQueryLogGet` accepts. A blank string field is
 * omitted entirely (rather than sent as `""`), matching `QueryLogController.Search`'s own
 * `string.IsNullOrWhiteSpace(...)  ? null : ...` treatment of an absent filter axis -- sending an
 * empty string instead of omitting the parameter would still behave correctly server-side, but
 * omitting it here keeps the actual outgoing request legible when inspected. A
 * `datetime-local` input's own value carries no UTC offset at all (it is always local wall-clock
 * time); `new Date(value)` interprets that value in the browser's own local time zone, and
 * `.toISOString()` converts it to a real, unambiguous UTC instant the backend's own
 * `DateTimeOffset?` model binder parses correctly.
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

  if (filters.fromUtc.length > 0) params.fromUtc = new Date(filters.fromUtc).toISOString();
  if (filters.toUtc.length > 0) params.toUtc = new Date(filters.toUtc).toISOString();

  return params;
}

/** This constant contains the empty filter form value {@link QueryLogListComponent} starts from and resets to. */
export const EMPTY_QUERY_LOG_FILTERS: QueryLogFilterFormValue = {
  clientAddress: '',
  domain: '',
  queriedType: '',
  responseCode: '',
  fromUtc: '',
  toUtc: '',
};
