import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { ref } from 'vue'
import DeleteLinkDialog from '../DeleteLinkDialog.vue'

vi.mock('@/composables/useLinks', () => ({
  useDeleteLink: vi.fn(),
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

const mockLink = {
  id: 'link-001',
  domainId: 'domain-001',
  domainHostname: 'go.acme.com',
  slug: 'launch',
  destinationUrl: 'https://acme.com/product-launch',
  shortUrl: 'https://go.acme.com/launch',
  redirectType: '302' as const,
  status: 'active' as const,
  clickCount: 1284,
  maxClicks: null,
  expiresAt: null,
  createdAt: '2025-01-10T09:00:00Z',
  updatedAt: '2025-01-10T09:00:00Z',
  version: 1,
  expiryWarning: null,
}

function mountComponent(modelValue = true) {
  return mount(DeleteLinkDialog, {
    props: { modelValue, link: mockLink },
    global: {
      plugins: [vuetify],
      stubs: { 'v-dialog': { template: '<div><slot /></div>' } },
    },
  })
}

describe('DeleteLinkDialog', () => {
  let mockMutateAsync: ReturnType<typeof vi.fn>

  beforeEach(async () => {
    vi.clearAllMocks()
    mockMutateAsync = vi.fn().mockResolvedValue(undefined)

    const { useDeleteLink } = await import('@/composables/useLinks')
    vi.mocked(useDeleteLink).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: ref(false),
      isError: ref(false),
      error: ref(null),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useDeleteLink>)
  })

  it('renders the dialog title', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('Delete link')
  })

  it('shows the short URL in the confirmation message', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('https://go.acme.com/launch')
  })

  it('warns that redirects will stop immediately', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('immediately stop redirects')
  })

  it('renders cancel and delete buttons', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="delete-cancel-btn"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="delete-confirm-btn"]').exists()).toBe(true)
  })

  it('calls mutateAsync with the link id on confirm', async () => {
    const wrapper = mountComponent()
    await wrapper.find('[data-testid="delete-confirm-btn"]').trigger('click')
    expect(mockMutateAsync).toHaveBeenCalledWith('link-001')
  })

  it('emits deleted and closes on successful delete', async () => {
    const wrapper = mountComponent()
    await wrapper.find('[data-testid="delete-confirm-btn"]').trigger('click')
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('deleted')).toBeTruthy()
    expect(wrapper.emitted('update:modelValue')).toBeTruthy()
    expect(wrapper.emitted('update:modelValue')![0]).toEqual([false])
  })

  it('closes without deleting when cancel is clicked', async () => {
    const wrapper = mountComponent()
    await wrapper.find('[data-testid="delete-cancel-btn"]').trigger('click')
    expect(mockMutateAsync).not.toHaveBeenCalled()
    expect(wrapper.emitted('update:modelValue')![0]).toEqual([false])
  })

  it('emits deleted even when parent does not close the dialog', async () => {
    const wrapper = mountComponent()
    await wrapper.find('[data-testid="delete-confirm-btn"]').trigger('click')
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('deleted')).toBeTruthy()
    expect(wrapper.emitted('deleted')).toHaveLength(1)
  })

  it('disables cancel button while pending', async () => {
    const { useDeleteLink } = await import('@/composables/useLinks')
    vi.mocked(useDeleteLink).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: ref(true),
      isError: ref(false),
      error: ref(null),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useDeleteLink>)

    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="delete-cancel-btn"]').attributes('disabled')).toBeDefined()
  })

  it('shows an error alert when deletion fails', async () => {
    const { useDeleteLink } = await import('@/composables/useLinks')
    vi.mocked(useDeleteLink).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: ref(false),
      isError: ref(true),
      error: ref(new Error('Delete failed')),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useDeleteLink>)

    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="delete-error"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="delete-error"]').text()).toContain('Failed to delete link')
  })
})
