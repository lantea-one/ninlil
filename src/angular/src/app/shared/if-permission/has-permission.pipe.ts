import { Pipe, PipeTransform, inject } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';

/**
 * This pipe answers whether the signed-in user holds a permission, for a control that should stay
 * visible but disabled without it. The usual case is a status button that also toggles the
 * status, where hiding it (as `IfPermissionDirective` would) would hide the status too:
 * ```html
 * <button cButton [disabled]="!('zones:write' | hasPermission)" (click)="toggleDisabled(zone)">
 * ```
 * Impure so that it re-checks on every change detection: the answer depends on the session's
 * claims, not on the permission string passed in, and the check itself is a short array lookup.
 */
@Pipe({ name: 'hasPermission', pure: false })
export class HasPermissionPipe implements PipeTransform {
  private readonly authService = inject(AuthService);

  transform(permission: string): boolean {
    return this.authService.hasPermission(permission);
  }
}
