import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import HomeView from '../HomeView.vue'

const vuetify = createVuetify({ components, directives })

vi.stubGlobal(
  'ResizeObserver',
  class {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
)

describe('HomeView', () => {
  it('renders the app title', () => {
    const wrapper = mount(HomeView, {
      global: { plugins: [vuetify] },
    })
    expect(wrapper.text()).toContain('Control Plane')
  })

  it('renders the subtitle description', () => {
    const wrapper = mount(HomeView, {
      global: { plugins: [vuetify] },
    })
    expect(wrapper.text()).toContain('URL Shortener Management')
  })
})
