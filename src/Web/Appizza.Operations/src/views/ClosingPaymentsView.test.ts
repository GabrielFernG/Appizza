import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
const routeState = vi.hoisted(() => ({ query: {} as Record<string, string> }))
vi.mock('vue-router', () => ({ useRoute: () => routeState, useRouter: () => ({ push: vi.fn() }) }))
import ClosingPaymentsView from './ClosingPaymentsView.vue'

const stubs = { 'v-container': { template: '<div><slot /></div>' }, 'v-text-field': { inheritAttrs: false, props: ['modelValue'], emits: ['update:modelValue'], template: '<input :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />' }, 'v-btn': { inheritAttrs: false, props: { disabled: Boolean }, emits: ['click'], template: '<button :disabled="disabled" @click="$emit(\'click\')"><slot /></button>' }, 'v-alert': { template: '<div><slot /></div>' }, 'v-card': { template: '<div><slot /></div>' }, 'v-card-text': { template: '<div><slot /></div>' }, 'v-progress-circular': true }

describe('ClosingPaymentsView', () => {
  beforeEach(() => { routeState.query = {}; vi.clearAllMocks() })
  it('initializes session id from routed query', async () => {
    routeState.query = { sessionId: 'known-session-id' }
    const fetchMock = vi.fn().mockImplementation((url: string) => new Response(JSON.stringify(url.endsWith('/payments') ? { sessionId: 'known-session-id', paymentPlans: [], paymentAttempts: [] } : { sessionId: 'known-session-id', status: 'awaiting_payment', version: 1, totalAmount: 1, paidAmount: 0, reservedAmount: 0, outstandingAmount: 1 }), { status: 200 })); vi.stubGlobal('fetch', fetchMock)
    const wrapper = mount(ClosingPaymentsView, { global: { stubs } }); await flushPromises(); expect(fetchMock.mock.calls[0][0]).toContain('/known-session-id/balance'); expect(fetchMock.mock.calls[1][0]).toContain('/known-session-id/payments')
  })
  it('renders authoritative values and refreshes with GET only', async () => {
    const fetchMock = vi.fn().mockImplementation((url: string) => new Response(JSON.stringify(url.endsWith('/payments') ? { sessionId: 's', paymentPlans: [], paymentAttempts: [] } : { sessionId: 's', status: 'awaiting_payment', version: 2, totalAmount: 113.37, paidAmount: 40.12, reservedAmount: 1, outstandingAmount: 81.91 }), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)
    const wrapper = mount(ClosingPaymentsView, { global: { stubs } })
    const input = wrapper.find('input'); await input.setValue('s'); await wrapper.find('button').trigger('click'); await flushPromises()
    expect(wrapper.text()).toContain('81.91'); expect(wrapper.text()).toContain('awaiting_payment'); expect(fetchMock).toHaveBeenCalledTimes(2); expect(fetchMock.mock.calls[0][1].headers.Authorization).toBe('Bearer ')
  })
  it('clears stale data and shows error on a subsequent failed query', async () => {
    const fetchMock = vi.fn().mockImplementationOnce(() => new Response(JSON.stringify({ sessionId: 's', status: 'open', version: 1, totalAmount: 1, paidAmount: 0, reservedAmount: 0, outstandingAmount: 1 }), { status: 200 })).mockImplementationOnce(() => new Response(JSON.stringify({ sessionId: 's', paymentPlans: [], paymentAttempts: [] }), { status: 200 })).mockImplementationOnce(() => new Response('', { status: 404 })).mockImplementationOnce(() => new Response('', { status: 404 }))
    vi.stubGlobal('fetch', fetchMock); const wrapper = mount(ClosingPaymentsView, { global: { stubs } }); const input = wrapper.find('input'); await input.setValue('s'); await wrapper.find('button').trigger('click'); await flushPromises(); expect(wrapper.text()).toContain('open'); expect(fetchMock).toHaveBeenCalledTimes(2); await wrapper.find('button').trigger('click'); await flushPromises(); expect(wrapper.text()).not.toContain('open'); expect(wrapper.text()).toContain('404'); expect(fetchMock).toHaveBeenCalledTimes(3); expect(fetchMock.mock.calls[0][0]).toContain('/balance'); expect(fetchMock.mock.calls[1][0]).toContain('/payments'); expect(fetchMock.mock.calls[2][0]).toContain('/balance')
  })
  it('sends one cancel mutation with balance version and refreshes authoritatively', async () => {
    const responses = [
      { sessionId: 's', status: 'awaiting_payment', version: 7, totalAmount: 10, paidAmount: 0, reservedAmount: 0, outstandingAmount: 10 },
      { sessionId: 's', paymentPlans: [], paymentAttempts: [] },
      { sessionId: 's', status: 'open', version: 8, totalAmount: 10, paidAmount: 0, reservedAmount: 0, outstandingAmount: 10 },
      { sessionId: 's', paymentPlans: [], paymentAttempts: [] },
      { sessionId: 's', status: 'open', version: 8, totalAmount: 10, paidAmount: 0, reservedAmount: 0, outstandingAmount: 10 },
      { sessionId: 's', paymentPlans: [], paymentAttempts: [] }
    ]
    let callIndex = 0
    const fetchMock = vi.fn().mockImplementation((url: string, init?: RequestInit) => {
      callIndex += 1
      const body = callIndex === 1 ? responses[0] : callIndex === 2 ? responses[1] : callIndex === 3 ? responses[2] : callIndex === 4 ? responses[4] : responses[5]
      return Promise.resolve(new Response(JSON.stringify(body), { status: 200 }))
    })
    vi.stubGlobal('fetch', fetchMock); const wrapper = mount(ClosingPaymentsView, { global: { stubs } }); await wrapper.find('input').setValue('s'); await wrapper.find('button').trigger('click'); await flushPromises(); const buttons = wrapper.findAll('button'); await buttons[1].trigger('click'); await flushPromises()
    const mutation = fetchMock.mock.calls.find((call: any[]) => call[1]?.method === 'POST'); expect(mutation).toBeTruthy(); if (!mutation) throw new Error('mutation missing'); expect(mutation[0]).toContain('/operations/sessions/s/closing/cancel'); expect(JSON.parse(mutation[1].body).expectedVersion).toBe(7); expect(mutation[1].headers['Idempotency-Key']).toBeTruthy(); expect(fetchMock.mock.calls.filter((call: any[]) => call[1]?.method === 'POST')).toHaveLength(1); expect(wrapper.text()).toContain('open')
  })
})
