import { Component, computed, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardHeaderComponent,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { GettingStartedItem } from '../dashboard.model';

/**
 * The `localStorage` key that remembers a dismissal across reloads. It is per browser, not per
 * account, since it is only a display preference. (Keeping session tokens out of storage, as
 * `AuthService` does, is a separate security rule that does not apply here.)
 */
const DISMISSED_STORAGE_KEY = 'ninlil:dashboard:getting-started-dismissed';

/** This function reads the dismissal flag, or `false` when it is unset or storage is unavailable (e.g. blocked by the browser). */
function readDismissedFlag(): boolean {
  try {
    return localStorage.getItem(DISMISSED_STORAGE_KEY) === 'true';
  } catch {
    return false;
  }
}

/**
 * This component renders the Dashboard's "Getting Started" card: up to four onboarding steps, each
 * linking to the page that completes it. {@link DashboardComponent} fetches the rows through
 * {@link GettingStartedChecklistService}; this component only displays them and keeps the
 * dismissal, which is stored in `localStorage` for this browser only.
 *
 * <p>The card is hidden ({@link visible}) when there are no rows, when every row is done, or when
 * the user dismissed it. A dismissal is permanent for the browser, even if some steps are still
 * open.
 */
@Component({
  selector: 'app-getting-started-checklist',
  imports: [
    RouterLink,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardHeaderComponent,
    IconDirective,
  ],
  templateUrl: './getting-started-checklist.component.html',
  styleUrl: './getting-started-checklist.component.scss',
})
export class GettingStartedChecklistComponent {
  /** The rows to show, already filtered by permission in {@link GettingStartedChecklistService}. */
  readonly items = input<GettingStartedItem[]>([]);

  /** Whether this browser dismissed the card, read from `localStorage` when the component is created. */
  private readonly dismissedByUser = signal(readDismissedFlag());

  /** Whether every row is done; `false` when there are no rows. */
  readonly allComplete = computed(() => {
    const rows = this.items();
    return rows.length > 0 && rows.every((row) => row.complete);
  });

  /**
   * Whether the card renders: there is at least one row, not all are done, and this browser has
   * not dismissed it.
   */
  readonly visible = computed(
    () => this.items().length > 0 && !this.allComplete() && !this.dismissedByUser(),
  );

  /** This method hides the card and remembers that in `localStorage`, so it stays hidden after a reload. */
  dismiss(): void {
    this.dismissedByUser.set(true);
    try {
      localStorage.setItem(DISMISSED_STORAGE_KEY, 'true');
    } catch {
      // Storage is unavailable: the card stays hidden until the page is reloaded.
    }
  }
}
