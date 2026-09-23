export type RefundMethod = 'Cash' | 'Pix' | 'Credit' | 'Debit' | 'SoftPos'

export interface RefundCapability {
  supported: boolean
  providerBacked: boolean
  manualConfirmation: boolean
}

// Kept as a single capability map so the UI never derives financial state or
// treats provider-backed methods as Cash. A future server capability document
// can replace this map without changing the view contract.
export function refundCapability(method: string): RefundCapability {
  switch (method) {
    case 'Cash': return { supported: true, providerBacked: false, manualConfirmation: true }
    case 'Pix': case 'Credit': case 'Debit': case 'SoftPos': return { supported: true, providerBacked: true, manualConfirmation: false }
    default: return { supported: false, providerBacked: false, manualConfirmation: false }
  }
}
