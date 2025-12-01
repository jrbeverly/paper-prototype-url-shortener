import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { ref } from 'vue'
import RevokeApiKeyDialog from '../RevokeApiKeyDialog.vue'

vi.mock('@/composables/useApiKeys', () => ({
  useRevokeApiKey: vi.fn(),
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

const mockApiKey = {
  id: 'key-001',
  name: 'Production Key',
  keyPreview: 'sk_abc12345****...****5678',
  keyPrefix: 'sk_abc12345',
  createdAt: '2025-05-01T10:00:00Z',
  lastUsedAt: '2025-06-01T14:30:00Z',
  permissions: ['viewer'],
  isRevoked: false,
}

function mountComponent(modelValue = true) {
  return mount(RevokeApiKeyDialog, {
    props: { modelValue, apiKey: mockApiKey },
    global: {
      plugins: [vuetify],
      stubs: { 'v-dialog': { template: '<div><slot /></div>' } },
    },
  })
}

describe('RevokeApiKeyDialog', () => {
  let mockMutateAsync: ReturnType<typeof vi.fn>

  beforeEach(async () => {
    vi.clearAllMocks()
    mockMutateAsync = vi.fn().mockResolvedValue(undefined)

    const { useRevokeApiKey } = await import('@/composables/useApiKeys')
    vi.mocked(useRevokeApiKey).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: ref(false),
      isError: ref(false),
      error: ref(null),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useRevokeApiKey>)
  })

  it('renders the dialog title', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('Revoke API key')
  })

  it('shows the key name in the confirmation message', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('Production Key')
  })

  it('warns the action is immediate and irreversible', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="revoke-warning"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('immediate and cannot be undone')
  })

  it('renders cancel and revoke buttons', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="revoke-cancel-btn"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="revoke-confirm-btn"]').exists()).toBe(true)
  })

  it('calls mutateAsync with the key id on revoke', async () => {
    const wrapper = mountComponent()
    await wrapper.find('[data-testid="revoke-confirm-btn"]').trigger('click')
    expect(mockMutateAsync).toHaveBeenCalledWith('key-001')
  })

  it('closes on successful revoke', async () => {
    const wrapper = mountComponent()
    await wrapper.find('[data-testid="revoke-confirm-btn"]').trigger('click')
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('update:modelValue')![0]).toEqual([false])
  })

  it('closes without revoking when cancel is clicked', async () => {
    const wrapper = mountComponent()
    await wrapper.find('[data-testid="revoke-cancel-btn"]').trigger('click')
    expect(mockMutateAsync).not.toHaveBeenCalled()
    expect(wrapper.emitted('update:modelValue')![0]).toEqual([false])
  })

  it('shows error alert when revoke fails', async () => {
    const { useRevokeApiKey } = await import('@/composables/useApiKeys')
    vi.mocked(useRevokeApiKey).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: ref(false),
      isError: ref(true),
      error: ref(new Error('Revoke failed')),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useRevokeApiKey>)

    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="revoke-error"]').exists()).toBe(true)
  })
})
