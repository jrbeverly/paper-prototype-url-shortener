import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { defineComponent, ref } from 'vue'
import DomainDetailView from '../DomainDetailView.vue'

vi.mock('@/composables/useDomains', () => ({
  useDomain: vi.fn(),
  useVerifyDomain: vi.fn(),
  useUpdateDomain: vi.fn(),
  useDeleteDomain: vi.fn(),
}))

const mockPush = vi.hoisted(() => vi.fn())
vi.mock('vue-router', () => ({
  useRouter: vi.fn().mockReturnValue({ push: mockPush }),
  useRoute: vi.fn().mockReturnValue({ params: { id: 'test-domain-id' } }),
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

vi.stubGlobal('navigator', {
  clipboard: { writeText: vi.fn().mockResolvedValue(undefined) },
})

const mockPendingDomain = {
  id: 'test-domain-id',
  tenantId: 'mock-tenant-id',
  hostname: 'go.acme.com',
  status: 'pending_verification' as const,
  certificateStatus: 'pending' as const,
  linkCount: 0,
  settings: {
    defaultRedirectUrl: null,
    errorPageBranding: null,
    notFoundBehavior: '404' as const,
  },
  verificationInstructions: {
    txtName: 'go.acme.com',
    txtValue: 'short-io-verify=abc123',
    cnameName: 'go.acme.com',
    cnameValue: 'cname.short.io',
  },
  createdAt: '2025-03-01T10:00:00Z',
  updatedAt: null,
  deletedAt: null,
}

const mockActiveDomain = {
  ...mockPendingDomain,
  status: 'active' as const,
  certificateStatus: 'issued' as const,
  linkCount: 5,
  verificationInstructions: undefined,
}

const stubDialog = (name: string) =>
  defineComponent({
    name,
    props: ['modelValue', 'domain'],
    emits: ['update:modelValue', 'deleted'],
    template: `<div :data-testid="'${name.toLowerCase()}-stub'" :data-visible="String(modelValue)"></div>`,
  })

function mountComponent() {
  return mount(DomainDetailView, {
    global: {
      plugins: [vuetify],
      stubs: {
        DeleteDomainDialog: stubDialog('DeleteDomainDialog'),
      },
    },
  })
}

describe('DomainDetailView', () => {
  let mockMutateAsyncVerify: ReturnType<typeof vi.fn>
  let mockMutateAsyncUpdate: ReturnType<typeof vi.fn>
  let mockMutateAsyncDelete: ReturnType<typeof vi.fn>

  beforeEach(async () => {
    vi.clearAllMocks()
    mockMutateAsyncVerify = vi.fn().mockResolvedValue({
      id: 'test-domain-id',
      hostname: 'go.acme.com',
      status: 'verification_failed',
      txtCheck: {
        passed: false,
        expected: 'short-io-verify=abc123',
        actual: null,
        error: 'Not found',
      },
      cnameCheck: { passed: false, expected: 'cname.short.io', actual: null, error: 'Not found' },
      message: 'Verification failed.',
      checkedAt: '2025-06-01T12:00:00Z',
    })
    mockMutateAsyncUpdate = vi.fn().mockResolvedValue({})
    mockMutateAsyncDelete = vi.fn().mockResolvedValue(undefined)

    const { useDomain, useVerifyDomain, useUpdateDomain, useDeleteDomain } =
      await import('@/composables/useDomains')

    vi.mocked(useDomain).mockReturnValue({
      data: ref(mockPendingDomain),
      isLoading: ref(false),
      isError: ref(false),
      error: ref(null),
    } as ReturnType<typeof useDomain>)

    vi.mocked(useVerifyDomain).mockReturnValue({
      mutateAsync: mockMutateAsyncVerify,
      isPending: ref(false),
      isError: ref(false),
      isSuccess: ref(false),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useVerifyDomain>)

    vi.mocked(useUpdateDomain).mockReturnValue({
      mutateAsync: mockMutateAsyncUpdate,
      isPending: ref(false),
      isError: ref(false),
      isSuccess: ref(false),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useUpdateDomain>)

    vi.mocked(useDeleteDomain).mockReturnValue({
      mutateAsync: mockMutateAsyncDelete,
      isPending: ref(false),
      isError: ref(false),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useDeleteDomain>)
  })

  describe('loading state', () => {
    it('shows a skeleton loader while loading', async () => {
      const { useDomain } = await import('@/composables/useDomains')
      vi.mocked(useDomain).mockReturnValue({
        data: ref(undefined),
        isLoading: ref(true),
        isError: ref(false),
        error: ref(null),
      } as ReturnType<typeof useDomain>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="loading-skeleton"]').exists()).toBe(true)
    })
  })

  describe('error state', () => {
    it('shows an error alert when the request fails', async () => {
      const { useDomain } = await import('@/composables/useDomains')
      vi.mocked(useDomain).mockReturnValue({
        data: ref(undefined),
        isLoading: ref(false),
        isError: ref(true),
        error: ref(new Error('Not found')),
      } as ReturnType<typeof useDomain>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="load-error"]').exists()).toBe(true)
    })
  })

  describe('domain header', () => {
    it('shows the domain hostname', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="domain-hostname"]').text()).toBe('go.acme.com')
    })

    it('shows the domain status chip', () => {
      const wrapper = mountComponent()
      const chip = wrapper.find('[data-testid="status-chip"]')
      expect(chip.exists()).toBe(true)
      expect(chip.text()).toContain('Pending verification')
    })

    it('shows the certificate status chip', () => {
      const wrapper = mountComponent()
      const chip = wrapper.find('[data-testid="cert-status-chip"]')
      expect(chip.exists()).toBe(true)
      expect(chip.text()).toContain('Pending')
    })

    it('shows Active status for an active domain', async () => {
      const { useDomain } = await import('@/composables/useDomains')
      vi.mocked(useDomain).mockReturnValue({
        data: ref(mockActiveDomain),
        isLoading: ref(false),
        isError: ref(false),
        error: ref(null),
      } as ReturnType<typeof useDomain>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="status-chip"]').text()).toContain('Active')
    })
  })

  describe('DNS instructions', () => {
    it('shows DNS instructions for a pending_verification domain', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="dns-instructions-card"]').exists()).toBe(true)
    })

    it('hides DNS instructions for an active domain', async () => {
      const { useDomain } = await import('@/composables/useDomains')
      vi.mocked(useDomain).mockReturnValue({
        data: ref(mockActiveDomain),
        isLoading: ref(false),
        isError: ref(false),
        error: ref(null),
      } as ReturnType<typeof useDomain>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="dns-instructions-card"]').exists()).toBe(false)
    })

    it('displays the TXT record name and value', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="txt-name"]').text()).toBe('go.acme.com')
      expect(wrapper.find('[data-testid="txt-value"]').text()).toBe('short-io-verify=abc123')
    })

    it('displays the CNAME record name and value', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="cname-name"]').text()).toBe('go.acme.com')
      expect(wrapper.find('[data-testid="cname-value"]').text()).toBe('cname.short.io')
    })

    it('renders copy buttons for TXT and CNAME records', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="copy-txt-btn"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="copy-cname-btn"]').exists()).toBe(true)
    })
  })

  describe('verification', () => {
    it('renders the verify button', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="verify-btn"]').exists()).toBe(true)
    })

    it('calls the verify mutation when verify button is clicked', async () => {
      const wrapper = mountComponent()
      await wrapper.find('[data-testid="verify-btn"]').trigger('click')
      expect(mockMutateAsyncVerify).toHaveBeenCalledWith('test-domain-id')
    })

    it('shows the verify result panel after a check', async () => {
      const wrapper = mountComponent()
      await wrapper.find('[data-testid="verify-btn"]').trigger('click')
      await wrapper.vm.$nextTick()

      expect(wrapper.find('[data-testid="verify-result-card"]').exists()).toBe(true)
    })

    it('shows the verification result message', async () => {
      const wrapper = mountComponent()
      await wrapper.find('[data-testid="verify-btn"]').trigger('click')
      await wrapper.vm.$nextTick()

      expect(wrapper.find('[data-testid="verify-result-message"]').text()).toContain(
        'Verification failed'
      )
    })
  })

  describe('domain settings', () => {
    it('renders the settings card', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="settings-card"]').exists()).toBe(true)
    })

    it('renders the default redirect URL input', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="default-redirect-input"]').exists()).toBe(true)
    })

    it('renders the not found behavior select', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="not-found-select"]').exists()).toBe(true)
    })

    it('renders the save settings button', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="save-settings-btn"]').exists()).toBe(true)
    })

    it('calls update mutation when save settings is clicked', async () => {
      const wrapper = mountComponent()
      await wrapper.find('[data-testid="save-settings-btn"]').trigger('click')
      expect(mockMutateAsyncUpdate).toHaveBeenCalled()
    })

    it('shows a success alert after settings are saved', async () => {
      const { useUpdateDomain } = await import('@/composables/useDomains')
      vi.mocked(useUpdateDomain).mockReturnValue({
        mutateAsync: mockMutateAsyncUpdate,
        isPending: ref(false),
        isError: ref(false),
        isSuccess: ref(true),
        reset: vi.fn(),
      } as unknown as ReturnType<typeof useUpdateDomain>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="settings-saved"]').exists()).toBe(true)
    })
  })

  describe('danger zone', () => {
    it('renders the danger zone card', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="danger-zone-card"]').exists()).toBe(true)
    })

    it('renders the delete domain button', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="delete-domain-btn"]').exists()).toBe(true)
    })

    it('opens the delete dialog when delete domain is clicked', async () => {
      const wrapper = mountComponent()
      const stub = wrapper.find('[data-testid="deletedomaindialog-stub"]')
      expect(stub.attributes('data-visible')).toBe('false')

      await wrapper.find('[data-testid="delete-domain-btn"]').trigger('click')
      await wrapper.vm.$nextTick()

      expect(stub.attributes('data-visible')).toBe('true')
    })
  })
})
