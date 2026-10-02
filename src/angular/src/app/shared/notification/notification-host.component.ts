import { Component, inject } from '@angular/core';
import {
  ToastBodyComponent,
  ToastComponent,
  ToastHeaderComponent,
  ToasterComponent,
} from '@coreui/angular';
import { NotificationService } from './notification.service';

/**
 * This component renders the toasts {@link NotificationService} queues. It is mounted once in the
 * root `App` template, beside `<router-outlet>`, so toasts show on every route, including the
 * login page.
 *
 * <p>Each {@link NotificationService.toasts} entry becomes a CoreUI `c-toast` in the top-end
 * corner, all stacked at once. Success toasts hide themselves after a delay; error toasts have no
 * delay and stay until closed. Errors get `role="alert"`, which a screen reader announces at once;
 * successes get `role="status"`, announced when the reader is idle. A toast appears without the
 * user focusing it, so without these roles it would not be announced at all.
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

  /** This method removes a toast once CoreUI hides it, after its timer or a click on its close button. */
  onToastVisibleChange(visible: boolean, id: number): void {
    if (!visible) this.notification.dismiss(id);
  }
}
