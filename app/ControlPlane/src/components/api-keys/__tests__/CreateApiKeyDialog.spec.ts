import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { ref } from 'vue'
import CreateApiKeyDialog from '../CreateApiKeyDialog.vue'

vi.mock('@/composables/useApiKeys', () => ({
  useCreateApiKey: vi.fn(),
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

function mountComponent(modelValue = true) {
  return mount(CreateApiKeyDialog, {
    props: { modelValue },
    global: {
      plugins: [vuetify],
      stubs: { 'v-dialog': { template: '<div><slot /></div>' } },
    },
  })
}

const mockCreatedKey = {
  id: 'key-001',
  name: 'My Key',
  key: 'sk_abc123def456',
  keyPrefix: 'sk_abc123',
  createdAt: '2025-06-01T10:00:00Z',
  permissions: ['viewer'],
}

describe('CreateApiKeyDialog', () => {
  let mockMutateAsync: ReturnType<typeof vi.fn>

  beforeEach(async () => {
    vi.clearAllMocks()
    mockMutateAsync = vi.fn().mockResolvedValue(mockCreatedKey)

    const { useCreateApiKey } = await import('@/composables/useApiKeys')
    vi.mocked(useCreateApiKey).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: ref(false),
      isError: ref(false),
      error: ref(null),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useCreateApiKey>)
  })

  it('renders the dialog title', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('Create API key')
  })

  it('renders the key name input', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="key-name-input"]').exists()).toBe(true)
  })

  it('renders the role select', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="key-role-select"]').exists()).toBe(true)
  })

  it('renders cancel and create buttons', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="create-cancel-btn"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="create-submit-btn"]').exists()).toBe(true)
  })

  it('disables submit button when name is empty', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="create-submit-btn"]').attributes('disabled')).toBeDefined()
  })

  it('enables submit button when name is filled', async () => {
    const wrapper = mountComponent()
    const input = wrapper.find('[data-testid="key-name-input"]').find('input')
    await input.setValue('My Production Key')
    await wrapper.vm.$nextTick()

    expect(wrapper.find('[data-testid="create-submit-btn"]').attributes('disabled')).toBeUndefined()
  })

  it('shows name required error on empty submit', async () => {
    const wrapper = mountComponent()
    const input = wrapper.find('[data-testid="key-name-input"]').find('input')
    await input.setValue('')
    await input.trigger('blur')
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('Name is required')
  })

  it('shows name length error when exceeding 100 chars', async () => {
    const wrapper = mountComponent()
    const input = wrapper.find('[data-testid="key-name-input"]').find('input')
    await input.setValue('a'.repeat(101))
    await input.trigger('blur')
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('Name cannot exceed 100 characters')
  })

  it('calls mutateAsync with name and permissions on submit', async () => {
    const wrapper = mountComponent()
    const nameInput = wrapper.find('[data-testid="key-name-input"]').find('input')
    await nameInput.setValue('My Production Key')
    await nameInput.trigger('blur')
    await wrapper.vm.$nextTick()

    await wrapper.find('[data-testid="create-submit-btn"]').trigger('click')
    await wrapper.vm.$nextTick()

    expect(mockMutateAsync).toHaveBeenCalledWith({
      name: 'My Production Key',
      permissions: ['viewer'],
    })
  })

  it('emits created with the new key on success', async () => {
    const wrapper = mountComponent()
    const nameInput = wrapper.find('[data-testid="key-name-input"]').find('input')
    await nameInput.setValue('My Key')
    await nameInput.trigger('blur')
    await wrapper.vm.$nextTick()

    await wrapper.find('[data-testid="create-submit-btn"]').trigger('click')
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('created')).toBeTruthy()
    expect(wrapper.emitted('created')![0]).toEqual([mockCreatedKey])
  })

  it('closes on success', async () => {
    const wrapper = mountComponent()
    const nameInput = wrapper.find('[data-testid="key-name-input"]').find('input')
    await nameInput.setValue('My Key')
    await wrapper.vm.$nextTick()

    await wrapper.find('[data-testid="create-submit-btn"]').trigger('click')
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('update:modelValue')![0]).toEqual([false])
  })

  it('closes without creating when cancel is clicked', async () => {
    const wrapper = mountComponent()
    await wrapper.find('[data-testid="create-cancel-btn"]').trigger('click')
    expect(mockMutateAsync).not.toHaveBeenCalled()
    expect(wrapper.emitted('update:modelValue')![0]).toEqual([false])
  })

  it('shows error alert when creation fails', async () => {
    const { useCreateApiKey } = await import('@/composables/useApiKeys')
    vi.mocked(useCreateApiKey).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: ref(false),
      isError: ref(true),
      error: ref(new Error('Create failed')),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useCreateApiKey>)

    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="create-error"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="create-error"]').text()).toContain(
      'Failed to create API key'
    )
  })

  it('clears form on close', async () => {
    const wrapper = mountComponent()
    const nameInput = wrapper.find('[data-testid="key-name-input"]').find('input')
    await nameInput.setValue('My Key')
    await wrapper.vm.$nextTick()

    await wrapper.find('[data-testid="create-cancel-btn"]').trigger('click')
    await wrapper.vm.$nextTick()

    expect((nameInput.element as HTMLInputElement).value).toBe('')
  })
})
