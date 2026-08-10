import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import App from './App.vue'

describe('App', () => {
  it('renders the router outlet', () => {
    const wrapper = mount(App, {
      global: {
        stubs: {
          'v-app': { template: '<div><slot /></div>' },
          'v-main': { template: '<main><slot /></main>' },
          RouterView: { template: '<div data-testid="router-view" />' },
        },
      },
    })

    expect(wrapper.find('[data-testid="router-view"]').exists()).toBe(true)
  })
})
