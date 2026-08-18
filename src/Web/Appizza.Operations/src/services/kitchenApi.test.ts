import { describe, expect, it, vi } from 'vitest'
import { AuthContext } from './auth'
import { KitchenApi } from './kitchenApi'
import { ApiProblem, parseProblem } from './problemDetails'
import type { KitchenDeliveryItem } from '../models/delivery'

const item = { productionItemId: 'pi-1', productionVersion: 7, deliveryConfirmationId: 'dc-1', deliveryConfirmationVersion: 3, deliveryContestId: 'contest-1', deliveryContestVersion: 2 } as unknown as KitchenDeliveryItem

describe('KitchenApi Delivery operations', () => {
  it('sends ready with expected version and idempotency key', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response('{}', { status: 200 })); vi.stubGlobal('fetch', fetch)
    await new KitchenApi(new AuthContext()).sendToTable(item, 'key-1')
    expect(fetch).toHaveBeenCalledWith('/api/v1/operations/kitchen/production-items/pi-1/send-to-table', expect.objectContaining({ body: '{"expectedVersion":7}' }))
    expect(fetch.mock.calls[0][1].headers['Idempotency-Key']).toBe('key-1')
  })

  it('confirms employee and resolves both contest outcomes with current versions', async () => {
    const fetch = vi.fn().mockImplementation(() => Promise.resolve(new Response('{}', { status: 200 }))); vi.stubGlobal('fetch', fetch); const api = new KitchenApi(new AuthContext())
    await api.confirmEmployee(item, 'confirm-key'); await api.resolve(item, 'confirm_delivered', 'resolve-key'); await api.resolve(item, 'retry_delivery', 'retry-key')
    expect(fetch.mock.calls[0][0]).toContain('/delivery-confirmations/dc-1/confirm'); expect(fetch.mock.calls[0][1].body).toContain('"expectedVersion":3')
    expect(fetch.mock.calls[1][1].body).toContain('"resolution":"confirm_delivered"'); expect(fetch.mock.calls[2][1].body).toContain('"resolution":"retry_delivery"')
  })

  it('maps 403 and 409 ProblemDetails without inventing state', async () => {
    const response = new Response(JSON.stringify({ title: 'Conflict', detail: 'changed', errorCode: 'CONCURRENCY_CONFLICT' }), { status: 409 }); const problem = await parseProblem(response)
    expect(problem).toBeInstanceOf(ApiProblem); expect(problem.problem.errorCode).toBe('CONCURRENCY_CONFLICT'); expect(problem.message).toBe('changed')
  })
})
