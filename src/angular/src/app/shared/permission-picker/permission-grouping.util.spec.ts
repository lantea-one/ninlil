import { groupPermissionsByResource } from './permission-grouping.util';

describe('groupPermissionsByResource', () => {
  it('groups permissions by their resource prefix', () => {
    const groups = groupPermissionsByResource([
      'zones:read',
      'zones:write',
      'roles:read',
      'roles:write',
    ]);

    expect(groups).toEqual([
      { resource: 'roles', permissions: ['roles:read', 'roles:write'] },
      { resource: 'zones', permissions: ['zones:read', 'zones:write'] },
    ]);
  });

  it('sorts groups alphabetically by resource, regardless of input order', () => {
    const groups = groupPermissionsByResource(['webhooks:read', 'clientgroups:read']);

    expect(groups.map((group) => group.resource)).toEqual(['clientgroups', 'webhooks']);
  });

  it('returns an empty array for an empty input', () => {
    expect(groupPermissionsByResource([])).toEqual([]);
  });

  it('groups a permission with no colon under its own full name', () => {
    const groups = groupPermissionsByResource(['legacyPermission']);

    expect(groups).toEqual([{ resource: 'legacyPermission', permissions: ['legacyPermission'] }]);
  });

  it('preserves the original per-group ordering of permissions within a resource', () => {
    const groups = groupPermissionsByResource(['zones:write', 'zones:read']);

    expect(groups[0].permissions).toEqual(['zones:write', 'zones:read']);
  });
});
