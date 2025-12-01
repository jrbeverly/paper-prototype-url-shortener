import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { createRouter, createWebHistory } from 'vue-router'
import RegisterView from '../RegisterView.vue'

vi.mock('@/services/authService', () => ({
  register: vi.fn(),
}))

const vuetify = createVuetify({ components, directives })

vi.stubGlobal(
  'ResizeObserver',
  class {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
)

import * as authService from '@/services/authService'
const registerMock = vi.mocked(authService.register)

function makeRouter() {
  return createRouter({
    history: createWebHistory(),
    routes: [
      { path: '/login', name: 'login', component: { template: '<div>Login</div>' } },
      { path: '/register', name: 'register', component: RegisterView },
      { path: '/dashboard', name: 'dashboard', component: { template: '<div>Dashboard</div>' } },
    ],
  })
}

function mountComponent() {
  return mount(RegisterView, {
    global: {
      plugins: [vuetify, makeRouter()],
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

async function fillValidForm(wrapper: ReturnType<typeof mountComponent>) {
  await setFieldValue(wrapper, 'register-name-input', 'Test User')
  await setFieldValue(wrapper, 'register-email-input', 'test@example.com')
  await setFieldValue(wrapper, 'register-password-input', 'Password1!')
  await setFieldValue(wrapper, 'register-confirm-password-input', 'Password1!')
  const checkbox = wrapper.find('[data-testid="register-terms-checkbox"] input[type="checkbox"]')
  await checkbox.setValue(true)
  await wrapper.vm.$nextTick()
}

describe('RegisterView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    registerMock.mockResolvedValue(undefined)
  })

  describe('form rendering', () => {
    it('renders the "Create account" title', () => {
      const wrapper = mountComponent()
      expect(wrapper.text()).toContain('Create account')
    })

    it('renders name, email, password, and confirm password inputs', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="register-name-input"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="register-email-input"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="register-password-input"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="register-confirm-password-input"]').exists()).toBe(true)
    })

    it('renders the terms of service checkbox', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="register-terms-checkbox"]').exists()).toBe(true)
    })

    it('renders the create account button', () => {
      const wrapper = mountComponent()
      const btn = wrapper.find('[data-testid="register-submit-btn"]')
      expect(btn.exists()).toBe(true)
      expect(btn.text()).toBe('Create account')
    })

    it('renders a link to the sign in page', () => {
      const wrapper = mountComponent()
      const link = wrapper.find('[data-testid="login-link"]')
      expect(link.exists()).toBe(true)
      expect(link.text()).toContain('Sign in')
    })

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
      expect(wrapper.find('.v-col-sm-8.v-col-md-4').exists()).toBe(true)
    })
  })

  describe('form validation', () => {
    it('shows name required error on empty submit', async () => {
      const wrapper = mountComponent()
      await triggerSubmit(wrapper)
      const msg = wrapper.find('[data-testid="register-name-input"] .v-messages__message')
      expect(msg.exists()).toBe(true)
      expect(msg.text()).toBe('Name is required')
    })

    it('shows email required error on empty submit', async () => {
      const wrapper = mountComponent()
      await triggerSubmit(wrapper)
      const msg = wrapper.find('[data-testid="register-email-input"] .v-messages__message')
      expect(msg.exists()).toBe(true)
      expect(msg.text()).toBe('Email is required')
    })

    it('shows invalid email format error', async () => {
      const wrapper = mountComponent()
      await setFieldValue(wrapper, 'register-email-input', 'not-an-email')
      await triggerSubmit(wrapper)
      const msg = wrapper.find('[data-testid="register-email-input"] .v-messages__message')
      expect(msg.exists()).toBe(true)
      expect(msg.text()).toBe('Email must be valid')
    })

    it('shows password required error on empty submit', async () => {
      const wrapper = mountComponent()
      await triggerSubmit(wrapper)
      const msg = wrapper.find('[data-testid="register-password-input"] .v-messages__message')
      expect(msg.exists()).toBe(true)
      expect(msg.text()).toBe('Password is required')
    })

    it('shows minimum password length error', async () => {
      const wrapper = mountComponent()
      await setFieldValue(wrapper, 'register-password-input', 'short')
      await triggerSubmit(wrapper)
      const msg = wrapper.find('[data-testid="register-password-input"] .v-messages__message')
      expect(msg.exists()).toBe(true)
      expect(msg.text()).toBe('Password must be at least 8 characters')
    })

    it('shows confirm password required error on empty submit', async () => {
      const wrapper = mountComponent()
      await triggerSubmit(wrapper)
      const msg = wrapper.find(
        '[data-testid="register-confirm-password-input"] .v-messages__message'
      )
      expect(msg.exists()).toBe(true)
      expect(msg.text()).toBe('Please confirm your password')
    })

    it('shows password mismatch error', async () => {
      const wrapper = mountComponent()
      await setFieldValue(wrapper, 'register-password-input', 'Password1!')
      await setFieldValue(wrapper, 'register-confirm-password-input', 'Different1!')
      await triggerSubmit(wrapper)
      const msg = wrapper.find(
        '[data-testid="register-confirm-password-input"] .v-messages__message'
      )
      expect(msg.exists()).toBe(true)
      expect(msg.text()).toBe('Passwords do not match')
    })

    it('does not call register() when validation fails', async () => {
      const wrapper = mountComponent()
      await triggerSubmit(wrapper)
      expect(registerMock).not.toHaveBeenCalled()
    })
  })

  describe('password strength indicator', () => {
    it('does not show strength bar when password is empty', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="password-strength-bar"]').exists()).toBe(false)
    })

    it('shows strength bar when password has content', async () => {
      const wrapper = mountComponent()
      await setFieldValue(wrapper, 'register-password-input', 'abc')
      expect(wrapper.find('[data-testid="password-strength-bar"]').exists()).toBe(true)
    })

    it('shows "Weak" label for a weak password', async () => {
      const wrapper = mountComponent()
      await setFieldValue(wrapper, 'register-password-input', 'abcdefgh')
      expect(wrapper.find('[data-testid="password-strength-label"]').text()).toBe('Weak')
    })

    it('shows "Fair" label for a moderately strong password', async () => {
      const wrapper = mountComponent()
      await setFieldValue(wrapper, 'register-password-input', 'Abcdefgh')
      expect(wrapper.find('[data-testid="password-strength-label"]').text()).toBe('Fair')
    })

    it('shows "Strong" label for a strong password', async () => {
      const wrapper = mountComponent()
      await setFieldValue(wrapper, 'register-password-input', 'Abc123!@#')
      expect(wrapper.find('[data-testid="password-strength-label"]').text()).toBe('Strong')
    })
  })

  describe('successful registration', () => {
    it('calls register() with name, email, and password on valid submission', async () => {
      const wrapper = mountComponent()
      await fillValidForm(wrapper)
      await triggerSubmit(wrapper)
      expect(registerMock).toHaveBeenCalledWith('Test User', 'test@example.com', 'Password1!')
    })
  })

  describe('error handling', () => {
    it('shows user-friendly message for duplicate email', async () => {
      registerMock.mockRejectedValue(new Error('Email already exists'))
      const wrapper = mountComponent()
      await fillValidForm(wrapper)
      await triggerSubmit(wrapper)
      const alert = wrapper.find('[data-testid="register-error"]')
      expect(alert.exists()).toBe(true)
      expect(alert.text()).toContain('already exists')
    })

    it('shows network error message for fetch failure', async () => {
      registerMock.mockRejectedValue(new TypeError('Failed to fetch'))
      const wrapper = mountComponent()
      await fillValidForm(wrapper)
      await triggerSubmit(wrapper)
      expect(wrapper.find('[data-testid="register-error"]').text()).toContain('network error')
    })

    it('shows generic message for unknown errors', async () => {
      registerMock.mockRejectedValue('unknown failure')
      const wrapper = mountComponent()
      await fillValidForm(wrapper)
      await triggerSubmit(wrapper)
      expect(wrapper.find('[data-testid="register-error"]').text()).toContain('Registration failed')
    })

    it('clears previous error on re-submit', async () => {
      registerMock.mockRejectedValue(new Error('Email already exists'))
      const wrapper = mountComponent()
      await fillValidForm(wrapper)
      await triggerSubmit(wrapper)
      expect(wrapper.find('[data-testid="register-error"]').exists()).toBe(true)

      registerMock.mockResolvedValue(undefined)
      await triggerSubmit(wrapper)
      expect(wrapper.find('[data-testid="register-error"]').exists()).toBe(false)
    })
  })
})
