import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import AnalyticsView from '../AnalyticsView.vue'

const vuetify = createVuetify({ components, directives })

vi.stubGlobal(
  'ResizeObserver',
  class {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
)

describe('AnalyticsView', () => {
  it('renders the page title', () => {
    const wrapper = mount(AnalyticsView, {
      global: { plugins: [vuetify] },
    })
    expect(wrapper.text()).toContain('Analytics')
  })

  it('renders the subtitle', () => {
    const wrapper = mount(AnalyticsView, {
      global: { plugins: [vuetify] },
    })
    expect(wrapper.text()).toContain('Click analytics and performance metrics')
  })
})
