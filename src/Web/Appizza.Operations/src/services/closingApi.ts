import type { AuthContext } from './auth'
import { parseProblem } from './problemDetails'

export interface SessionBalance { sessionId: string; status: string; version: number; subtotalAmount: number; discountAmount: number; adjustmentAmount: number; totalAmount: number; paidAmount: number; reservedAmount: number; outstandingAmount: number; availableToReserveAmount: number; closingStartedAt?: string | null; paidAt?: string | null; closedAt?: string | null }
export interface ClosingSession { sessionId: string; status: string; version: number; openedAt: string; closingStartedAt?: string | null; diningTableId: string }
export interface PaymentAllocation { id: string; amount: number; stableOrder: number; participantId?: string | null; orderItemId?: string | null }
export interface PaymentPlan { id: string; logicalPlanId: string; sessionId: string; version: number; mode: string; allocations: PaymentAllocation[] }
export interface RefundSummary { completedAmount: number; inFlightAmount: number; availableAmount: number }
export interface RefundHistory { refundId: string; amount: number; reason: string; status: string; createdAt: string; updatedAt: string; reconciliationRequired: boolean }
export interface PaymentAttempt { id: string; paymentPlanId?: string | null; paymentPlanVersion?: number | null; method: string; status: string; amount: number; reservedAmount: number; provider?: string | null; providerReference?: string | null; version: number; refundSummary: RefundSummary; refunds: RefundHistory[] }
export interface SessionPaymentDetails { sessionId: string; paymentPlans: PaymentPlan[]; paymentAttempts: PaymentAttempt[] }
export interface CancelClosingResponse { sessionId: string; status: string; version: number; [key: string]: unknown }
export interface RefundResponse { refundId: string; paymentAttemptId: string; amount: number; reason: string; status: string; completedAmount: number; inFlightAmount: number; availableAmount: number }

export class ClosingApi {
  constructor(private readonly auth: AuthContext, private readonly baseUrl = `${import.meta.env.VITE_APPIZZA_API_URL ?? ''}/api/v1`) {}
  async me(): Promise<void> {
    const response = await fetch(`${this.baseUrl}/auth/me`, { headers: { Authorization: `Bearer ${this.auth.token ?? ''}` } })
    if (!response.ok) throw await parseProblem(response)
    this.auth.user = await response.json()
  }
  async balance(sessionId: string): Promise<SessionBalance> {
    const response = await fetch(`${this.baseUrl}/operations/sessions/${encodeURIComponent(sessionId)}/balance`, { headers: { Authorization: `Bearer ${this.auth.token ?? ''}` } })
    if (!response.ok) throw await parseProblem(response)
    return await response.json() as SessionBalance
  }
  async closingSessions(): Promise<ClosingSession[]> {
    const response = await fetch(`${this.baseUrl}/operations/sessions/closing`, { headers: { Authorization: `Bearer ${this.auth.token ?? ''}` } })
    if (!response.ok) throw await parseProblem(response)
    return await response.json() as ClosingSession[]
  }
  async paymentDetails(sessionId: string): Promise<SessionPaymentDetails> {
    const response = await fetch(`${this.baseUrl}/operations/sessions/${encodeURIComponent(sessionId)}/payments`, { headers: { Authorization: `Bearer ${this.auth.token ?? ''}` } })
    if (!response.ok) throw await parseProblem(response)
    return await response.json() as SessionPaymentDetails
  }
  async cancelClosing(sessionId: string, expectedVersion: number, reason: string, idempotencyKey: string): Promise<CancelClosingResponse> {
    const response = await fetch(`${this.baseUrl}/operations/sessions/${encodeURIComponent(sessionId)}/closing/cancel`, { method: 'POST', headers: { Authorization: `Bearer ${this.auth.token ?? ''}`, 'Content-Type': 'application/json', 'Idempotency-Key': idempotencyKey }, body: JSON.stringify({ expectedVersion, reason }) })
    if (!response.ok) throw await parseProblem(response)
    return await response.json() as CancelClosingResponse
  }
  async createRefund(paymentId: string, amount: number, reason: string, idempotencyKey: string): Promise<RefundResponse> {
    const response = await fetch(`${this.baseUrl}/payments/${encodeURIComponent(paymentId)}/refunds`, { method: 'POST', headers: { Authorization: `Bearer ${this.auth.token ?? ''}`, 'Content-Type': 'application/json', 'Idempotency-Key': idempotencyKey }, body: JSON.stringify({ amount, reason }) })
    if (!response.ok) throw await parseProblem(response)
    return await response.json() as RefundResponse
  }
  async confirmCash(refundId: string): Promise<RefundResponse> {
    const response = await fetch(`${this.baseUrl}/payments/refunds/${encodeURIComponent(refundId)}/confirm-cash`, { method: 'POST', headers: { Authorization: `Bearer ${this.auth.token ?? ''}` } })
    if (!response.ok) throw await parseProblem(response)
    return await response.json() as RefundResponse
  }
}
