import { describe, expect, it, vi } from 'vitest'
import { AuthContext } from './auth'
import { ClosingApi } from './closingApi'

describe('ClosingApi', () => {
  it('loads authoritative balance with bearer and GET', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ sessionId: 's', status: 'awaiting_payment', version: 3, totalAmount: 113.37, paidAmount: 40.12, reservedAmount: 1, outstandingAmount: 81.91 }), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock); const auth = new AuthContext({ getItem: () => 'token', setItem: () => undefined, removeItem: () => undefined, clear: () => undefined, key: () => null, length: 0 } as Storage)
    const result = await new ClosingApi(auth).balance('session/1')
    expect(result.outstandingAmount).toBe(81.91); expect(fetchMock).toHaveBeenCalledWith('/api/v1/operations/sessions/session%2F1/balance', { headers: { Authorization: 'Bearer token' } })
  })
  it('surfaces API errors without fallback', async () => { const fetchMock = vi.fn().mockResolvedValue(new Response('', { status: 403 })); vi.stubGlobal('fetch', fetchMock); await expect(new ClosingApi(new AuthContext()).balance('x')).rejects.toBeTruthy(); expect(fetchMock).toHaveBeenCalledTimes(1) })
  it('cancels closing with authoritative version and idempotency key', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ sessionId: 's', status: 'open', version: 8 }), { status: 200 })); vi.stubGlobal('fetch', fetchMock)
    await new ClosingApi(new AuthContext()).cancelClosing('s', 7, 'operator request', 'key-1')
    expect(fetchMock).toHaveBeenCalledWith('/api/v1/operations/sessions/s/closing/cancel', { method: 'POST', headers: { Authorization: 'Bearer ', 'Content-Type': 'application/json', 'Idempotency-Key': 'key-1' }, body: JSON.stringify({ expectedVersion: 7, reason: 'operator request' }) })
  })
  it('creates a refund with a stable idempotency key and only client fields', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ refundId: 'r', paymentAttemptId: 'p', amount: 2, reason: 'x', status: 'created' }), { status: 202 })); vi.stubGlobal('fetch', fetchMock)
    await new ClosingApi(new AuthContext()).createRefund('p', 2, 'x', 'key-1')
    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/payments/p/refunds')
    expect(fetchMock.mock.calls[0][1]).toMatchObject({ method: 'POST', headers: expect.objectContaining({ 'Idempotency-Key': 'key-1' }), body: JSON.stringify({ amount: 2, reason: 'x' }) })
  })
  it('confirms cash through the explicit action endpoint', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ refundId: 'r', status: 'completed' }), { status: 200 })); vi.stubGlobal('fetch', fetchMock)
    await new ClosingApi(new AuthContext()).confirmCash('r')
    expect(fetchMock).toHaveBeenCalledWith('/api/v1/payments/refunds/r/confirm-cash', { method: 'POST', headers: { Authorization: 'Bearer ' } })
  })
})
