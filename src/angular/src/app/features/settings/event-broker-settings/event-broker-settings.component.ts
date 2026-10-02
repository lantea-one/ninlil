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
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component is the "Event broker" settings page (`/settings/event-broker`). It edits the
 * event transport (in-memory, RabbitMQ or ActiveMQ) and the broker connection through `GET` and
 * `PATCH /api/event-broker-settings` (`eventbroker:read`, `eventbroker:write`), and shows whether
 * the broker is connected.
 *
 * <p>Broker passwords are never returned, so a blank password field means "keep the stored one"
 * (see {@link buildBrokerPatchFields}). Only the sub-object of the selected transport is sent.
 * Changes need a restart, since `Program.cs` picks the transport at startup; the backend's note in
 * the success notification says so.
 */
@Component({
  selector: 'app-event-broker-settings',
  imports: [
    IfPermissionDirective,
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

  /** When this section was last written, from the cluster config sync; {@link originatingNodeId} names the node. */
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
   * This method PATCHes `transport` and the `rabbitMq` or `activeMq` sub-object of the selected
   * transport, without a blank password (see {@link buildBrokerPatchFields}), then reloads.
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
      // The password is never returned; clearing the field keeps "blank means unchanged" true.
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
