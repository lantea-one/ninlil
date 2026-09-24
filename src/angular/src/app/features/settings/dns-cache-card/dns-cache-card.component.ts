import { Component, OnInit, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  AlertComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardFooterComponent,
  CardHeaderComponent,
  FormControlDirective,
  FormLabelDirective,
  FormTextDirective,
  SpinnerComponent,
  TableDirective,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { firstValueFrom } from 'rxjs';
import { DnsCacheDetail, DnsCacheService } from '../../../api-client';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../../shared/confirm-dialog/confirm-dialog.component';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';
import { NotificationService } from '../../../shared/notification/notification.service';

/** The label each cache is shown under; an unknown future cache falls back to its API name. */
const CACHE_LABELS: Record<string, string> = {
  forwarded: 'Forwarded answers',
  recursive: 'Recursive resolver',
};

/**
 * This component shows this node's DNS answer caches -- the forwarded-response cache and the
 * recursive resolver's cache -- with their size and hit rate, and lets an operator flush one or
 * all of them, or forget a single name, via `GET`/`DELETE /api/dns-cache` and
 * `DELETE /api/dns-cache/entries/{name}`. It sits on the Recursive Resolver settings page, next to
 * the cache size and serve-stale settings, and uses the same permissions: `recursivesettings:read`
 * to see it and `recursivesettings:write` for every action that removes anything.
 *
 * <p>Caches are per node, so on a cluster this shows (and flushes) only the node the console is
 * talking to.
 */
@Component({
  selector: 'app-dns-cache-card',
  imports: [
    ConfirmDialogComponent,
    IfPermissionDirective,
    ReactiveFormsModule,
    AlertComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardFooterComponent,
    CardHeaderComponent,
    FormControlDirective,
    FormLabelDirective,
    FormTextDirective,
    IconDirective,
    SpinnerComponent,
    TableDirective,
  ],
  templateUrl: './dns-cache-card.component.html',
  styleUrl: './dns-cache-card.component.scss',
})
export class DnsCacheCardComponent implements OnInit {
  private readonly cacheApi = inject(DnsCacheService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly caches = signal<DnsCacheDetail[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly removeForm = this.formBuilder.group({
    name: ['', [Validators.required, Validators.pattern(/\S/)]],
  });

  /** Whether {@link ConfirmDialogComponent} is currently shown for a pending flush. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  ngOnInit(): void {
    void this.load();
  }

  /** This method returns the label a cache is shown under. */
  label(cache: DnsCacheDetail): string {
    return CACHE_LABELS[cache.name ?? ''] ?? cache.name ?? '';
  }

  /** This method returns a cache's hit rate as a whole percentage, or an em dash before any lookup. */
  hitRate(cache: DnsCacheDetail): string {
    const lookups = (cache.hits ?? 0) + (cache.misses ?? 0);
    return lookups === 0 ? '—' : `${Math.round(((cache.hits ?? 0) / lookups) * 100)}%`;
  }

  /** This method re-reads every cache's statistics. */
  async load(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      this.caches.set(await firstValueFrom(this.cacheApi.apiDnsCacheGet()));
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load cache statistics.'));
    } finally {
      this.loading.set(false);
    }
  }

  /** This method confirms, then flushes, one cache -- or every cache when none is given. */
  async flush(cache?: DnsCacheDetail): Promise<void> {
    const confirmed = await this.confirm({
      title: cache ? `Flush the ${this.label(cache).toLowerCase()} cache?` : 'Flush every cache?',
      message:
        'Every cached answer is dropped, so the next lookup for each name goes upstream again. ' +
        'Hit and miss counts restart from zero.',
      confirmLabel: 'Flush',
    });

    if (!confirmed) return;

    await this.run(async () => {
      const result = await firstValueFrom(
        this.cacheApi.apiDnsCacheDelete(cache?.name ? { cache: cache.name } : {}),
      );
      this.notification.success(`Flushed ${countLabel(result.removedEntryCount ?? 0)}.`);
    }, 'Could not flush the cache.');
  }

  /** This method forgets every cached answer for the name typed into {@link removeForm}. */
  async removeName(): Promise<void> {
    if (this.removeForm.invalid) return;

    const name = this.removeForm.controls.name.value.trim();

    await this.run(async () => {
      const result = await firstValueFrom(this.cacheApi.apiDnsCacheEntriesNameDelete({ name }));
      const removed = result.removedEntryCount ?? 0;
      this.notification.success(
        removed === 0
          ? `Nothing was cached for ${name}.`
          : `Removed ${countLabel(removed)} for ${name}.`,
      );
      this.removeForm.reset();
    }, `Could not remove ${name} from the cache.`);
  }

  /** This method runs one write action, then refreshes the statistics whatever the outcome. */
  private async run(action: () => Promise<void>, failureMessage: string): Promise<void> {
    this.busy.set(true);

    try {
      await action();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, failureMessage));
    } finally {
      this.busy.set(false);
      await this.load();
    }
  }

  /** This method shows {@link ConfirmDialogComponent} and resolves once the operator answers. */
  private confirm(data: ConfirmDialogData): Promise<boolean> {
    this.confirmDialogData.set(data);
    this.confirmDialogVisible.set(true);
    return new Promise<boolean>((resolve) => {
      this.confirmResolve = resolve;
    });
  }

  /** This method runs once {@link ConfirmDialogComponent} resolves, settling the pending {@link confirm} promise. */
  onConfirmDialogResolved(confirmed: boolean): void {
    this.confirmDialogVisible.set(false);
    this.confirmResolve?.(confirmed);
    this.confirmResolve = null;
  }
}

/** This function words an entry count, e.g. `1 entry` or `12 entries`. */
function countLabel(count: number): string {
  return count === 1 ? '1 entry' : `${count} entries`;
}
