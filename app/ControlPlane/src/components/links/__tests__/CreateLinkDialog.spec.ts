import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { ref } from 'vue'
import { setActivePinia, createPinia } from 'pinia'
import CreateLinkDialog from '../CreateLinkDialog.vue'

vi.mock('@/composables/useLinks', () => ({
  useCreateLink: vi.fn(),
}))

vi.mock('@/composables/useDomains', () => ({
  useDomains: vi.fn(),
}))

vi.mock('@/services/linkService', () => ({
  linkService: {
    checkSlugAvailability: vi.fn(),
  },
}))

vi.mock('@/stores/workspace', () => ({
  useWorkspaceStore: vi.fn(() => ({
    currentWorkspaceId: 'mock-tenant-id',
    workspaces: [],
    setCurrentWorkspace: vi.fn(),
  })),
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

const mockDomains = {
  items: [
    {
      id: 'domain-001',
      hostname: 'go.acme.com',
      status: 'active',
      certificateStatus: 'issued',
      linkCount: 5,
      createdAt: '2025-01-01',
    },
    {
      id: 'domain-002',
      hostname: 'test.io',
      status: 'pending_verification',
      certificateStatus: 'pending',
      linkCount: 0,
      createdAt: '2025-05-01',
    },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 2,
}

function mountComponent(modelValue = true) {
  return mount(CreateLinkDialog, {
    props: { modelValue },
    global: {
      plugins: [vuetify],
      stubs: { 'v-dialog': { template: '<div><slot /></div>' } },
    },
  })
}

describe('CreateLinkDialog', () => {
  let mockMutateAsync: ReturnType<typeof vi.fn>
  let mockCheckSlug: ReturnType<typeof vi.fn>

  beforeEach(async () => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
    mockMutateAsync = vi.fn().mockResolvedValue({
      id: 'new-link',
      domainId: 'domain-001',
      domainHostname: 'go.acme.com',
      slug: 'my-slug',
      destinationUrl: 'https://example.com',
      shortUrl: 'https://go.acme.com/my-slug',
      redirectType: '302',
      status: 'active',
      clickCount: 0,
      maxClicks: null,
      expiresAt: null,
      createdAt: '2025-06-01',
      updatedAt: '2025-06-01',
      version: 1,
      expiryWarning: null,
    })

    const { useCreateLink } = await import('@/composables/useLinks')
    const { useDomains } = await import('@/composables/useDomains')
    const { linkService } = await import('@/services/linkService')

    vi.mocked(useCreateLink).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: ref(false),
      isError: ref(false),
      error: ref(null),
      variables: ref(undefined),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useCreateLink>)

    vi.mocked(useDomains).mockReturnValue({
      data: ref(mockDomains),
      isLoading: ref(false),
      isError: ref(false),
      isFetching: ref(false),
      error: ref(null),
    } as ReturnType<typeof useDomains>)

    mockCheckSlug = vi.fn().mockResolvedValue({ available: true })
    vi.mocked(linkService.checkSlugAvailability).mockImplementation(mockCheckSlug)
  })

  it('renders the dialog title', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('Create link')
  })

  it('renders the domain select', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="domain-select"]').exists()).toBe(true)
  })

  it('renders the destination URL input', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="destination-input"]').exists()).toBe(true)
  })

  it('renders the slug input', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="slug-input"]').exists()).toBe(true)
  })

  it('renders the redirect type select', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="redirect-type-select"]').exists()).toBe(true)
  })

  it('renders the expires at input', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="expires-at-input"]').exists()).toBe(true)
  })

  it('renders the max clicks input', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="max-clicks-input"]').exists()).toBe(true)
  })

  it('renders cancel and create buttons', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="create-cancel-btn"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="create-submit-btn"]').exists()).toBe(true)
  })

  it('disables create button when form is incomplete', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="create-submit-btn"]').attributes('disabled')).toBeDefined()
  })

  describe('form validation', () => {
    it('shows destination error for invalid URL', async () => {
      const wrapper = mountComponent()
      const destInput = wrapper.find('[data-testid="destination-input"]').find('input')
      await destInput.setValue('not-a-url')
      await destInput.trigger('blur')
      await wrapper.vm.$nextTick()

      expect(wrapper.text()).toContain('Enter a valid URL')
    })

    it('shows destination error for non-http protocol', async () => {
      const wrapper = mountComponent()
      const destInput = wrapper.find('[data-testid="destination-input"]').find('input')
      await destInput.setValue('ftp://example.com')
      await destInput.trigger('blur')
      await wrapper.vm.$nextTick()

      expect(wrapper.text()).toContain('URL must start with http:// or https://')
    })

    it('clears destination error on valid URL', async () => {
      const wrapper = mountComponent()
      const destInput = wrapper.find('[data-testid="destination-input"]').find('input')
      await destInput.setValue('not-a-url')
      await destInput.trigger('blur')
      await wrapper.vm.$nextTick()
      expect(wrapper.text()).toContain('Enter a valid URL')

      await destInput.setValue('https://valid.com')
      await destInput.trigger('blur')
      await wrapper.vm.$nextTick()
      expect(wrapper.text()).not.toContain('Enter a valid URL')
    })

    it('shows slug error for invalid characters', async () => {
      const wrapper = mountComponent()
      const slugInput = wrapper.find('[data-testid="slug-input"]').find('input')
      await slugInput.setValue('slug with spaces!')
      await slugInput.trigger('blur')
      await wrapper.vm.$nextTick()

      expect(wrapper.text()).toContain('Only letters, numbers, and hyphens allowed')
    })

    it('shows slug error when exceeding 100 characters', async () => {
      const wrapper = mountComponent()
      const slugInput = wrapper.find('[data-testid="slug-input"]').find('input')
      await slugInput.setValue('a'.repeat(101))
      await slugInput.trigger('blur')
      await wrapper.vm.$nextTick()

      expect(wrapper.text()).toContain('Slug cannot exceed 100 characters')
    })
  })

  it('shows error alert when create fails', async () => {
    const { useCreateLink } = await import('@/composables/useLinks')
    vi.mocked(useCreateLink).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: ref(false),
      isError: ref(true),
      error: ref(new Error('Create failed')),
      variables: ref(undefined),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useCreateLink>)

    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="create-error"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="create-error"]').text()).toContain('Failed to create link')
  })

  it('closes without creating when cancel is clicked', async () => {
    const wrapper = mountComponent()
    await wrapper.find('[data-testid="create-cancel-btn"]').trigger('click')
    expect(mockMutateAsync).not.toHaveBeenCalled()
    expect(wrapper.emitted('update:modelValue')![0]).toEqual([false])
  })

  it('disables the create button while pending', async () => {
    const { useCreateLink } = await import('@/composables/useLinks')
    vi.mocked(useCreateLink).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: ref(true),
      isError: ref(false),
      error: ref(null),
      variables: ref(undefined),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useCreateLink>)

    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="create-submit-btn"]').attributes('disabled')).toBeDefined()
  })

  it('does not submit if form is incomplete even when button is clicked', async () => {
    const wrapper = mountComponent()
    // Form is incomplete (no domain selected, no destination URL)
    await wrapper.find('[data-testid="create-submit-btn"]').trigger('click')
    expect(mockMutateAsync).not.toHaveBeenCalled()
  })

  it('clears form fields on close', async () => {
    const wrapper = mountComponent()
    const destInput = wrapper.find('[data-testid="destination-input"]').find('input')
    await destInput.setValue('https://example.com')
    await wrapper.vm.$nextTick()

    await wrapper.find('[data-testid="create-cancel-btn"]').trigger('click')
    await wrapper.vm.$nextTick()

    expect((destInput.element as HTMLInputElement).value).toBe('')
  })
})
