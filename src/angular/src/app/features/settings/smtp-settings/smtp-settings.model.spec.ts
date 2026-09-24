import { buildSmtpSettingsPatchBody } from './smtp-settings.model';

describe('smtp-settings.model', () => {
  it('omits the password key entirely when the field is blank', () => {
    const result = buildSmtpSettingsPatchBody({ smtpHost: 'smtp.example.com', password: '' });

    expect(result).toEqual({ smtpHost: 'smtp.example.com' });
    expect('password' in result).toBeFalse();
  });

  it('omits the password key entirely when the field is only whitespace', () => {
    const result = buildSmtpSettingsPatchBody({ smtpHost: 'smtp.example.com', password: '   ' });

    expect('password' in result).toBeFalse();
  });

  it('includes the password key when the caller actually typed a new value', () => {
    const result = buildSmtpSettingsPatchBody({
      smtpHost: 'smtp.example.com',
      password: 'hunter2',
    });

    expect(result).toEqual({ smtpHost: 'smtp.example.com', password: 'hunter2' });
  });
});
