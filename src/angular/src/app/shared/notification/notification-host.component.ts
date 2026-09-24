import { Component, inject } from '@angular/core';
import {
  ToastBodyComponent,
  ToastComponent,
  ToastHeaderComponent,
  ToasterComponent,
} from '@coreui/angular';
import { NotificationService } from './notification.service';

/**
 * This component is the one, app-wide host that actually renders every toast
 * {@link NotificationService} queues -- mounted once, directly in `App`'s own root template
 * alongside `<router-outlet>`, so it sits above every route (including the login page, before
 * `AppShellComponent` mounts) rather than needing to be re-added inside each page. It wraps
 * CoreUI's own `c-toaster`/`c-toast` components declaratively: each queued
 * {@link NotificationService.toasts} entry becomes one `c-toast`, positioned top-end, colored by
 * its own `success`/`danger` outcome, and either auto-hiding after its own delay or staying until
 * the operator dismisses it via the toast's own close button -- {@link NotificationService.error}
 * passes no delay for exactly this reason. Replaces this project's earlier `MatSnackBar`-based
 * mechanism (which queued and displayed one toast at a time); this CoreUI-native toaster instead
 * shows every currently-queued toast stacked at once, matching `c-toaster`'s own real behavior.
 * Each toast's own `role` is `"alert"` (assertive, interrupts a screen reader immediately) for an
 * error and `"status"` (polite, announced once the reader is idle) for a success confirmation --
 * `c-toast`'s own `[attr.role]` host binding turns that into the real `aria-live` region behavior
 * a toast that can appear without any corresponding user action needs to be announced at all.
 */
@Component({
  selector: 'app-notification-host',
  imports: [ToastBodyComponent, ToastComponent, ToastHeaderComponent, ToasterComponent],
  template: `
    <c-toaster placement="top-end">
      @for (toast of notification.toasts(); track toast.id) {
        <c-toast
          [visible]="true"
          [color]="toast.color"
          [autohide]="toast.autoHideDelayMs !== null"
          [delay]="toast.autoHideDelayMs ?? 0"
          [fade]="true"
          [role]="toast.color === 'danger' ? 'alert' : 'status'"
          (visibleChange)="onToastVisibleChange($event, toast.id)"
        >
          <c-toast-header [closeButton]="true">
            <strong class="me-auto">{{ toast.color === 'success' ? 'Success' : 'Error' }}</strong>
          </c-toast-header>
          <c-toast-body>{{ toast.message }}</c-toast-body>
        </c-toast>
      }
    </c-toaster>
  `,
})
export class NotificationHostComponent {
  /** Exposed so the template can read {@link NotificationService.toasts} directly. */
  readonly notification = inject(NotificationService);

  /** This method dismisses a toast once CoreUI reports it no longer visible -- either its own auto-hide timer elapsed, or the operator clicked its close button. */
  onToastVisibleChange(visible: boolean, id: number): void {
    if (!visible) this.notification.dismiss(id);
  }
}
