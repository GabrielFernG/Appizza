import { describe, expect, it } from 'vitest'
import { refundCapability } from './refundCapabilities'

describe('refund capabilities', () => {
  it('keeps Cash manual and provider-free', () => expect(refundCapability('Cash')).toEqual({ supported: true, providerBacked: false, manualConfirmation: true }))
  it('supports provider-backed methods including SoftPos', () => {
    for (const method of ['Pix', 'Credit', 'Debit', 'SoftPos']) expect(refundCapability(method)).toEqual({ supported: true, providerBacked: true, manualConfirmation: false })
  })
  it('does not expose unknown methods as refundable', () => expect(refundCapability('Unknown')).toEqual({ supported: false, providerBacked: false, manualConfirmation: false }))
})
