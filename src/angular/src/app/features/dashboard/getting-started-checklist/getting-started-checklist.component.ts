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
 * The `localStorage` key this component reads/writes to remember a dismissal across a page
 * reload. Deliberately per-browser, not per-account or server-persisted -- a per-viewer UI
 * convenience, matching the same scope every other browser-local preference in a web app of this
 * kind carries, and the first such convenience in this codebase (this app's session tokens are
 * the one thing deliberately kept in-memory-only instead, per `AuthService`'s own doc comment --
 * an unrelated security decision about a different kind of state, not a precedent against using
 * `localStorage` here).
 */
const DISMISSED_STORAGE_KEY = 'ninlil:dashboard:getting-started-dismissed';

/** This function reads the dismissal flag, defaulting to `false` if storage is unavailable (private browsing, a disabled-storage browser setting) or simply empty. */
function readDismissedFlag(): boolean {
  try {
    return localStorage.getItem(DISMISSED_STORAGE_KEY) === 'true';
  } catch {
    return false;
  }
}

/**
 * This component renders the Dashboard's own "Getting Started" checklist card: a short, fixed
 * 4-row list ({@link GettingStartedChecklistService} resolves each row's real `complete` state
 * against a live backend endpoint), each row a real `routerLink` straight to the page that
 * satisfies it. Purely presentational -- {@link DashboardComponent} owns the actual fetch, the
 * same split this app's other dashboard-area components ({@link QuickStatsComponent},
 * {@link HealthTileGridComponent}) already follow -- except for the one piece of state that
 * belongs here and nowhere else: dismissal, a `localStorage`-persisted, per-browser UI
 * convenience with no server-side representation at all.
 * <p>
 * The card hides itself for two entirely different reasons, both handled by {@link visible}:
 * the caller dismissed it, or every visible row is now genuinely complete. The second case wins
 * even over a *stale* dismissal -- a caller who dismissed the card while 3 of 4 rows were done,
 * then later finishes the 4th without ever re-opening it, still sees it gone for good rather than
 * silently lingering as available-but-hidden forever.
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
  /** The checklist rows to render, already permission-filtered by {@link GettingStartedChecklistService}. */
  readonly items = input<GettingStartedItem[]>([]);

  /** Whether this browser has dismissed the card, seeded once from `localStorage` at construction. */
  private readonly dismissedByUser = signal(readDismissedFlag());

  /** Whether every row in {@link items} is complete -- vacuously `false` when {@link items} is empty, since there is then nothing to show either way. */
  readonly allComplete = computed(() => {
    const rows = this.items();
    return rows.length > 0 && rows.every((row) => row.complete);
  });

  /**
   * Whether the card should render at all: at least one permission-visible row exists, not every
   * one of them is already complete, and this browser hasn't dismissed it.
   */
  readonly visible = computed(
    () => this.items().length > 0 && !this.allComplete() && !this.dismissedByUser(),
  );

  /** This method dismisses the card for this browser, persisting the choice so it stays gone across a reload. */
  dismiss(): void {
    this.dismissedByUser.set(true);
    try {
      localStorage.setItem(DISMISSED_STORAGE_KEY, 'true');
    } catch {
      // Private browsing / disabled storage -- the in-memory signal above still hides the card
      // for the rest of this page visit, the only guarantee this method can make in that case.
    }
  }
}
