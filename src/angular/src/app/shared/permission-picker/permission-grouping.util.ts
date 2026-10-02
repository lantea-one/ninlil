/** This interface describes the permissions of one resource. */
export interface PermissionGroup {
  /** The resource prefix, e.g. `"zones"` for `"zones:read"`/`"zones:write"`. */
  resource: string;

  /** Every permission string under this resource, in the order the backend returned them. */
  permissions: string[];
}

/**
 * This function groups `resource:action` permission strings, as `GET /api/permissions` returns
 * them, by resource, so {@link PermissionPickerComponent} can list `zones:read` and `zones:write`
 * under one `"zones"` heading. Groups are sorted by resource; within a group the input order is
 * kept. A string without a `:` forms a one-item group.
 */
export function groupPermissionsByResource(permissions: readonly string[]): PermissionGroup[] {
  const groups = new Map<string, string[]>();

  for (const permission of permissions) {
    const separatorIndex = permission.indexOf(':');
    const resource = separatorIndex === -1 ? permission : permission.slice(0, separatorIndex);

    const existing = groups.get(resource);
    if (existing) {
      existing.push(permission);
    } else {
      groups.set(resource, [permission]);
    }
  }

  return [...groups.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([resource, groupPermissions]) => ({ resource, permissions: groupPermissions }));
}
