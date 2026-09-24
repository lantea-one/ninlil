import { splitListenAddresses } from './transport-settings.model';

describe('transport-settings.model', () => {
  it('splitListenAddresses accepts new lines, commas, semicolons and spaces between addresses', () => {
    expect(splitListenAddresses(' 127.0.0.1,::1\n192.168.1.1; 10.0.0.1  fe80::1 ')).toEqual([
      '127.0.0.1',
      '::1',
      '192.168.1.1',
      '10.0.0.1',
      'fe80::1',
    ]);
  });

  it('splitListenAddresses turns an empty or blank field into an empty list', () => {
    expect(splitListenAddresses('')).toEqual([]);
    expect(splitListenAddresses(' \n ')).toEqual([]);
  });
});
