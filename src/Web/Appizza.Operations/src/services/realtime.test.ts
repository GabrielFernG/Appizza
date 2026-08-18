import { beforeEach, describe, expect, it, vi } from 'vitest'

const state = vi.hoisted(() => ({ handlers: new Map<string, (payload: Record<string, string>) => void>(), start: vi.fn(), stop: vi.fn() }))
vi.mock('@microsoft/signalr', () => ({ LogLevel: { Warning: 2 }, HubConnectionBuilder: class {
  withUrl() { return this }; withAutomaticReconnect() { return this }; configureLogging() { return this }; build() { return { on: (name: string, fn: (payload: Record<string, string>) => void) => state.handlers.set(name, fn), start: state.start, stop: state.stop } }
} }))
import { OperationsRealtime } from './realtime'

describe('OperationsRealtime', () => {
  beforeEach(() => { state.handlers.clear(); state.start.mockResolvedValue(undefined); state.stop.mockResolvedValue(undefined) })
  it.each(['DeliveryChanged', 'OrderStatusChanged'])('invalidates through GET callback for %s', async event => {
    const callback = vi.fn(); const realtime = new OperationsRealtime(() => 'token', callback); await realtime.start()
    state.handlers.get(event)?.({ eventId: 'e-1', eventType: 'delivery' }); state.handlers.get(event)?.({ eventId: 'e-1' })
    expect(callback).toHaveBeenCalledTimes(1); expect(callback).toHaveBeenCalledWith('e-1', 'delivery')
  })
  it('supports reconnect lifecycle cleanup', async () => { const realtime = new OperationsRealtime(() => null, vi.fn()); await realtime.start(); await realtime.stop(); expect(state.stop).toHaveBeenCalled() })
})
