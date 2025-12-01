import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { ref } from 'vue'
import DeleteDomainDialog from '../DeleteDomainDialog.vue'

vi.mock('@/composables/useDomains', () => ({
  useDeleteDomain: vi.fn(),
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

const mockDomain = {
  id: 'domain-001',
  hostname: 'go.acme.com',
  status: 'active' as const,
  certificateStatus: 'issued' as const,
  linkCount: 42,
  createdAt: '2025-03-01T10:00:00Z',
}

function mountComponent(modelValue = true) {
  return mount(DeleteDomainDialog, {
    props: { modelValue, domain: mockDomain },
    global: {
      plugins: [vuetify],
      stubs: { 'v-dialog': { template: '<div><slot /></div>' } },
    },
  })
}

describe('DeleteDomainDialog', () => {
  let mockMutateAsync: ReturnType<typeof vi.fn>

  beforeEach(async () => {
    vi.clearAllMocks()
    mockMutateAsync = vi.fn().mockResolvedValue(undefined)

    const { useDeleteDomain } = await import('@/composables/useDomains')
    vi.mocked(useDeleteDomain).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: ref(false),
      isError: ref(false),
      error: ref(null),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useDeleteDomain>)
  })

  it('renders the dialog title', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('Delete domain')
  })

  it('shows the hostname in the confirmation message', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="delete-domain-hostname"]').text()).toBe('go.acme.com')
  })

  it('warns that all links will be disabled', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('All links on this domain will be disabled')
  })

  it('warns the action cannot be undone', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('cannot be undone')
  })

  it('renders cancel and delete buttons', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="delete-cancel-btn"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="delete-confirm-btn"]').exists()).toBe(true)
  })

  it('calls mutateAsync with the domain id on delete', async () => {
    const wrapper = mountComponent()
    await wrapper.find('[data-testid="delete-confirm-btn"]').trigger('click')
    expect(mockMutateAsync).toHaveBeenCalledWith('domain-001')
  })

  it('emits deleted and closes on success', async () => {
    const wrapper = mountComponent()
    await wrapper.find('[data-testid="delete-confirm-btn"]').trigger('click')
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('deleted')).toBeTruthy()
    expect(wrapper.emitted('update:modelValue')![0]).toEqual([false])
  })

  it('closes without deleting when cancel is clicked', async () => {
    const wrapper = mountComponent()
    await wrapper.find('[data-testid="delete-cancel-btn"]').trigger('click')
    expect(mockMutateAsync).not.toHaveBeenCalled()
    expect(wrapper.emitted('update:modelValue')![0]).toEqual([false])
  })

  it('disables cancel button while pending', async () => {
    const { useDeleteDomain } = await import('@/composables/useDomains')
    vi.mocked(useDeleteDomain).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: ref(true),
      isError: ref(false),
      error: ref(null),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useDeleteDomain>)

    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="delete-cancel-btn"]').attributes('disabled')).toBeDefined()
  })

  it('shows error alert when deletion fails', async () => {
    const { useDeleteDomain } = await import('@/composables/useDomains')
    vi.mocked(useDeleteDomain).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: ref(false),
      isError: ref(true),
      error: ref(new Error('Delete failed')),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useDeleteDomain>)

    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="delete-error"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="delete-error"]').text()).toContain('Failed to delete domain')
  })
})
