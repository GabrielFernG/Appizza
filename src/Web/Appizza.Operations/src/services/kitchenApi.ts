import type { AuthContext } from './auth'
import { parseProblem } from './problemDetails'
import type { KitchenDeliveryItem } from '../models/delivery'

export class KitchenApi {
  constructor(private readonly auth: AuthContext, private readonly baseUrl = '/api/v1') {}
  private async request<T>(path: string, init: RequestInit = {}): Promise<T> {
    const response = await fetch(`${this.baseUrl}${path}`, {
      ...init,
      headers: { Authorization: `Bearer ${this.auth.token ?? ''}`, 'Content-Type': 'application/json', ...(init.headers ?? {}) },
    })
    if (!response.ok) throw await parseProblem(response)
    return response.status === 204 ? undefined as T : await response.json() as T
  }
  async me(): Promise<NonNullable<AuthContext['user']>> { const value = await this.request<NonNullable<AuthContext['user']>>('/auth/me'); this.auth.user = value; return value }
  async productionItems(stationId?: string): Promise<KitchenDeliveryItem[]> {
    const query = stationId ? `?stationId=${encodeURIComponent(stationId)}` : ''
    return this.request<KitchenDeliveryItem[]>(`/operations/kitchen/production-items${query}`)
  }
  async stations(): Promise<Array<{ id: string; name: string }>> { return this.request('/operations/kitchen/stations') }
  async accept(item: KitchenDeliveryItem, key: string): Promise<unknown> {
    return this.request(`/operations/kitchen/production-items/${item.productionItemId}/accept`, { method: 'POST', headers: { 'Idempotency-Key': key } })
  }
  async sendToTable(item: KitchenDeliveryItem, key: string): Promise<unknown> {
    return this.request(`/operations/kitchen/production-items/${item.productionItemId}/send-to-table`, { method: 'POST', body: JSON.stringify({ expectedVersion: item.productionVersion }), headers: { 'Idempotency-Key': key } })
  }
  async confirmEmployee(item: KitchenDeliveryItem, key: string): Promise<unknown> {
    if (!item.deliveryConfirmationId || item.deliveryConfirmationVersion === null) throw new Error('Delivery confirmation is not available')
    return this.request(`/operations/kitchen/delivery-confirmations/${item.deliveryConfirmationId}/confirm`, { method: 'POST', body: JSON.stringify({ expectedVersion: item.deliveryConfirmationVersion }), headers: { 'Idempotency-Key': key } })
  }
  async resolve(item: KitchenDeliveryItem, resolution: 'confirm_delivered' | 'retry_delivery', key: string, reason?: string): Promise<unknown> {
    if (!item.deliveryContestId || item.deliveryContestVersion === null) throw new Error('Delivery contest is not available')
    return this.request(`/operations/kitchen/delivery-contests/${item.deliveryContestId}/resolve`, { method: 'POST', body: JSON.stringify({ resolution, reason, expectedVersion: item.deliveryContestVersion }), headers: { 'Idempotency-Key': key } })
  }
}
