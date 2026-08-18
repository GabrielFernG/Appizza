import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'

export type DeliveryInvalidation = (eventId: string, eventType: string) => void

export class OperationsRealtime {
  private connection: HubConnection | null = null
  constructor(private readonly token: () => string | null, private readonly onInvalidation: DeliveryInvalidation) {}
  async start(): Promise<void> {
    this.connection = new HubConnectionBuilder().withUrl('/hubs/phase1', { accessTokenFactory: () => this.token() ?? '' }).withAutomaticReconnect().configureLogging(LogLevel.Warning).build()
    const invalidate = (payload: { eventId?: string; eventType?: string }) => { if (payload.eventId && payload.eventType) this.onInvalidation(payload.eventId, payload.eventType) }
    this.connection.on('DeliveryChanged', invalidate)
    this.connection.on('OrderStatusChanged', invalidate)
    await this.connection.start()
  }
  async stop(): Promise<void> { if (this.connection) await this.connection.stop(); this.connection = null }
}
