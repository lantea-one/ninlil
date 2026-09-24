import { EMPTY_QUERY_LOG_FILTERS, buildQueryLogSearchParams } from './query-log.model';

describe('buildQueryLogSearchParams', () => {
  it('omits every blank filter field, keeping only skip and take', () => {
    const params = buildQueryLogSearchParams(EMPTY_QUERY_LOG_FILTERS, 0, 25);

    expect(params).toEqual({ skip: 0, take: 25 });
  });

  it('trims and includes every non-blank text filter field', () => {
    const params = buildQueryLogSearchParams(
      {
        ...EMPTY_QUERY_LOG_FILTERS,
        clientAddress: '  192.0.2.10  ',
        domain: ' example.com ',
        queriedType: ' A ',
        responseCode: ' NXDOMAIN ',
      },
      50,
      10,
    );

    expect(params).toEqual({
      skip: 50,
      take: 10,
      clientAddress: '192.0.2.10',
      domain: 'example.com',
      queriedType: 'A',
      responseCode: 'NXDOMAIN',
    });
  });

  it('converts a datetime-local field to a real UTC ISO instant', () => {
    const params = buildQueryLogSearchParams(
      { ...EMPTY_QUERY_LOG_FILTERS, fromUtc: '2026-01-01T00:00', toUtc: '2026-01-02T00:00' },
      0,
      25,
    );

    expect(params.fromUtc).toBe(new Date('2026-01-01T00:00').toISOString());
    expect(params.toUtc).toBe(new Date('2026-01-02T00:00').toISOString());
  });

  it('leaves a field consisting only of whitespace out of the request', () => {
    const params = buildQueryLogSearchParams(
      { ...EMPTY_QUERY_LOG_FILTERS, clientAddress: '   ' },
      0,
      25,
    );

    expect(params.clientAddress).toBeUndefined();
  });
});
