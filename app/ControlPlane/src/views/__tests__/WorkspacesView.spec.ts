import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { ref } from 'vue'
import WorkspacesView from '../WorkspacesView.vue'

vi.mock('@/composables/useWorkspace', () => ({
  useWorkspaceSettings: vi.fn(),
  useUpdateWorkspaceSettings: vi.fn(),
  useDeleteWorkspace: vi.fn(),
}))

vi.mock('@/stores/workspace', () => ({
  useWorkspaceStore: vi.fn().mockReturnValue({ currentWorkspaceId: 'ws-001' }),
}))

vi.mock('@/stores/ui', () => ({
  useUIStore: vi.fn().mockReturnValue({ addNotification: vi.fn() }),
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

const mockSettings = {
  id: 'ws-001',
  name: 'Acme Corp',
  slug: 'acme-corp',
  branding: { primaryColor: '#FF5733', logoUrl: null },
  defaults: { fallback404Url: 'https://acme.com/404', defaultRedirectType: '302' as const },
}

function mountComponent() {
  return mount(WorkspacesView, {
    global: {
      plugins: [vuetify],
      // Stub v-dialog to avoid jsdom visualViewport errors when dialogs open
      stubs: {
        'v-dialog': { template: '<div><slot /></div>' },
      },
    },
  })
}

describe('WorkspacesView', () => {
  let mockMutateAsync: ReturnType<typeof vi.fn>

  beforeEach(async () => {
    vi.clearAllMocks()
    mockMutateAsync = vi.fn().mockResolvedValue(mockSettings)

    const { useWorkspaceSettings, useUpdateWorkspaceSettings, useDeleteWorkspace } =
      await import('@/composables/useWorkspace')

    vi.mocked(useWorkspaceSettings).mockReturnValue({
      data: ref(mockSettings),
      isLoading: ref(false),
      isError: ref(false),
      isFetching: ref(false),
      error: ref(null),
    } as ReturnType<typeof useWorkspaceSettings>)

    vi.mocked(useUpdateWorkspaceSettings).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: ref(false),
      isError: ref(false),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useUpdateWorkspaceSettings>)

    vi.mocked(useDeleteWorkspace).mockReturnValue({
      mutateAsync: vi.fn().mockResolvedValue(undefined),
      isPending: ref(false),
      isError: ref(false),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useDeleteWorkspace>)
  })

  // ── Loading state ─────────────────────────────────────────────────────────────
  describe('loading state', () => {
    it('shows loading card while data is fetching', async () => {
      const { useWorkspaceSettings } = await import('@/composables/useWorkspace')
      vi.mocked(useWorkspaceSettings).mockReturnValue({
        data: ref(undefined),
        isLoading: ref(true),
        isError: ref(false),
        isFetching: ref(true),
        error: ref(null),
      } as ReturnType<typeof useWorkspaceSettings>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="general-card"]').exists()).toBe(true)
    })
  })

  // ── Error state ───────────────────────────────────────────────────────────────
  describe('error state', () => {
    it('shows load error alert when settings fail to fetch', async () => {
      const { useWorkspaceSettings } = await import('@/composables/useWorkspace')
      vi.mocked(useWorkspaceSettings).mockReturnValue({
        data: ref(undefined),
        isLoading: ref(false),
        isError: ref(true),
        isFetching: ref(false),
        error: ref(new Error('Network error')),
      } as ReturnType<typeof useWorkspaceSettings>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="load-error"]').exists()).toBe(true)
    })
  })

  // ── General section ───────────────────────────────────────────────────────────
  describe('general section', () => {
    it('renders the general card', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="general-card"]').exists()).toBe(true)
    })

    it('pre-fills name and slug from loaded settings', () => {
      const wrapper = mountComponent()
      const nameInput = wrapper.find('[data-testid="name-input"] input')
      const slugInput = wrapper.find('[data-testid="slug-input"] input')
      expect((nameInput.element as HTMLInputElement).value).toBe('Acme Corp')
      expect((slugInput.element as HTMLInputElement).value).toBe('acme-corp')
    })

    it('renders the general save button', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="general-save-btn"]').exists()).toBe(true)
    })

    it('calls updateSettings with name and slug on save', async () => {
      const wrapper = mountComponent()
      await wrapper.find('[data-testid="general-save-btn"]').trigger('click')
      await flushPromises()
      expect(mockMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ name: 'Acme Corp', slug: 'acme-corp' })
      )
    })

    it('shows success alert after saving general settings', async () => {
      const wrapper = mountComponent()
      await wrapper.find('[data-testid="general-save-btn"]').trigger('click')
      await flushPromises()
      expect(wrapper.find('[data-testid="general-success"]').exists()).toBe(true)
    })

    it('shows error alert when general save fails', async () => {
      const { useUpdateWorkspaceSettings } = await import('@/composables/useWorkspace')
      vi.mocked(useUpdateWorkspaceSettings).mockReturnValue({
        mutateAsync: vi.fn().mockRejectedValue(new Error('API error')),
        isPending: ref(false),
        isError: ref(false),
        reset: vi.fn(),
      } as unknown as ReturnType<typeof useUpdateWorkspaceSettings>)

      const wrapper = mountComponent()
      await wrapper.find('[data-testid="general-save-btn"]').trigger('click')
      await flushPromises()
      expect(wrapper.find('[data-testid="general-error"]').exists()).toBe(true)
    })
  })

  // ── Branding section ──────────────────────────────────────────────────────────
  describe('branding section', () => {
    it('renders the branding card', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="branding-card"]').exists()).toBe(true)
    })

    it('renders the color picker button', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="color-picker-btn"]').exists()).toBe(true)
    })

    it('renders the color swatch element', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="color-swatch"]').exists()).toBe(true)
    })

    it('renders the color picker button showing the current color value', () => {
      const wrapper = mountComponent()
      const btn = wrapper.find('[data-testid="color-picker-btn"]')
      expect(btn.text()).toContain('#FF5733')
    })

    it('renders the live color preview chip', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="color-preview-chip"]').exists()).toBe(true)
    })

    it('renders the logo upload button (disabled placeholder)', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="logo-upload-btn"]').exists()).toBe(true)
    })

    it('renders the branding save button', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="branding-save-btn"]').exists()).toBe(true)
    })

    it('calls updateSettings with branding on save', async () => {
      const wrapper = mountComponent()
      await wrapper.find('[data-testid="branding-save-btn"]').trigger('click')
      await flushPromises()
      expect(mockMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ branding: expect.objectContaining({ primaryColor: '#FF5733' }) })
      )
    })

    it('shows success alert after saving branding', async () => {
      const wrapper = mountComponent()
      await wrapper.find('[data-testid="branding-save-btn"]').trigger('click')
      await flushPromises()
      expect(wrapper.find('[data-testid="branding-success"]').exists()).toBe(true)
    })

    it('shows error alert when branding save fails', async () => {
      const { useUpdateWorkspaceSettings } = await import('@/composables/useWorkspace')
      vi.mocked(useUpdateWorkspaceSettings).mockReturnValue({
        mutateAsync: vi.fn().mockRejectedValue(new Error('API error')),
        isPending: ref(false),
        isError: ref(false),
        reset: vi.fn(),
      } as unknown as ReturnType<typeof useUpdateWorkspaceSettings>)

      const wrapper = mountComponent()
      await wrapper.find('[data-testid="branding-save-btn"]').trigger('click')
      await flushPromises()
      expect(wrapper.find('[data-testid="branding-error"]').exists()).toBe(true)
    })
  })

  // ── Defaults section ──────────────────────────────────────────────────────────
  describe('defaults section', () => {
    it('renders the defaults card', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="defaults-card"]').exists()).toBe(true)
    })

    it('renders the redirect type select', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="redirect-type-select"]').exists()).toBe(true)
    })

    it('renders the 404 fallback URL input', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="fallback-url-input"]').exists()).toBe(true)
    })

    it('pre-fills fallback URL from loaded settings', () => {
      const wrapper = mountComponent()
      const input = wrapper.find('[data-testid="fallback-url-input"] input')
      expect((input.element as HTMLInputElement).value).toBe('https://acme.com/404')
    })

    it('renders the defaults save button', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="defaults-save-btn"]').exists()).toBe(true)
    })

    it('calls updateSettings with defaults on save', async () => {
      const wrapper = mountComponent()
      await wrapper.find('[data-testid="defaults-save-btn"]').trigger('click')
      await flushPromises()
      expect(mockMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({
          defaults: expect.objectContaining({ defaultRedirectType: '302' }),
        })
      )
    })

    it('shows success alert after saving defaults', async () => {
      const wrapper = mountComponent()
      await wrapper.find('[data-testid="defaults-save-btn"]').trigger('click')
      await flushPromises()
      expect(wrapper.find('[data-testid="defaults-success"]').exists()).toBe(true)
    })

    it('shows error alert when defaults save fails', async () => {
      const { useUpdateWorkspaceSettings } = await import('@/composables/useWorkspace')
      vi.mocked(useUpdateWorkspaceSettings).mockReturnValue({
        mutateAsync: vi.fn().mockRejectedValue(new Error('API error')),
        isPending: ref(false),
        isError: ref(false),
        reset: vi.fn(),
      } as unknown as ReturnType<typeof useUpdateWorkspaceSettings>)

      const wrapper = mountComponent()
      await wrapper.find('[data-testid="defaults-save-btn"]').trigger('click')
      await flushPromises()
      expect(wrapper.find('[data-testid="defaults-error"]').exists()).toBe(true)
    })
  })

  // ── Team stub ─────────────────────────────────────────────────────────────────
  describe('team section', () => {
    it('renders the team card', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="team-card"]').exists()).toBe(true)
    })

    it('shows the coming soon placeholder', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="team-coming-soon"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="team-coming-soon"]').text()).toContain('coming soon')
    })
  })

  // ── Danger zone ───────────────────────────────────────────────────────────────
  describe('danger zone', () => {
    it('renders the danger zone card', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="danger-zone-card"]').exists()).toBe(true)
    })

    it('renders the delete workspace button', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="delete-workspace-btn"]').exists()).toBe(true)
    })

    it('the delete dialog is initially closed', () => {
      const wrapper = mountComponent()
      const dialog = wrapper.findComponent({ name: 'DeleteWorkspaceDialog' })
      expect(dialog.exists()).toBe(true)
      expect(dialog.props('modelValue')).toBe(false)
    })

    it('opens the delete dialog when the delete button is clicked', async () => {
      const wrapper = mountComponent()
      await wrapper.find('[data-testid="delete-workspace-btn"]').trigger('click')
      await wrapper.vm.$nextTick()

      const dialog = wrapper.findComponent({ name: 'DeleteWorkspaceDialog' })
      expect(dialog.props('modelValue')).toBe(true)
    })
  })
})
