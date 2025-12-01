import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { defineComponent, ref } from 'vue'
import LinkDetailView from '../LinkDetailView.vue'

// Mock canvas for Chart.js in jsdom
HTMLCanvasElement.prototype.getContext = vi.fn().mockReturnValue({
  canvas: { width: 100, height: 100 },
  createLinearGradient: vi.fn(() => ({
    addColorStop: vi.fn(),
  })),
  drawImage: vi.fn(),
  fillRect: vi.fn(),
  clearRect: vi.fn(),
  beginPath: vi.fn(),
  moveTo: vi.fn(),
  lineTo: vi.fn(),
  stroke: vi.fn(),
  fill: vi.fn(),
  arc: vi.fn(),
  fillText: vi.fn(),
  measureText: vi.fn(() => ({ width: 20 })),
  setTransform: vi.fn(),
  save: vi.fn(),
  restore: vi.fn(),
  scale: vi.fn(),
}) as unknown as CanvasRenderingContext2D

HTMLCanvasElement.prototype.toDataURL = vi.fn(() => 'data:image/png;base64,')
HTMLCanvasElement.prototype.toBlob = vi.fn()

vi.mock('@/composables/useLinks', () => ({
  useLink: vi.fn(),
  useUpdateLink: vi.fn(),
  useDeleteLink: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRouter: vi.fn().mockReturnValue({ push: vi.fn() }),
  useRoute: vi.fn().mockReturnValue({ params: { id: 'link-001' } }),
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
  analytics: {
    totalClicks: 1284,
    uniqueClicks: 924,
    clicksToday: 51,
    clickTrend: [
      { date: '2025-06-01', count: 42 },
      { date: '2025-06-02', count: 51 },
    ],
    topCountries: [
      { countryCode: 'US', countryName: 'United States', count: 449 },
      { countryCode: 'GB', countryName: 'United Kingdom', count: 192 },
    ],
    topReferrers: [
      { domain: 'google.com', count: 321 },
      { domain: 'twitter.com', count: 231 },
    ],
    devices: { desktop: 508, mobile: 323, tablet: 93 },
  },
  auditTrail: [
    {
      version: 1,
      action: 'created',
      description: 'Link created',
      timestamp: '2025-01-10T09:00:00Z',
    },
  ],
}

const stubDialog = (name: string) =>
  defineComponent({
    name,
    props: ['modelValue', 'link'],
    emits: ['update:modelValue', 'updated', 'deleted'],
    template: `<div :data-testid="'${name.toLowerCase()}-stub'" :data-visible="String(modelValue ?? false)"></div>`,
  })

function makeStubs() {
  return {
    EditLinkDialog: stubDialog('EditLinkDialog'),
    DeleteLinkDialog: stubDialog('DeleteLinkDialog'),
  }
}

function mountComponent() {
  return mount(LinkDetailView, {
    global: {
      plugins: [vuetify],
      stubs: makeStubs(),
    },
  })
}

describe('LinkDetailView', () => {
  beforeEach(async () => {
    vi.clearAllMocks()

    const { useLink, useUpdateLink, useDeleteLink } = await import('@/composables/useLinks')

    vi.mocked(useLink).mockReturnValue({
      data: ref(mockLink),
      isLoading: ref(false),
      isError: ref(false),
      error: ref(null),
    } as ReturnType<typeof useLink>)

    vi.mocked(useUpdateLink).mockReturnValue({
      mutateAsync: vi.fn().mockResolvedValue(mockLink),
      isPending: ref(false),
      isError: ref(false),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useUpdateLink>)

    vi.mocked(useDeleteLink).mockReturnValue({
      mutateAsync: vi.fn().mockResolvedValue(undefined),
      isPending: ref(false),
      isError: ref(false),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useDeleteLink>)
  })

  describe('loading state', () => {
    it('shows a skeleton loader while loading', async () => {
      const { useLink } = await import('@/composables/useLinks')
      vi.mocked(useLink).mockReturnValue({
        data: ref(undefined),
        isLoading: ref(true),
        isError: ref(false),
        error: ref(null),
      } as ReturnType<typeof useLink>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="loading-skeleton"]').exists()).toBe(true)
    })
  })

  describe('error state', () => {
    it('shows an error alert when the request fails', async () => {
      const { useLink } = await import('@/composables/useLinks')
      vi.mocked(useLink).mockReturnValue({
        data: ref(undefined),
        isLoading: ref(false),
        isError: ref(true),
        error: ref(new Error('Not found')),
      } as ReturnType<typeof useLink>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="detail-error"]').exists()).toBe(true)
    })
  })

  describe('link detail', () => {
    it('shows the short URL', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="link-short-url"]').text()).toContain('go.acme.com/launch')
    })

    it('shows the destination URL', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="link-destination"]').text()).toContain('acme.com')
    })

    it('shows the status chip', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="link-status-chip"]').text()).toContain('active')
    })

    it('shows the total clicks', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="total-clicks"]').text()).toContain('1,284')
    })

    it('shows the domain property', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="prop-domain"]').text()).toContain('go.acme.com')
    })

    it('shows the slug property', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="prop-slug"]').text()).toBe('launch')
    })

    it('shows the redirect type property', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="prop-redirect-type"]').text()).toBe('302')
    })
  })

  describe('action buttons', () => {
    it('renders copy, open, edit, and delete buttons', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="copy-btn"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="open-btn"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="edit-btn"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="delete-btn"]').exists()).toBe(true)
    })

    it('opens the edit dialog when the edit button is clicked', async () => {
      const wrapper = mountComponent()
      const stub = wrapper.find('[data-testid="editlinkdialog-stub"]')
      expect(stub.attributes('data-visible')).toBe('false')

      await wrapper.find('[data-testid="edit-btn"]').trigger('click')
      await wrapper.vm.$nextTick()

      expect(stub.attributes('data-visible')).toBe('true')
    })

    it('opens the delete dialog when the delete button is clicked', async () => {
      const wrapper = mountComponent()
      const stub = wrapper.find('[data-testid="deletelinkdialog-stub"]')
      expect(stub.attributes('data-visible')).toBe('false')

      await wrapper.find('[data-testid="delete-btn"]').trigger('click')
      await wrapper.vm.$nextTick()

      expect(stub.attributes('data-visible')).toBe('true')
    })
  })
})
