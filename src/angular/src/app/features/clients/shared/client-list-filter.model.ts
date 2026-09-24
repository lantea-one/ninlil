import { DnsClientSortOrderEnum } from '../../../api-client';

/**
 * The quick filters the client list offers, one per summary card: every device, only devices
 * online now, only offline ones, only those registered since local midnight, or only favorites.
 * Each maps onto exactly the query parameters `GET /api/clients/summary` counted it with, so a
 * card's count and the list it opens always describe the same devices.
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

/** The orderings the client list offers, most useful first -- the first is the default. */
export const CLIENT_SORT_OPTIONS: readonly ClientSortOption[] = [
  { value: DnsClientSortOrderEnum.LastSeen, label: 'Recently seen' },
  { value: DnsClientSortOrderEnum.DisplayName, label: 'Name' },
  { value: DnsClientSortOrderEnum.Created, label: 'Registration order' },
];
