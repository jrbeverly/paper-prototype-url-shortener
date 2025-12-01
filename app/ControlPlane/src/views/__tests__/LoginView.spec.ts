import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createRouter, createWebHistory } from 'vue-router'
import { setActivePinia, createPinia } from 'pinia'

vi.mock('@/services/authService', () => ({
  login: vi.fn(),
}))

vi.mock('vue-router', async () => {
  const actual = await vi.importActual('vue-router')
  return {
    ...actual,
    useRoute: vi.fn(),
  }
})

const vuetify = createVuetify({ components, directives })

import LoginView from '../LoginView.vue'
import * as authService from '@/services/authService'
import { useRoute } from 'vue-router'

vi.stubGlobal(
  'ResizeObserver',
  class {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
)

const loginMock = vi.mocked(authService.login)

function mountComponent(routeQuery: Record<string, unknown> = {}) {
  vi.mocked(useRoute).mockReturnValue({ query: routeQuery } as ReturnType<typeof useRoute>)

  const router = createRouter({
    history: createWebHistory(),
    routes: [
      { path: '/login', name: 'login', component: LoginView },
      { path: '/register', name: 'register', component: { template: '<div>Register</div>' } },
      { path: '/dashboard', name: 'dashboard', component: { template: '<div>Dashboard</div>' } },
    ],
  })

  return mount(LoginView, {
    global: {
      plugins: [vuetify, router],
    },
  })
}

async function setFieldValue(
  wrapper: ReturnType<typeof mountComponent>,
  testId: string,
  value: string
) {
  const field = wrapper.find(`[data-testid="${testId}"]`)
  const input = field.find('input')
  await input.setValue(value)
  await wrapper.vm.$nextTick()
}

async function triggerSubmit(wrapper: ReturnType<typeof mountComponent>) {
  await wrapper.find('form').trigger('submit')
  await flushPromises()
  await wrapper.vm.$nextTick()
}

async function fillAndSubmit(
  wrapper: ReturnType<typeof mountComponent>,
  email: string,
  password: string
) {
  await setFieldValue(wrapper, 'email-input', email)
  await setFieldValue(wrapper, 'password-input', password)
  await triggerSubmit(wrapper)
}

describe('LoginView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
    loginMock.mockResolvedValue(undefined)
  })

  describe('form rendering', () => {
    it('renders email and password inputs', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="email-input"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="password-input"]').exists()).toBe(true)
    })

    it('renders the sign in button', () => {
      const wrapper = mountComponent()
      const btn = wrapper.find('[data-testid="login-submit-btn"]')
      expect(btn.exists()).toBe(true)
      expect(btn.text()).toBe('Sign in')
    })

    it('renders the remember me checkbox', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="remember-me-checkbox"]').exists()).toBe(true)
    })

    it('renders the forgot password link', () => {
      const wrapper = mountComponent()
      const link = wrapper.find('[data-testid="forgot-password-link"]')
      expect(link.exists()).toBe(true)
      expect(link.text()).toBe('Forgot password?')
    })

    it('renders a link to the registration page', () => {
      const wrapper = mountComponent()
      const link = wrapper.find('[data-testid="register-link"]')
      expect(link.exists()).toBe(true)
      expect(link.text()).toContain('Create account')
    })
  })

  describe('form validation', () => {
    it('shows email required error when email is empty', async () => {
      const wrapper = mountComponent()
      await triggerSubmit(wrapper)

      const messages = wrapper.find('[data-testid="email-input"] .v-messages__message')
      expect(messages.exists()).toBe(true)
      expect(messages.text()).toBe('Email is required')
    })

    it('shows email format error for invalid email', async () => {
      const wrapper = mountComponent()
      await setFieldValue(wrapper, 'email-input', 'not-an-email')
      await triggerSubmit(wrapper)

      const messages = wrapper.find('[data-testid="email-input"] .v-messages__message')
      expect(messages.exists()).toBe(true)
      expect(messages.text()).toBe('Email must be valid')
    })

    it('shows password required error when password is empty', async () => {
      const wrapper = mountComponent()
      await setFieldValue(wrapper, 'email-input', 'test@example.com')
      await triggerSubmit(wrapper)

      const messages = wrapper.find('[data-testid="password-input"] .v-messages__message')
      expect(messages.exists()).toBe(true)
      expect(messages.text()).toBe('Password is required')
    })

    it('does not call login when validation fails', async () => {
      const wrapper = mountComponent()
      await triggerSubmit(wrapper)

      expect(loginMock).not.toHaveBeenCalled()
    })
  })

  describe('successful login', () => {
    it('calls login with email and password', async () => {
      const wrapper = mountComponent()
      await fillAndSubmit(wrapper, 'test@example.com', 'password123')

      expect(loginMock).toHaveBeenCalledWith('test@example.com', 'password123', expect.anything())
    })

    it('redirects to dashboard by default', async () => {
      const wrapper = mountComponent()
      await fillAndSubmit(wrapper, 'test@example.com', 'password123')

      expect(loginMock).toHaveBeenCalledWith('test@example.com', 'password123', {
        name: 'dashboard',
      })
    })

    it('redirects to the original destination from query param', async () => {
      const wrapper = mountComponent({ redirect: '/links/create' })
      await fillAndSubmit(wrapper, 'test@example.com', 'pass')

      expect(loginMock).toHaveBeenCalledWith('test@example.com', 'pass', '/links/create')
    })

    it('ignores non-string redirect query params', async () => {
      const wrapper = mountComponent({ redirect: ['/evil'] })
      await fillAndSubmit(wrapper, 'a@b.com', 'pass')

      expect(loginMock).toHaveBeenCalledWith('a@b.com', 'pass', { name: 'dashboard' })
    })
  })

  describe('login errors', () => {
    it('shows invalid credentials message', async () => {
      loginMock.mockRejectedValue(new Error('Invalid credentials'))
      const wrapper = mountComponent()
      await fillAndSubmit(wrapper, 'bad@example.com', 'wrong')

      const alert = wrapper.find('[data-testid="login-error"]')
      expect(alert.exists()).toBe(true)
      expect(alert.text()).toBe('The email address or password you entered is incorrect.')
    })

    it('shows account suspended message', async () => {
      loginMock.mockRejectedValue(new Error('Account suspended'))
      const wrapper = mountComponent()
      await fillAndSubmit(wrapper, 'a@b.com', 'pass')

      expect(wrapper.find('[data-testid="login-error"]').text()).toBe(
        'Your account has been suspended.'
      )
    })

    it('shows network error message for fetch failure', async () => {
      loginMock.mockRejectedValue(new TypeError('Failed to fetch'))
      const wrapper = mountComponent()
      await fillAndSubmit(wrapper, 'a@b.com', 'pass')

      expect(wrapper.find('[data-testid="login-error"]').text()).toContain('network error')
    })

    it('shows generic message for unknown errors', async () => {
      loginMock.mockRejectedValue('unknown failure')
      const wrapper = mountComponent()
      await fillAndSubmit(wrapper, 'a@b.com', 'pass')

      expect(wrapper.find('[data-testid="login-error"]').text()).toBe(
        'Login failed. Please try again.'
      )
    })

    it('clears previous error on re-submit', async () => {
      loginMock.mockRejectedValue(new Error('Invalid credentials'))
      const wrapper = mountComponent()
      await fillAndSubmit(wrapper, 'a@b.com', 'pass')

      expect(wrapper.find('[data-testid="login-error"]').exists()).toBe(true)

      loginMock.mockReset()
      loginMock.mockResolvedValue(undefined)
      await fillAndSubmit(wrapper, 'test@example.com', 'pass')

      expect(wrapper.find('[data-testid="login-error"]').exists()).toBe(false)
    })
  })

  describe('responsive layout', () => {
    it('renders inside a centered v-container', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('.v-container.fill-height').exists()).toBe(true)
    })

    it('renders the form inside a v-card', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('.v-card').exists()).toBe(true)
    })

    it('uses responsive column widths', () => {
      const wrapper = mountComponent()
      const col = wrapper.find('.v-col-sm-8.v-col-md-4')
      expect(col.exists()).toBe(true)
    })
  })
})
