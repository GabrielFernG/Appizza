import type { AuthContext } from './auth'
import { parseProblem } from './problemDetails'
import type { Communication, MediaAsset } from '../models/communication'

export interface CommunicationInput {
  title: string
  body?: string | null
  mediaAssetId?: string | null
  mediaType: 'image' | 'video'
  priority: number
  startsAt: string
  endsAt: string
}

export class CommunicationsApi {
  constructor(private readonly auth: AuthContext, private readonly baseUrl = '/api/v1') {}
  private async request<T>(path: string, init: RequestInit = {}): Promise<T> {
    const response = await fetch(`${this.baseUrl}${path}`, {
      ...init,
      headers: { Authorization: `Bearer ${this.auth.token ?? ''}`, 'Content-Type': 'application/json', ...(init.headers ?? {}) },
    })
    if (!response.ok) throw await parseProblem(response)
    return response.status === 204 ? undefined as T : await response.json() as T
  }
  async list(): Promise<Communication[]> { return this.request('/operations/communications') }
  async me(): Promise<NonNullable<AuthContext['user']>> { const value = await this.request<NonNullable<AuthContext['user']>>('/auth/me'); this.auth.user = value; return value }
  async media(): Promise<MediaAsset[]> { return this.request('/operations/media/assets') }
  async create(input: CommunicationInput, key: string): Promise<Communication> { return this.request('/operations/communications', { method: 'POST', headers: { 'Idempotency-Key': key }, body: JSON.stringify(input) }) }
  async transition(item: Communication, action: 'publish' | 'pause' | 'archive', key: string): Promise<Communication> {
    return this.request(`/operations/communications/${item.id}/${action}`, { method: 'POST', headers: { 'Idempotency-Key': key }, body: JSON.stringify({ expectedVersion: item.version }) })
  }
}
