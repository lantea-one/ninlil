import { DnsClientSortOrderEnum } from '../../../api-client';

/**
 * The quick filters the client list offers, one per summary card: every device, online now,
 * offline, registered since local midnight, or favorites. `ClientListComponent` maps each to the
 * `GET /api/clients` parameters matching what `GET /api/clients/summary` counted, so a card's
 * count and the list it opens describe the same devices.
 */
export type ClientListFilter = 'all' | 'online' | 'offline' | 'new' | 'favorites';

/** Every {@link ClientListFilter}, for validating a value read back from the URL. */
export const CLIENT_LIST_FILTERS: readonly ClientListFilter[] = [
  'all',
  'online',
  'offline',
  'new',
  'favorites',
];

/** One option of the client list's sort control: the backend ordering plus its label. */
export interface ClientSortOption {
  readonly value: DnsClientSortOrderEnum;
  readonly label: string;
}

/** The orderings the client list offers, most useful first; the first is the default. */
export const CLIENT_SORT_OPTIONS: readonly ClientSortOption[] = [
  { value: DnsClientSortOrderEnum.LastSeen, label: 'Recently seen' },
  { value: DnsClientSortOrderEnum.DisplayName, label: 'Name' },
  { value: DnsClientSortOrderEnum.Created, label: 'Registration order' },
];
