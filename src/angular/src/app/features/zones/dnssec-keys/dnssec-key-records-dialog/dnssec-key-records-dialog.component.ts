import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, input, output, signal } from '@angular/core';
import { Clipboard } from '@angular/cdk/clipboard';
import {
  ButtonDirective,
  ModalBodyComponent,
  ModalComponent,
  ModalFooterComponent,
  ModalHeaderComponent,
  ModalTitleDirective,
  TooltipDirective,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { DnssecKeyDetail, DnssecKeyRoleEnum } from '../../../../api-client';
import { dnssecAlgorithmNumber } from '../dnssec-key.model';

/** One labeled, copyable value in {@link DnssecKeyRecordsDialogComponent}. */
interface CopyableField {
  label: string;
  /** Exactly what is copied. */
  value: string;
  /** A human-readable gloss shown after the value but never copied, e.g. an algorithm's name. */
  note?: string;
}

/**
 * This component shows one key's published records -- what an operator hands to the parent zone
 * (a registrar, or whoever hosts the parent) when signing a zone or rolling its KSK. The DS record
 * and its parts come from the API (`DnssecKeyDetail.dsRecord`/`dsDigest`), computed from the same
 * DNSKEY the zone publishes; registrars differ in whether they want the whole DS record, its parts
 * entered separately, or the DNSKEY itself, so each is copyable on its own. A ZSK is never
 * referenced from the parent, so for one the dialog shows only its DNSKEY.
 */
@Component({
  selector: 'app-dnssec-key-records-dialog',
  imports: [
    DatePipe,
    NgTemplateOutlet,
    ButtonDirective,
    ModalBodyComponent,
    ModalComponent,
    ModalFooterComponent,
    ModalHeaderComponent,
    ModalTitleDirective,
    TooltipDirective,
    IconDirective,
  ],
  templateUrl: './dnssec-key-records-dialog.component.html',
  styleUrl: './dnssec-key-records-dialog.component.scss',
})
export class DnssecKeyRecordsDialogComponent {
  private readonly clipboard = inject(Clipboard);

  /** Whether this dialog is currently shown -- a caller drives this with its own signal. */
  readonly visible = input<boolean>(false);

  /** The key whose records this dialog shows. */
  readonly key = input<DnssecKeyDetail | null>(null);

  /** Emits this dialog's own new visibility -- `false` on every close. */
  readonly visibleChange = output<boolean>();

  readonly isKsk = computed(() => this.key()?.role === DnssecKeyRoleEnum.Ksk);

  /** The DS record, then its parts, for registrars that ask for them one field at a time. */
  readonly parentFields = computed<CopyableField[]>(() => {
    const key = this.key();
    if (!key?.dsRecord || !key.dsDigest) return [];

    return [
      { label: 'DS record', value: key.dsRecord },
      { label: 'Key tag', value: String(key.keyTag ?? '') },
      {
        label: 'Algorithm',
        value: String(dnssecAlgorithmNumber(key.algorithm) ?? ''),
        note: key.algorithm,
      },
      { label: 'Digest type', value: '2', note: 'SHA-256' },
      { label: 'Digest', value: key.dsDigest },
    ];
  });

  /** The DNSKEY record as the zone publishes it. */
  readonly dnsKeyField = computed<CopyableField>(() => ({
    label: 'DNSKEY record',
    value: this.key()?.dnsKeyRecord ?? '',
  }));

  /** The label of whichever value was most recently copied, briefly, to confirm it on its button. */
  readonly copiedLabel = signal<string | null>(null);

  /** This method copies one value and briefly confirms it on the button that was clicked. */
  copy(field: CopyableField): void {
    this.clipboard.copy(field.value);
    this.copiedLabel.set(field.label);
    setTimeout(() => {
      if (this.copiedLabel() === field.label) this.copiedLabel.set(null);
    }, 2000);
  }

  close(): void {
    this.visibleChange.emit(false);
  }
}
