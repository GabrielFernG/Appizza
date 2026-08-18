import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import KitchenQueueView from './KitchenQueueView.vue'

describe('KitchenQueueView', () => {
  it('presents the Operations Delivery queue shell', () => {
    vi.stubGlobal('fetch', vi.fn())
    const wrapper = mount(KitchenQueueView, { global: { stubs: { 'v-container': { template: '<div><slot /></div>' }, 'v-text-field': true, 'v-btn': { template: '<button><slot /></button>' }, 'v-select': true, 'v-alert': true, 'v-list': true, 'v-list-item': true, 'v-card': true, 'v-card-text': true } } })
    expect(wrapper.text()).toContain('Fila da cozinha')
    expect(wrapper.text()).toContain('O realtime invalida; a API continua sendo a fonte de verdade.')
  })
})
