/** This interface describes one resource's own group of permission strings. */
export interface PermissionGroup {
  /** The resource prefix, e.g. `"zones"` for `"zones:read"`/`"zones:write"`. */
  resource: string;

  /** Every permission string under this resource, in the order the backend returned them. */
  permissions: string[];
}

/**
 * This function groups a flat, sorted list of `resource:action` permission strings (exactly the
 * shape `GET /api/permissions` returns -- see `Controllers.PermissionsController`) by their own
 * resource prefix, so a checkbox list can render `zones:read`/`zones:write` next to each other
 * under one `"zones"` heading rather than as 26+ ungrouped rows. A permission string with no
 * `:` in it at all (not expected from this backend today, but not assumed impossible either) is
 * grouped under its own full name as a one-item group, rather than dropped or thrown on.
 *
 * Shared by both {@link PermissionPickerComponent}'s own users-management and roles-management
 * consumers -- built once here rather than duplicating the same grouping logic in both features'
 * own model files.
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
