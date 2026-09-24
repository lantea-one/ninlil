import { Directive, TemplateRef, ViewContainerRef, effect, inject, input } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';

/**
 * This directive renders its element only when the signed-in user holds a permission, and
 * otherwise renders the optional `else` template (or nothing):
 * ```html
 * <button cButton (click)="delete(zone)" *appIfPermission="'zones:write'">Delete</button>
 * <c-card-footer *appIfPermission="'smtpsettings:write'; else readOnly">...</c-card-footer>
 * ```
 * Pages use it to hide create, edit, delete, toggle and save controls from a user who may read a
 * page but not change what it shows -- the API would refuse the change with a 403 anyway, so
 * offering the control only leads to an error. It re-evaluates whenever the session's claims
 * change (a token refresh, signing out), because {@link AuthService.hasPermission} reads a signal.
 * Code that needs the same answer outside a template calls {@link AuthService.hasPermission}.
 */
@Directive({ selector: '[appIfPermission]' })
export class IfPermissionDirective {
  private readonly authService = inject(AuthService);
  private readonly templateRef = inject(TemplateRef<unknown>);
  private readonly viewContainer = inject(ViewContainerRef);

  /** The permission required to render the element, e.g. `'zones:write'`. */
  readonly appIfPermission = input.required<string>();

  /** The template rendered instead when the permission is missing. */
  readonly appIfPermissionElse = input<TemplateRef<unknown> | null>(null);

  /**
   * The template currently rendered (`null` for nothing, `undefined` before the first render), so
   * an unchanged answer doesn't rebuild the view.
   */
  private renderedTemplate: TemplateRef<unknown> | null | undefined = undefined;

  constructor() {
    effect(() => {
      const template = this.authService.hasPermission(this.appIfPermission())
        ? this.templateRef
        : this.appIfPermissionElse();

      if (template === this.renderedTemplate) return;

      this.viewContainer.clear();
      this.renderedTemplate = template;
      if (template) this.viewContainer.createEmbeddedView(template);
    });
  }
}
