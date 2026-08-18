import { describe, expect, it } from 'vitest'
import { IntentKey, newIdempotencyKey } from './idempotency'

describe('delivery idempotency', () => {
  it('reuses a key for retries of the same intent', () => {
    const intent = new IntentKey()
    expect(intent.for('send-to-table:item-1')).toBe(intent.for('send-to-table:item-1'))
    expect(intent.for('send-to-table:item-1')).not.toBe(intent.for('send-to-table:item-2'))
  })

  it('creates UUID keys for new intentions', () => {
    expect(newIdempotencyKey()).toMatch(/^[0-9a-f-]{36}$/i)
  })
})
