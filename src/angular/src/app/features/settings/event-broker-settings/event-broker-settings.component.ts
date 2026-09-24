import { Component, OnInit, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import {
  AlertComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardFooterComponent,
  CardHeaderComponent,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  FormSelectDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { firstValueFrom } from 'rxjs';
import { EventBrokerSettingsService, EventTransportEnum } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { ClusterSyncInfoComponent } from '../../../shared/cluster-sync-info/cluster-sync-info.component';
import { buildBrokerPatchFields } from './event-broker-settings.model';

/**
 * This component reads and writes this host's own message-broker event-transport configuration
 * via `GET`/`PATCH /api/event-broker-settings`. Unlike every other settings form, this one entity
 * carries a genuine secret (each broker's own connection `password`), which
 * `EventBrokerSettingsController.Get` deliberately never returns -- so this form can never
 * pre-fill it, and leaving the password input blank on save must mean "leave the stored password
 * untouched," never "clear it." See {@link buildBrokerPatchFields}'s own doc comment for the full,
 * checked-against-the-backend reasoning; this component applies it to whichever of `rabbitMq`/
 * `activeMq` matches the currently-selected transport before sending the `PATCH`. Every successful
 * `PATCH`'s own "restart required" note is surfaced directly in the success notification, never
 * suppressed -- doubly relevant here, since `EventBrokerSettingsController`'s own doc comment
 * notes that *which* broker project's own extension method runs at all is decided once in
 * `Program.cs`, so switching transports always needs a restart to take effect.
 */
@Component({
  selector: 'app-event-broker-settings',
  imports: [
    ReactiveFormsModule,
    AlertComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardFooterComponent,
    CardHeaderComponent,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    FormSelectDirective,
    IconDirective,
    SpinnerComponent,
    ClusterSyncInfoComponent,
  ],
  templateUrl: './event-broker-settings.component.html',
  styleUrl: './event-broker-settings.component.scss',
})
export class EventBrokerSettingsComponent implements OnInit {
  private readonly eventBrokerApi = inject(EventBrokerSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly transportOptions: readonly EventTransportEnum[] = [
    EventTransportEnum.InMemory,
    EventTransportEnum.RabbitMq,
    EventTransportEnum.ActiveMq,
  ];

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly isConnected = signal<boolean | null>(null);

  /** When this settings section was last written and by which cluster node, per the cluster's own config-sync mechanism. */
  readonly lastModifiedUtc = signal<string | null | undefined>(null);
  readonly originatingNodeId = signal<string | null | undefined>(null);

  readonly form = this.formBuilder.group({
    transport: [EventTransportEnum.InMemory as EventTransportEnum],
    rabbitMqHostName: [''],
    rabbitMqPort: [5672],
    rabbitMqUserName: [''],
    rabbitMqPassword: [''],
    rabbitMqVirtualHost: [''],
    rabbitMqExchangeName: [''],
    rabbitMqInitialReconnectDelay: ['00:00:05'],
    rabbitMqMaxReconnectDelay: ['00:01:00'],
    activeMqHostName: [''],
    activeMqPort: [61616],
    activeMqUserName: [''],
    activeMqPassword: [''],
    activeMqTopicName: [''],
    activeMqInitialReconnectDelay: ['00:00:05'],
    activeMqMaxReconnectDelay: ['00:01:00'],
    activeMqPublishTimeout: ['00:00:30'],
  });

  ngOnInit(): void {
    void this.loadSettings();
  }

  /**
   * This method PATCHes `transport` plus whichever of `rabbitMq`/`activeMq` matches the
   * currently-selected transport -- always omitting that sub-object's own `password` field
   * entirely when left blank, per {@link buildBrokerPatchFields}.
   */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();
    const body: Record<string, unknown> = { transport: value.transport };

    if (value.transport === EventTransportEnum.RabbitMq) {
      body['rabbitMq'] = buildBrokerPatchFields({
        hostName: value.rabbitMqHostName,
        port: value.rabbitMqPort,
        userName: value.rabbitMqUserName,
        password: value.rabbitMqPassword,
        virtualHost: value.rabbitMqVirtualHost,
        exchangeName: value.rabbitMqExchangeName,
        initialReconnectDelay: value.rabbitMqInitialReconnectDelay,
        maxReconnectDelay: value.rabbitMqMaxReconnectDelay,
      });
    } else if (value.transport === EventTransportEnum.ActiveMq) {
      body['activeMq'] = buildBrokerPatchFields({
        hostName: value.activeMqHostName,
        port: value.activeMqPort,
        userName: value.activeMqUserName,
        password: value.activeMqPassword,
        topicName: value.activeMqTopicName,
        initialReconnectDelay: value.activeMqInitialReconnectDelay,
        maxReconnectDelay: value.activeMqMaxReconnectDelay,
        publishTimeout: value.activeMqPublishTimeout,
      });
    }

    try {
      const result = await firstValueFrom(
        this.eventBrokerApi.apiEventBrokerSettingsPatch({ body }),
      );
      this.notification.success(result.note ?? 'Event broker settings saved.');
      // Never re-populate the password field from the response -- it is never returned, so
      // clearing it back to blank keeps this form's own "blank means unchanged" contract honest.
      this.form.patchValue({ rabbitMqPassword: '', activeMqPassword: '' });
      await this.loadSettings();
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save event broker settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.eventBrokerApi.apiEventBrokerSettingsGet());
      this.isConnected.set(settings.isConnected ?? null);
      this.form.patchValue({
        transport: settings.transport ?? EventTransportEnum.InMemory,
        rabbitMqHostName: settings.rabbitMq?.hostName ?? '',
        rabbitMqPort: settings.rabbitMq?.port ?? 5672,
        rabbitMqUserName: settings.rabbitMq?.userName ?? '',
        rabbitMqVirtualHost: settings.rabbitMq?.virtualHost ?? '',
        rabbitMqExchangeName: settings.rabbitMq?.exchangeName ?? '',
        rabbitMqInitialReconnectDelay: settings.rabbitMq?.initialReconnectDelay ?? '00:00:05',
        rabbitMqMaxReconnectDelay: settings.rabbitMq?.maxReconnectDelay ?? '00:01:00',
        activeMqHostName: settings.activeMq?.hostName ?? '',
        activeMqPort: settings.activeMq?.port ?? 61616,
        activeMqUserName: settings.activeMq?.userName ?? '',
        activeMqTopicName: settings.activeMq?.topicName ?? '',
        activeMqInitialReconnectDelay: settings.activeMq?.initialReconnectDelay ?? '00:00:05',
        activeMqMaxReconnectDelay: settings.activeMq?.maxReconnectDelay ?? '00:01:00',
        activeMqPublishTimeout: settings.activeMq?.publishTimeout ?? '00:00:30',
      });
      this.lastModifiedUtc.set(settings.lastModifiedUtc);
      this.originatingNodeId.set(settings.originatingNodeId);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load event broker settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}
