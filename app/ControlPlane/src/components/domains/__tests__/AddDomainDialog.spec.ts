import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { ref } from 'vue'
import AddDomainDialog from '../AddDomainDialog.vue'

vi.mock('@/composables/useDomains', () => ({
  useAddDomain: vi.fn(),
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

const mockCreatedDomain = {
  id: 'domain-001',
  hostname: 'go.acme.com',
  status: 'pending_verification' as const,
  verificationInstructions: {
    txtName: '_acme-challenge',
    txtValue: 'verify-abc123',
    cnameName: 'go.acme.com',
    cnameValue: 'cloudfront.example.net',
  },
  createdAt: '2025-06-01T10:00:00Z',
}

function mountComponent(modelValue = true) {
  return mount(AddDomainDialog, {
    props: { modelValue },
    global: {
      plugins: [vuetify],
      stubs: { 'v-dialog': { template: '<div><slot /></div>' } },
    },
  })
}

describe('AddDomainDialog', () => {
  let mockMutateAsync: ReturnType<typeof vi.fn>

  beforeEach(async () => {
    vi.clearAllMocks()
    mockMutateAsync = vi.fn().mockResolvedValue(mockCreatedDomain)

    const { useAddDomain } = await import('@/composables/useDomains')
    vi.mocked(useAddDomain).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: ref(false),
      isError: ref(false),
      error: ref(null),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useAddDomain>)
  })

  it('renders the dialog title', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('Add domain')
  })

  it('renders the hostname input', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="hostname-input"]').exists()).toBe(true)
  })

  it('renders cancel and add buttons', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="add-cancel-btn"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="add-submit-btn"]').exists()).toBe(true)
  })

  it('disables the add button when hostname is empty', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="add-submit-btn"]').attributes('disabled')).toBeDefined()
  })

  it('enables the add button when hostname is filled', async () => {
    const wrapper = mountComponent()
    const input = wrapper.find('[data-testid="hostname-input"]').find('input')
    await input.setValue('go.acme.com')
    await input.trigger('blur')
    await wrapper.vm.$nextTick()

    expect(wrapper.find('[data-testid="add-submit-btn"]').attributes('disabled')).toBeUndefined()
  })

  describe('hostname validation', () => {
    it('shows required error for empty hostname', async () => {
      const wrapper = mountComponent()
      const input = wrapper.find('[data-testid="hostname-input"]').find('input')
      await input.setValue('')
      await input.trigger('blur')
      await wrapper.vm.$nextTick()

      expect(wrapper.text()).toContain('Hostname is required')
    })

    it('shows error when URL includes protocol', async () => {
      const wrapper = mountComponent()
      const input = wrapper.find('[data-testid="hostname-input"]').find('input')
      await input.setValue('https://go.acme.com')
      await input.trigger('blur')
      await wrapper.vm.$nextTick()

      expect(wrapper.text()).toContain('without https:// or http://')
    })

    it('shows error when hostname includes a path', async () => {
      const wrapper = mountComponent()
      const input = wrapper.find('[data-testid="hostname-input"]').find('input')
      await input.setValue('go.acme.com/path')
      await input.trigger('blur')
      await wrapper.vm.$nextTick()

      expect(wrapper.text()).toContain('Hostname must not include a path')
    })

    it('shows error for invalid hostname format', async () => {
      const wrapper = mountComponent()
      const input = wrapper.find('[data-testid="hostname-input"]').find('input')
      await input.setValue('not valid')
      await input.trigger('blur')
      await wrapper.vm.$nextTick()

      expect(wrapper.text()).toContain('Enter a valid hostname')
    })

    it('accepts a valid hostname', async () => {
      const wrapper = mountComponent()
      const input = wrapper.find('[data-testid="hostname-input"]').find('input')
      await input.setValue('go.acme.com')
      await input.trigger('blur')
      await wrapper.vm.$nextTick()

      expect(wrapper.text()).not.toContain('Hostname is required')
      expect(wrapper.text()).not.toContain('without https://')
      expect(wrapper.text()).not.toContain('Hostname must not include a path')
      expect(wrapper.text()).not.toContain('Enter a valid hostname')
    })
  })

  it('calls mutateAsync with trimmed lowercase hostname on submit', async () => {
    const wrapper = mountComponent()
    const input = wrapper.find('[data-testid="hostname-input"]').find('input')
    await input.setValue('  Go.ACME.com  ')
    await input.trigger('blur')
    await wrapper.vm.$nextTick()

    await wrapper.find('[data-testid="add-submit-btn"]').trigger('click')
    await wrapper.vm.$nextTick()

    expect(mockMutateAsync).toHaveBeenCalledWith({ hostname: 'go.acme.com' })
  })

  it('emits created with the new domain on success', async () => {
    const wrapper = mountComponent()
    const input = wrapper.find('[data-testid="hostname-input"]').find('input')
    await input.setValue('go.acme.com')
    await wrapper.vm.$nextTick()

    await wrapper.find('[data-testid="add-submit-btn"]').trigger('click')
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('created')).toBeTruthy()
    expect(wrapper.emitted('created')![0]).toEqual([mockCreatedDomain])
  })

  it('closes on success', async () => {
    const wrapper = mountComponent()
    const input = wrapper.find('[data-testid="hostname-input"]').find('input')
    await input.setValue('go.acme.com')
    await wrapper.vm.$nextTick()

    await wrapper.find('[data-testid="add-submit-btn"]').trigger('click')
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('update:modelValue')![0]).toEqual([false])
  })

  it('closes without adding when cancel is clicked', async () => {
    const wrapper = mountComponent()
    await wrapper.find('[data-testid="add-cancel-btn"]').trigger('click')
    expect(mockMutateAsync).not.toHaveBeenCalled()
    expect(wrapper.emitted('update:modelValue')![0]).toEqual([false])
  })

  it('shows error alert when add fails', async () => {
    const { useAddDomain } = await import('@/composables/useDomains')
    vi.mocked(useAddDomain).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: ref(false),
      isError: ref(true),
      error: ref(new Error('Add failed')),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useAddDomain>)

    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="add-error"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="add-error"]').text()).toContain('Failed to add domain')
  })

  it('does not call mutateAsync when validation fails', async () => {
    const wrapper = mountComponent()
    const input = wrapper.find('[data-testid="hostname-input"]').find('input')
    await input.setValue('invalid hostname with spaces')
    await input.trigger('blur')
    await wrapper.vm.$nextTick()

    await wrapper.find('[data-testid="add-submit-btn"]').trigger('click')

    expect(mockMutateAsync).not.toHaveBeenCalled()
  })
})
