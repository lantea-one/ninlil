import { buildBrokerPatchFields } from './event-broker-settings.model';

/**
 * These tests cover buildBrokerPatchFields: a blank or whitespace password is left out, a typed one
 * is kept. Plain unit tests.
 */
describe('event-broker-settings.model', () => {
  it('omits the password key entirely when the field is blank', () => {
    const result = buildBrokerPatchFields({ hostName: 'broker.local', password: '' });

    expect(result).toEqual({ hostName: 'broker.local' });
    expect('password' in result).toBeFalse();
  });

  it('omits the password key entirely when the field is only whitespace', () => {
    const result = buildBrokerPatchFields({ hostName: 'broker.local', password: '   ' });

    expect('password' in result).toBeFalse();
  });

  it('includes the password key when the caller actually typed a new value', () => {
    const result = buildBrokerPatchFields({ hostName: 'broker.local', password: 'hunter2' });

    expect(result).toEqual({ hostName: 'broker.local', password: 'hunter2' });
  });
});
