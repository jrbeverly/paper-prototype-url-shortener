import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import KeyRevealDialog from '../KeyRevealDialog.vue'

const vuetify = createVuetify({ components, directives })

vi.stubGlobal(
  'ResizeObserver',
  class {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
)

const mockCreatedKey = {
  id: 'key-001',
  name: 'Production Key',
  key: 'sk_abc123def456ghi789jkl012mno345pqr678stu',
  keyPrefix: 'sk_abc123',
  createdAt: '2025-06-01T10:00:00Z',
  permissions: ['viewer'],
}

function mountComponent(modelValue = true) {
  return mount(KeyRevealDialog, {
    props: { modelValue, createdKey: mockCreatedKey },
    global: {
      plugins: [vuetify],
      stubs: { 'v-dialog': { template: '<div><slot /></div>' } },
    },
  })
}

describe('KeyRevealDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    Object.defineProperty(navigator, 'clipboard', {
      value: { writeText: vi.fn().mockResolvedValue(undefined) },
      writable: true,
    })
  })

  it('renders the dialog title', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('API key created')
  })

  it('displays the key name', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('Production Key')
  })

  it('displays the full key value', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="key-value"]').text()).toBe(mockCreatedKey.key)
  })

  it('shows a warning to save the key', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="reveal-warning"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="reveal-warning"]').text()).toContain('Save this key now')
  })

  it('renders the copy key button', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="copy-key-btn"]').exists()).toBe(true)
  })

  it('renders the done button', () => {
    const wrapper = mountComponent()
    const btn = wrapper.find('[data-testid="reveal-done-btn"]')
    expect(btn.exists()).toBe(true)
    expect(btn.text()).toContain("I've saved my key")
  })

  it('closes when done button is clicked', async () => {
    const wrapper = mountComponent()
    await wrapper.find('[data-testid="reveal-done-btn"]').trigger('click')
    expect(wrapper.emitted('update:modelValue')![0]).toEqual([false])
  })

  it('copies key to clipboard on copy button click', async () => {
    const wrapper = mountComponent()
    await wrapper.find('[data-testid="copy-key-btn"]').trigger('click')
    expect(navigator.clipboard.writeText).toHaveBeenCalledWith(mockCreatedKey.key)
  })

  it('renders the key value container', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="key-value-container"]').exists()).toBe(true)
  })
})
