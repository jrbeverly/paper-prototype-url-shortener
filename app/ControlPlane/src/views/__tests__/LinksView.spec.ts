import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { defineComponent, ref } from 'vue'
import LinksView from '../LinksView.vue'

vi.mock('@/composables/useLinks', () => ({
  useLinks: vi.fn(),
  useDeleteLink: vi.fn(),
  useCreateLink: vi.fn(),
}))

vi.mock('@/composables/useDomains', () => ({
  useDomains: vi.fn(),
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

const mockLinks = [
  {
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
  },
  {
    id: 'link-002',
    domainId: 'domain-001',
    domainHostname: 'go.acme.com',
    slug: 'pricing',
    destinationUrl: 'https://acme.com/pricing',
    shortUrl: 'https://go.acme.com/pricing',
    redirectType: '301' as const,
    status: 'paused' as const,
    clickCount: 537,
    maxClicks: null,
    expiresAt: null,
    createdAt: '2025-02-15T11:30:00Z',
    updatedAt: '2025-02-15T11:30:00Z',
    version: 1,
    expiryWarning: null,
  },
]

const mockPage = { items: mockLinks, nextCursor: null, totalCount: 2 }

const stubDialog = (name: string) =>
  defineComponent({
    name,
    props: ['modelValue', 'link'],
    emits: ['update:modelValue', 'created', 'deleted'],
    template: `<div :data-testid="'${name.toLowerCase()}-stub'" :data-visible="String(modelValue ?? false)"></div>`,
  })

function makeStubs() {
  return {
    CreateLinkDialog: stubDialog('CreateLinkDialog'),
    DeleteLinkDialog: stubDialog('DeleteLinkDialog'),
  }
}

function mountComponent() {
  return mount(LinksView, {
    global: {
      plugins: [vuetify],
      stubs: makeStubs(),
    },
  })
}

describe('LinksView', () => {
  beforeEach(async () => {
    vi.clearAllMocks()

    const { useLinks, useDeleteLink, useCreateLink } = await import('@/composables/useLinks')
    const { useDomains } = await import('@/composables/useDomains')

    vi.mocked(useLinks).mockReturnValue({
      data: ref(mockPage),
      isLoading: ref(false),
      isError: ref(false),
      isFetching: ref(false),
      error: ref(null),
    } as ReturnType<typeof useLinks>)

    vi.mocked(useDeleteLink).mockReturnValue({
      mutateAsync: vi.fn().mockResolvedValue(undefined),
      isPending: ref(false),
      isError: ref(false),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useDeleteLink>)

    vi.mocked(useCreateLink).mockReturnValue({
      mutateAsync: vi.fn().mockResolvedValue(mockLinks[0]),
      isPending: ref(false),
      isError: ref(false),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useCreateLink>)

    vi.mocked(useDomains).mockReturnValue({
      data: ref({
        items: [{ id: 'domain-001', hostname: 'go.acme.com', status: 'active' }],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      }),
      isLoading: ref(false),
      isError: ref(false),
      error: ref(null),
    } as ReturnType<typeof useDomains>)
  })

  describe('loading state', () => {
    it('shows skeleton loaders while loading', async () => {
      const { useLinks } = await import('@/composables/useLinks')
      vi.mocked(useLinks).mockReturnValue({
        data: ref(undefined),
        isLoading: ref(true),
        isError: ref(false),
        isFetching: ref(false),
        error: ref(null),
      } as ReturnType<typeof useLinks>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="links-card"]').exists()).toBe(true)
    })
  })

  describe('error state', () => {
    it('shows an error alert when the request fails', async () => {
      const { useLinks } = await import('@/composables/useLinks')
      vi.mocked(useLinks).mockReturnValue({
        data: ref(undefined),
        isLoading: ref(false),
        isError: ref(true),
        isFetching: ref(false),
        error: ref(new Error('Request failed')),
      } as ReturnType<typeof useLinks>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="list-error"]').exists()).toBe(true)
    })
  })

  describe('empty state', () => {
    it('shows the empty state when there are no links', async () => {
      const { useLinks } = await import('@/composables/useLinks')
      vi.mocked(useLinks).mockReturnValue({
        data: ref({ items: [], nextCursor: null, totalCount: 0 }),
        isLoading: ref(false),
        isError: ref(false),
        isFetching: ref(false),
        error: ref(null),
      } as ReturnType<typeof useLinks>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="empty-state"]').exists()).toBe(true)
    })

    it('shows the create link button in the empty state', async () => {
      const { useLinks } = await import('@/composables/useLinks')
      vi.mocked(useLinks).mockReturnValue({
        data: ref({ items: [], nextCursor: null, totalCount: 0 }),
        isLoading: ref(false),
        isError: ref(false),
        isFetching: ref(false),
        error: ref(null),
      } as ReturnType<typeof useLinks>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="empty-create-btn"]').exists()).toBe(true)
    })
  })

  describe('links table', () => {
    it('renders the links table', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="links-table"]').exists()).toBe(true)
    })

    it('shows a row for each link', () => {
      const wrapper = mountComponent()
      for (const link of mockLinks) {
        expect(wrapper.find(`[data-testid="link-row-${link.id}"]`).exists()).toBe(true)
      }
    })

    it('shows the slug for each link', () => {
      const wrapper = mountComponent()
      const slugCell = wrapper.find('[data-testid="link-slug-link-001"]')
      expect(slugCell.text()).toContain('launch')
    })

    it('shows the destination URL (truncated)', () => {
      const wrapper = mountComponent()
      const destCell = wrapper.find('[data-testid="link-destination-link-001"]')
      expect(destCell.text()).toContain('acme.com')
    })

    it('shows the click count', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="link-clicks-link-001"]').text()).toBe('1284')
    })

    it('shows a green status chip for active links', () => {
      const wrapper = mountComponent()
      const chip = wrapper.find('[data-testid="link-status-chip-link-001"]')
      expect(chip.exists()).toBe(true)
      expect(chip.text()).toContain('active')
    })

    it('shows a warning status chip for paused links', () => {
      const wrapper = mountComponent()
      const chip = wrapper.find('[data-testid="link-status-chip-link-002"]')
      expect(chip.exists()).toBe(true)
      expect(chip.text()).toContain('paused')
    })

    it('shows copy and delete buttons for each link', () => {
      const wrapper = mountComponent()
      for (const link of mockLinks) {
        expect(wrapper.find(`[data-testid="copy-btn-${link.id}"]`).exists()).toBe(true)
        expect(wrapper.find(`[data-testid="delete-btn-${link.id}"]`).exists()).toBe(true)
      }
    })
  })

  describe('create link dialog', () => {
    it('renders the create link button', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="create-link-btn"]').exists()).toBe(true)
    })

    it('opens the create dialog when the button is clicked', async () => {
      const wrapper = mountComponent()
      const stub = wrapper.find('[data-testid="createlinkdialog-stub"]')
      expect(stub.attributes('data-visible')).toBe('false')

      await wrapper.find('[data-testid="create-link-btn"]').trigger('click')
      await wrapper.vm.$nextTick()

      expect(stub.attributes('data-visible')).toBe('true')
    })
  })

  describe('delete link dialog', () => {
    it('opens the delete dialog when the delete button is clicked', async () => {
      const wrapper = mountComponent()

      await wrapper.find('[data-testid="delete-btn-link-001"]').trigger('click')
      await wrapper.vm.$nextTick()

      const stub = wrapper.find('[data-testid="deletelinkdialog-stub"]')
      expect(stub.exists()).toBe(true)
      expect(stub.attributes('data-visible')).toBe('true')
    })
  })

  describe('search and filters', () => {
    it('renders the search input', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="search-input"]').exists()).toBe(true)
    })

    it('renders the domain filter', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="domain-filter"]').exists()).toBe(true)
    })

    it('renders the status filter', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="status-filter"]').exists()).toBe(true)
    })
  })
})
