import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import DashboardView from '../DashboardView.vue'

const vuetify = createVuetify({ components, directives })

vi.stubGlobal(
  'ResizeObserver',
  class {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
)

describe('DashboardView', () => {
  it('renders the page title', () => {
    const wrapper = mount(DashboardView, {
      global: { plugins: [vuetify] },
    })
    expect(wrapper.text()).toContain('Dashboard')
  })

  it('renders the welcome message', () => {
    const wrapper = mount(DashboardView, {
      global: { plugins: [vuetify] },
    })
    expect(wrapper.text()).toContain('Welcome to Short.io')
  })
})
