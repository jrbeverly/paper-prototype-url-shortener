import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { ref } from 'vue'
import EditLinkDialog from '../EditLinkDialog.vue'

vi.mock('@/composables/useLinks', () => ({
  useUpdateLink: vi.fn(),
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
  return mount(EditLinkDialog, {
    props: { modelValue, link: mockLink },
    global: {
      plugins: [vuetify],
      stubs: { 'v-dialog': { template: '<div><slot /></div>' } },
    },
  })
}

describe('EditLinkDialog', () => {
  let mockMutateAsync: ReturnType<typeof vi.fn>

  beforeEach(async () => {
    vi.clearAllMocks()
    mockMutateAsync = vi.fn().mockResolvedValue(mockLink)

    const { useUpdateLink } = await import('@/composables/useLinks')
    vi.mocked(useUpdateLink).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: ref(false),
      isError: ref(false),
      error: ref(null),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useUpdateLink>)
  })

  it('renders the dialog title', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('Edit link')
  })

  it('shows the short URL as subtitle', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('https://go.acme.com/launch')
  })

  it('pre-fills the destination URL from the prop', () => {
    const wrapper = mountComponent()
    const input = wrapper.find('[data-testid="edit-destination-input"]').find('input')
    expect((input.element as HTMLInputElement).value).toBe('https://acme.com/product-launch')
  })

  it('renders the redirect type select', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="edit-redirect-type-select"]').exists()).toBe(true)
  })

  it('renders the status select', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="edit-status-select"]').exists()).toBe(true)
  })

  it('renders cancel and save buttons', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="edit-cancel-btn"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="edit-save-btn"]').exists()).toBe(true)
  })

  it('disables save button when destination is empty', async () => {
    const wrapper = mountComponent()
    const input = wrapper.find('[data-testid="edit-destination-input"]').find('input')
    await input.setValue('')
    await wrapper.vm.$nextTick()

    expect(wrapper.find('[data-testid="edit-save-btn"]').attributes('disabled')).toBeDefined()
  })

  it('shows destination error on empty URL', async () => {
    const wrapper = mountComponent()
    const input = wrapper.find('[data-testid="edit-destination-input"]').find('input')
    await input.setValue('')
    await input.trigger('blur')
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('Destination URL is required')
  })

  it('shows destination error for invalid URL', async () => {
    const wrapper = mountComponent()
    const input = wrapper.find('[data-testid="edit-destination-input"]').find('input')
    await input.setValue('not-a-valid-url')
    await input.trigger('blur')
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('Enter a valid URL')
  })

  it('shows destination error for non-http protocol', async () => {
    const wrapper = mountComponent()
    const input = wrapper.find('[data-testid="edit-destination-input"]').find('input')
    await input.setValue('ftp://example.com')
    await input.trigger('blur')
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('URL must start with http:// or https://')
  })

  it('enables save button with valid destination', async () => {
    const wrapper = mountComponent()
    const input = wrapper.find('[data-testid="edit-destination-input"]').find('input')
    await input.setValue('https://new-destination.com/page')
    await input.trigger('blur')
    await wrapper.vm.$nextTick()

    expect(wrapper.find('[data-testid="edit-save-btn"]').attributes('disabled')).toBeUndefined()
  })

  it('calls mutateAsync with updated values on save', async () => {
    const wrapper = mountComponent()
    const input = wrapper.find('[data-testid="edit-destination-input"]').find('input')
    await input.setValue('https://new-destination.com/page')
    await input.trigger('blur')
    await wrapper.vm.$nextTick()

    await wrapper.find('[data-testid="edit-save-btn"]').trigger('click')
    await wrapper.vm.$nextTick()

    expect(mockMutateAsync).toHaveBeenCalledWith(
      expect.objectContaining({ destinationUrl: 'https://new-destination.com/page' })
    )
  })

  it('emits updated and closes on success', async () => {
    const wrapper = mountComponent()

    await wrapper.find('[data-testid="edit-save-btn"]').trigger('click')
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('updated')).toBeTruthy()
    expect(wrapper.emitted('update:modelValue')).toBeTruthy()
    expect(wrapper.emitted('update:modelValue')![0]).toEqual([false])
  })

  it('closes without saving when cancel is clicked', async () => {
    const wrapper = mountComponent()
    await wrapper.find('[data-testid="edit-cancel-btn"]').trigger('click')
    expect(mockMutateAsync).not.toHaveBeenCalled()
    expect(wrapper.emitted('update:modelValue')![0]).toEqual([false])
  })

  it('shows error alert when save fails', async () => {
    const { useUpdateLink } = await import('@/composables/useLinks')
    vi.mocked(useUpdateLink).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: ref(false),
      isError: ref(true),
      error: ref(new Error('Update failed')),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useUpdateLink>)

    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="edit-error"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="edit-error"]').text()).toContain('Failed to update link')
  })
})
