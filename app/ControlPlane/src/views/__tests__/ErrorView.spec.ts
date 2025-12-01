import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createRouter, createWebHistory } from 'vue-router'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import ErrorView from '../ErrorView.vue'

const vuetify = createVuetify({ components, directives })

function createTestRouter() {
  return createRouter({
    history: createWebHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/dashboard', name: 'dashboard', component: { template: '<div />' } },
      { path: '/error', name: 'error', component: { template: '<div />' } },
    ],
  })
}

function mountComponent(props: { code?: number; message?: string } = {}) {
  const router = createTestRouter()
  return {
    wrapper: mount(ErrorView, {
      global: { plugins: [vuetify, router] },
      props: { code: 500, ...props },
    }),
    router,
  }
}

describe('ErrorView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('renders the error code', () => {
    it('renders 500', () => {
      const { wrapper } = mountComponent({ code: 500 })
      expect(wrapper.text()).toContain('500')
    })

    it('renders 403', () => {
      const { wrapper } = mountComponent({ code: 403 })
      expect(wrapper.text()).toContain('403')
    })

    it('renders 503', () => {
      const { wrapper } = mountComponent({ code: 503 })
      expect(wrapper.text()).toContain('503')
    })
  })

  describe('renders the correct title per status code', () => {
    it('shows Internal Server Error for 500', () => {
      const { wrapper } = mountComponent({ code: 500 })
      expect(wrapper.text()).toContain('Internal Server Error')
    })

    it('shows Access Forbidden for 403', () => {
      const { wrapper } = mountComponent({ code: 403 })
      expect(wrapper.text()).toContain('Access Forbidden')
    })

    it('shows Unauthorized for 401', () => {
      const { wrapper } = mountComponent({ code: 401 })
      expect(wrapper.text()).toContain('Unauthorized')
    })

    it('shows Bad Request for 400', () => {
      const { wrapper } = mountComponent({ code: 400 })
      expect(wrapper.text()).toContain('Bad Request')
    })

    it('shows Bad Gateway for 502', () => {
      const { wrapper } = mountComponent({ code: 502 })
      expect(wrapper.text()).toContain('Bad Gateway')
    })

    it('shows Service Unavailable for 503', () => {
      const { wrapper } = mountComponent({ code: 503 })
      expect(wrapper.text()).toContain('Service Unavailable')
    })

    it('shows Request Timeout for 408', () => {
      const { wrapper } = mountComponent({ code: 408 })
      expect(wrapper.text()).toContain('Request Timeout')
    })

    it('shows An Error Occurred for an unknown code', () => {
      const { wrapper } = mountComponent({ code: 418 })
      expect(wrapper.text()).toContain('An Error Occurred')
    })
  })

  it('uses a custom message as the title when provided', () => {
    const { wrapper } = mountComponent({ code: 500, message: 'Custom error message' })
    expect(wrapper.text()).toContain('Custom error message')
  })

  describe('renders the correct description per status code', () => {
    it('describes a 500 error', () => {
      const { wrapper } = mountComponent({ code: 500 })
      expect(wrapper.text()).toContain('Something went wrong on our end')
    })

    it('describes a 403 error', () => {
      const { wrapper } = mountComponent({ code: 403 })
      expect(wrapper.text()).toContain("don't have permission")
    })

    it('describes a 401 error', () => {
      const { wrapper } = mountComponent({ code: 401 })
      expect(wrapper.text()).toContain('sign in')
    })
  })

  it('renders a Go to Dashboard button', () => {
    const { wrapper } = mountComponent()
    expect(wrapper.text()).toContain('Go to Dashboard')
  })

  it('renders a Go Back button', () => {
    const { wrapper } = mountComponent()
    expect(wrapper.text()).toContain('Go Back')
  })

  it('calls router.back() when Go Back is clicked', async () => {
    const { wrapper, router } = mountComponent()
    const backSpy = vi.spyOn(router, 'back')

    const buttons = wrapper.findAll('button')
    const goBackButton = buttons.find((b) => b.text().includes('Go Back'))
    expect(goBackButton).toBeDefined()
    await goBackButton!.trigger('click')

    expect(backSpy).toHaveBeenCalled()
  })
})
