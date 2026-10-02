import { Injectable, signal } from '@angular/core';

let nextToastId = 0;

/** One toast {@link NotificationService} is currently showing, rendered by `NotificationHostComponent`. */
export interface NotificationToast {
  /** An increasing id, unique within the page load, used to track and dismiss one toast. */
  readonly id: number;

  /** The message text. */
  readonly message: string;

  /** The CoreUI color: `'success'` from {@link NotificationService.success}, `'danger'` from {@link NotificationService.error}. */
  readonly color: 'success' | 'danger';

  /** The auto-hide delay in milliseconds, or `null` for a toast the user must close (errors). */
  readonly autoHideDelayMs: number | null;
}

/**
 * This service is the app-wide queue of toast notifications, held in a signal. Pages call
 * {@link success} and {@link error} for short "it worked" or "it failed" messages, and
 * `NotificationHostComponent` renders {@link toasts}.
 *
 * <p>Forms show their validation and submit errors in a local `errorMessage` alert instead, so the
 * message stays beside the form until the user acts on it.
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly _toasts = signal<NotificationToast[]>([]);

  /** Every toast currently shown, oldest first. */
  readonly toasts = this._toasts.asReadonly();

  /** This method shows a success toast that hides itself after 4 seconds. */
  success(message: string): void {
    this.push(message, 'success', 4000);
  }

  /** This method shows an error toast that stays until the user closes it. */
  error(message: string): void {
    this.push(message, 'danger', null);
  }

  /** This method removes a toast by id; `NotificationHostComponent` calls it when a toast hides. */
  dismiss(id: number): void {
    this._toasts.update((toasts) => toasts.filter((toast) => toast.id !== id));
  }

  private push(
    message: string,
    color: NotificationToast['color'],
    autoHideDelayMs: number | null,
  ): void {
    const toast: NotificationToast = { id: nextToastId++, message, color, autoHideDelayMs };
    this._toasts.update((toasts) => [...toasts, toast]);
  }
}
