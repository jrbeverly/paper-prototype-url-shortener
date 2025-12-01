import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { defineComponent, ref } from 'vue'
import DomainsView from '../DomainsView.vue'

vi.mock('@/composables/useDomains', () => ({
  useDomains: vi.fn(),
  useAddDomain: vi.fn(),
  useDeleteDomain: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRouter: vi.fn().mockReturnValue({ push: vi.fn() }),
  useRoute: vi.fn().mockReturnValue({ params: {} }),
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

const mockDomains = [
  {
    id: 'domain-001',
    hostname: 'go.acme.com',
    status: 'active' as const,
    certificateStatus: 'issued' as const,
    linkCount: 42,
    createdAt: '2025-03-01T10:00:00Z',
  },
  {
    id: 'domain-002',
    hostname: 'links.example.org',
    status: 'pending_verification' as const,
    certificateStatus: 'pending' as const,
    linkCount: 0,
    createdAt: '2025-05-20T14:30:00Z',
  },
  {
    id: 'domain-003',
    hostname: 'short.mybrand.io',
    status: 'verification_failed' as const,
    certificateStatus: 'pending' as const,
    linkCount: 0,
    createdAt: '2025-05-25T09:15:00Z',
  },
]

const mockDomainList = { items: mockDomains, page: 1, pageSize: 20, totalCount: 3 }

const stubDialog = (name: string) =>
  defineComponent({
    name,
    props: ['modelValue', 'domain'],
    emits: ['update:modelValue', 'created', 'deleted'],
    template: `<div :data-testid="'${name.toLowerCase()}-stub'" :data-visible="String(modelValue)"></div>`,
  })

function makeStubs() {
  return {
    AddDomainDialog: stubDialog('AddDomainDialog'),
    DeleteDomainDialog: stubDialog('DeleteDomainDialog'),
  }
}

function mountComponent() {
  return mount(DomainsView, {
    global: {
      plugins: [vuetify],
      stubs: makeStubs(),
    },
  })
}

describe('DomainsView', () => {
  beforeEach(async () => {
    vi.clearAllMocks()

    const { useDomains, useAddDomain, useDeleteDomain } = await import('@/composables/useDomains')

    vi.mocked(useDomains).mockReturnValue({
      data: ref(mockDomainList),
      isLoading: ref(false),
      isError: ref(false),
      error: ref(null),
    } as ReturnType<typeof useDomains>)

    vi.mocked(useAddDomain).mockReturnValue({
      mutateAsync: vi.fn().mockResolvedValue({}),
      isPending: ref(false),
      isError: ref(false),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useAddDomain>)

    vi.mocked(useDeleteDomain).mockReturnValue({
      mutateAsync: vi.fn().mockResolvedValue(undefined),
      isPending: ref(false),
      isError: ref(false),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useDeleteDomain>)
  })

  describe('loading state', () => {
    it('shows skeleton loaders while loading', async () => {
      const { useDomains } = await import('@/composables/useDomains')
      vi.mocked(useDomains).mockReturnValue({
        data: ref(undefined),
        isLoading: ref(true),
        isError: ref(false),
        error: ref(null),
      } as ReturnType<typeof useDomains>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="domains-card"]').exists()).toBe(true)
    })
  })

  describe('error state', () => {
    it('shows an error alert when the request fails', async () => {
      const { useDomains } = await import('@/composables/useDomains')
      vi.mocked(useDomains).mockReturnValue({
        data: ref(undefined),
        isLoading: ref(false),
        isError: ref(true),
        error: ref(new Error('Request failed')),
      } as ReturnType<typeof useDomains>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="list-error"]').exists()).toBe(true)
    })
  })

  describe('empty state', () => {
    it('shows the empty state when there are no domains', async () => {
      const { useDomains } = await import('@/composables/useDomains')
      vi.mocked(useDomains).mockReturnValue({
        data: ref({ items: [], page: 1, pageSize: 20, totalCount: 0 }),
        isLoading: ref(false),
        isError: ref(false),
        error: ref(null),
      } as ReturnType<typeof useDomains>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="empty-state"]').exists()).toBe(true)
    })

    it('shows the add domain button in the empty state', async () => {
      const { useDomains } = await import('@/composables/useDomains')
      vi.mocked(useDomains).mockReturnValue({
        data: ref({ items: [], page: 1, pageSize: 20, totalCount: 0 }),
        isLoading: ref(false),
        isError: ref(false),
        error: ref(null),
      } as ReturnType<typeof useDomains>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="empty-add-btn"]').exists()).toBe(true)
    })
  })

  describe('domains table', () => {
    it('renders the domains table', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="domains-table"]').exists()).toBe(true)
    })

    it('shows a row for each domain', () => {
      const wrapper = mountComponent()
      for (const domain of mockDomains) {
        expect(wrapper.find(`[data-testid="domain-row-${domain.id}"]`).exists()).toBe(true)
      }
    })

    it('shows the hostname for each domain', () => {
      const wrapper = mountComponent()
      for (const domain of mockDomains) {
        expect(wrapper.find(`[data-testid="domain-hostname-${domain.id}"]`).text()).toBe(
          domain.hostname
        )
      }
    })

    it('shows a green status chip for active domains', () => {
      const wrapper = mountComponent()
      const chip = wrapper.find('[data-testid="domain-status-chip-domain-001"]')
      expect(chip.exists()).toBe(true)
      expect(chip.text()).toContain('Active')
    })

    it('shows a yellow status chip for pending domains', () => {
      const wrapper = mountComponent()
      const chip = wrapper.find('[data-testid="domain-status-chip-domain-002"]')
      expect(chip.exists()).toBe(true)
      expect(chip.text()).toContain('Pending')
    })

    it('shows a red status chip for failed domains', () => {
      const wrapper = mountComponent()
      const chip = wrapper.find('[data-testid="domain-status-chip-domain-003"]')
      expect(chip.exists()).toBe(true)
      expect(chip.text()).toContain('Failed')
    })

    it('shows the certificate status chip', () => {
      const wrapper = mountComponent()
      const chip = wrapper.find('[data-testid="domain-cert-chip-domain-001"]')
      expect(chip.exists()).toBe(true)
      expect(chip.text()).toContain('Issued')
    })

    it('shows the link count', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="domain-links-domain-001"]').text()).toBe('42')
      expect(wrapper.find('[data-testid="domain-links-domain-002"]').text()).toBe('0')
    })

    it('shows a delete button for each domain', () => {
      const wrapper = mountComponent()
      for (const domain of mockDomains) {
        expect(wrapper.find(`[data-testid="domain-delete-btn-${domain.id}"]`).exists()).toBe(true)
      }
    })
  })

  describe('add domain dialog', () => {
    it('renders the add domain button', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="add-domain-btn"]').exists()).toBe(true)
    })

    it('opens the add dialog when the add domain button is clicked', async () => {
      const wrapper = mountComponent()
      const stub = wrapper.find('[data-testid="adddomaindialog-stub"]')
      expect(stub.attributes('data-visible')).toBe('false')

      await wrapper.find('[data-testid="add-domain-btn"]').trigger('click')
      await wrapper.vm.$nextTick()

      expect(stub.attributes('data-visible')).toBe('true')
    })

    it('opens the add dialog when the empty-state button is clicked', async () => {
      const { useDomains } = await import('@/composables/useDomains')
      vi.mocked(useDomains).mockReturnValue({
        data: ref({ items: [], page: 1, pageSize: 20, totalCount: 0 }),
        isLoading: ref(false),
        isError: ref(false),
        error: ref(null),
      } as ReturnType<typeof useDomains>)

      const wrapper = mountComponent()
      await wrapper.find('[data-testid="empty-add-btn"]').trigger('click')
      await wrapper.vm.$nextTick()

      expect(wrapper.find('[data-testid="adddomaindialog-stub"]').attributes('data-visible')).toBe(
        'true'
      )
    })
  })

  describe('delete domain dialog', () => {
    it('opens the delete dialog when the delete button is clicked', async () => {
      const wrapper = mountComponent()

      // Stub is not rendered until domainToDelete is set (v-if="domainToDelete")
      await wrapper.find('[data-testid="domain-delete-btn-domain-001"]').trigger('click')
      await wrapper.vm.$nextTick()

      const stub = wrapper.find('[data-testid="deletedomaindialog-stub"]')
      expect(stub.exists()).toBe(true)
      expect(stub.attributes('data-visible')).toBe('true')
    })
  })
})
