import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { defineComponent, ref } from 'vue'
import ApiKeysView from '../ApiKeysView.vue'

vi.mock('@/composables/useApiKeys', () => ({
  useApiKeys: vi.fn(),
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

const mockKeys = [
  {
    id: 'key-001',
    name: 'Production',
    keyPreview: 'sk_abc12345****...****5678',
    keyPrefix: 'sk_abc12345',
    createdAt: '2025-05-01T10:00:00Z',
    lastUsedAt: '2025-06-01T14:30:00Z',
    permissions: ['viewer'],
    isRevoked: false,
  },
  {
    id: 'key-002',
    name: 'Development',
    keyPreview: 'sk_xyz98765****...****4321',
    keyPrefix: 'sk_xyz98765',
    createdAt: '2025-04-15T09:00:00Z',
    lastUsedAt: null,
    permissions: ['member'],
    isRevoked: false,
  },
  {
    id: 'key-003',
    name: 'Legacy CI',
    keyPreview: 'sk_old11111****...****9999',
    keyPrefix: 'sk_old11111',
    createdAt: '2025-01-01T00:00:00Z',
    lastUsedAt: '2025-03-10T08:00:00Z',
    permissions: ['viewer'],
    isRevoked: true,
  },
]

const stubDialog = (name: string) =>
  defineComponent({
    name,
    props: ['modelValue', 'apiKey', 'createdKey'],
    emits: ['update:modelValue', 'created'],
    template: `<div :data-testid="'${name.toLowerCase()}-stub'" :data-visible="String(modelValue ?? false)"></div>`,
  })

function makeStubs() {
  return {
    CreateApiKeyDialog: stubDialog('CreateApiKeyDialog'),
    KeyRevealDialog: stubDialog('KeyRevealDialog'),
    RevokeApiKeyDialog: stubDialog('RevokeApiKeyDialog'),
  }
}

function mountComponent() {
  return mount(ApiKeysView, {
    global: {
      plugins: [vuetify],
      stubs: makeStubs(),
    },
  })
}

describe('ApiKeysView', () => {
  beforeEach(async () => {
    vi.clearAllMocks()

    const { useApiKeys } = await import('@/composables/useApiKeys')
    vi.mocked(useApiKeys).mockReturnValue({
      data: ref(mockKeys),
      isLoading: ref(false),
      isError: ref(false),
      isFetching: ref(false),
      error: ref(null),
    } as ReturnType<typeof useApiKeys>)
  })

  it('renders the page title', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('API Keys')
  })

  it('renders the create API key button', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="create-key-btn"]').exists()).toBe(true)
  })

  describe('keys table', () => {
    it('renders the keys table', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="keys-table"]').exists()).toBe(true)
    })

    it('renders a row for each key', () => {
      const wrapper = mountComponent()
      for (const key of mockKeys) {
        expect(wrapper.find(`[data-testid="key-row-${key.id}"]`).exists()).toBe(true)
      }
    })

    it('shows the key name for each key', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="key-name-key-001"]').text()).toBe('Production')
      expect(wrapper.find('[data-testid="key-name-key-002"]').text()).toBe('Development')
      expect(wrapper.find('[data-testid="key-name-key-003"]').text()).toBe('Legacy CI')
    })

    it('shows the key preview for each key', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="key-preview-key-001"]').text()).toBe(
        'sk_abc12345****...****5678'
      )
    })

    it('shows the role label for each key', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="key-role-key-001"]').text()).toBe('Viewer')
      expect(wrapper.find('[data-testid="key-role-key-002"]').text()).toBe('Member')
    })

    it('shows "Active" status chip for active keys', () => {
      const wrapper = mountComponent()
      const chip = wrapper.find('[data-testid="key-status-chip-key-001"]')
      expect(chip.text()).toContain('Active')
    })

    it('shows "Revoked" status chip for revoked keys', () => {
      const wrapper = mountComponent()
      const chip = wrapper.find('[data-testid="key-status-chip-key-003"]')
      expect(chip.text()).toContain('Revoked')
    })

    it('shows revoke button for non-revoked keys', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="revoke-btn-key-001"]').exists()).toBe(true)
    })

    it('does not show revoke button for revoked keys', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="revoke-btn-key-003"]').exists()).toBe(false)
    })

    it('shows "Never" for keys that have never been used', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="key-last-used-key-002"]').text()).toBe('Never')
    })
  })

  describe('empty state', () => {
    it('shows empty state when there are no keys', async () => {
      const { useApiKeys } = await import('@/composables/useApiKeys')
      vi.mocked(useApiKeys).mockReturnValue({
        data: ref([]),
        isLoading: ref(false),
        isError: ref(false),
        isFetching: ref(false),
        error: ref(null),
      } as ReturnType<typeof useApiKeys>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="empty-state"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="empty-state"]').text()).toContain('No API keys yet')
    })

    it('shows a create button in the empty state', async () => {
      const { useApiKeys } = await import('@/composables/useApiKeys')
      vi.mocked(useApiKeys).mockReturnValue({
        data: ref([]),
        isLoading: ref(false),
        isError: ref(false),
        isFetching: ref(false),
        error: ref(null),
      } as ReturnType<typeof useApiKeys>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="empty-create-btn"]').exists()).toBe(true)
    })
  })

  describe('loading state', () => {
    it('shows skeleton loaders while loading', async () => {
      const { useApiKeys } = await import('@/composables/useApiKeys')
      vi.mocked(useApiKeys).mockReturnValue({
        data: ref(undefined),
        isLoading: ref(true),
        isError: ref(false),
        isFetching: ref(false),
        error: ref(null),
      } as ReturnType<typeof useApiKeys>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="keys-card"]').exists()).toBe(true)
    })

    it('disables the create button while loading', async () => {
      const { useApiKeys } = await import('@/composables/useApiKeys')
      vi.mocked(useApiKeys).mockReturnValue({
        data: ref(undefined),
        isLoading: ref(true),
        isError: ref(false),
        isFetching: ref(false),
        error: ref(null),
      } as ReturnType<typeof useApiKeys>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="create-key-btn"]').attributes('disabled')).toBeDefined()
    })
  })

  describe('error state', () => {
    it('shows an error alert when the request fails', async () => {
      const { useApiKeys } = await import('@/composables/useApiKeys')
      vi.mocked(useApiKeys).mockReturnValue({
        data: ref(undefined),
        isLoading: ref(false),
        isError: ref(true),
        isFetching: ref(false),
        error: ref(new Error('Request failed')),
      } as ReturnType<typeof useApiKeys>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="list-error"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="list-error"]').text()).toContain('Failed to load API keys')
    })
  })

  describe('create key dialog', () => {
    it('opens the create dialog when the button is clicked', async () => {
      const wrapper = mountComponent()
      const stub = wrapper.find('[data-testid="createapikeydialog-stub"]')
      expect(stub.attributes('data-visible')).toBe('false')

      await wrapper.find('[data-testid="create-key-btn"]').trigger('click')
      await wrapper.vm.$nextTick()

      expect(stub.attributes('data-visible')).toBe('true')
    })
  })

  describe('revoke dialog', () => {
    it('opens the revoke dialog when the revoke button is clicked', async () => {
      const wrapper = mountComponent()

      await wrapper.find('[data-testid="revoke-btn-key-001"]').trigger('click')
      await wrapper.vm.$nextTick()

      const stub = wrapper.find('[data-testid="revokeapikeydialog-stub"]')
      expect(stub.exists()).toBe(true)
      expect(stub.attributes('data-visible')).toBe('true')
    })
  })
})
