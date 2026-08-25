import { describe, expect, it, vi } from 'vitest'
import { AuthContext } from './auth'
import { CommunicationsApi } from './communicationsApi'

const item = { id: 'c-1', version: 4, status: 'draft', title: 'Banner', mediaType: 'image', priority: 2, startsAt: '', endsAt: '' } as const

describe('CommunicationsApi', () => {
  it('lists with the authenticated token and creates with idempotency', async () => {
    const fetch = vi.fn()
      .mockResolvedValueOnce(new Response('[]', { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(item), { status: 201 }))
    vi.stubGlobal('fetch', fetch)
    const auth = new AuthContext({ getItem: () => 'token', setItem: () => {}, removeItem: () => {}, clear: () => {}, key: () => null, length: 0 } as Storage)
    const api = new CommunicationsApi(auth)
    await api.list()
    await api.create({ title: 'Banner', mediaType: 'image', priority: 2, startsAt: '2026-01-01', endsAt: '2026-01-02' }, 'key-1')
    expect(fetch.mock.calls[0][1].headers.Authorization).toBe('Bearer token')
    expect(fetch.mock.calls[1][1].headers['Idempotency-Key']).toBe('key-1')
  })

  it('sends expectedVersion for lifecycle transitions', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(item), { status: 200 })); vi.stubGlobal('fetch', fetch)
    await new CommunicationsApi(new AuthContext()).transition(item, 'publish', 'key-2')
    expect(fetch.mock.calls[0][1].body).toBe('{"expectedVersion":4}')
    expect(fetch.mock.calls[0][1].headers['Idempotency-Key']).toBe('key-2')
  })
})
