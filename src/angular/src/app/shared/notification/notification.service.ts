import { Injectable, signal } from '@angular/core';

let nextToastId = 0;

/** One toast {@link NotificationService} is currently showing, rendered by `NotificationHostComponent`. */
export interface NotificationToast {
  /** A monotonically increasing id, unique for the lifetime of this app instance -- used only to track/dismiss one specific toast among several shown at once. */
  readonly id: number;

  /** The toast's own message text. */
  readonly message: string;

  /** The CoreUI theme color this toast renders in -- `'success'` (green) or `'danger'` (red), matching {@link NotificationService.success}/{@link NotificationService.error}. */
  readonly color: 'success' | 'danger';

  /** Auto-hide delay in milliseconds, or `null` for a toast the operator must dismiss manually (an error, which should stay visible until acted on). */
  readonly autoHideDelayMs: number | null;
}

/**
 * This service is a thin, app-wide, signal-backed queue of toast notifications -- giving every
 * feature one consistent way to show a lightweight, non-blocking "it worked"/"it failed" message
 * without each feature managing its own toast state. `NotificationHostComponent` (mounted once,
 * at the app root, so it renders above every route including the login page) reads {@link toasts}
 * and renders each one as a CoreUI `c-toast`. Kept deliberately tiny -- a form's own inline
 * validation/submission errors still use a local `errorMessage` signal (matching `LoginComponent`'s
 * own precedent), since those need to stay visible until the user acts, not disappear after a few
 * seconds or get lost among other toasts the way this queue's own entries do.
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly _toasts = signal<NotificationToast[]>([]);

  /** Every toast currently shown, oldest first. */
  readonly toasts = this._toasts.asReadonly();

  /** This method shows a brief, auto-dismissing confirmation toast. */
  success(message: string): void {
    this.push(message, 'success', 4000);
  }

  /** This method shows a longer-lived error toast, dismissed only by the user. */
  error(message: string): void {
    this.push(message, 'danger', null);
  }

  /** This method removes one toast by id -- called by `NotificationHostComponent` once a toast auto-hides or the operator dismisses it explicitly. */
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
