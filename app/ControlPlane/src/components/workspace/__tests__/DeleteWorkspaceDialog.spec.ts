import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { ref } from 'vue'
import DeleteWorkspaceDialog from '../DeleteWorkspaceDialog.vue'

vi.mock('@/composables/useWorkspace', () => ({
  useDeleteWorkspace: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRouter: vi.fn().mockReturnValue({ push: vi.fn() }),
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

const mockWorkspace = {
  id: 'ws-001',
  name: 'Acme Corp',
  slug: 'acme-corp',
  branding: { primaryColor: '#1867C0', logoUrl: null },
  defaults: { fallback404Url: null, defaultRedirectType: '302' as const },
}

function mountComponent(modelValue = true) {
  return mount(DeleteWorkspaceDialog, {
    props: { modelValue, workspace: mockWorkspace },
    global: {
      plugins: [vuetify],
      stubs: { 'v-dialog': { template: '<div><slot /></div>' } },
    },
  })
}

describe('DeleteWorkspaceDialog', () => {
  let mockMutateAsync: ReturnType<typeof vi.fn>

  beforeEach(async () => {
    vi.clearAllMocks()
    mockMutateAsync = vi.fn().mockResolvedValue(undefined)

    const { useDeleteWorkspace } = await import('@/composables/useWorkspace')
    vi.mocked(useDeleteWorkspace).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: ref(false),
      isError: ref(false),
      error: ref(null),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useDeleteWorkspace>)
  })

  it('renders the dialog title', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('Delete workspace')
  })

  it('shows the workspace name in the confirmation message', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="delete-workspace-name"]').text()).toBe('Acme Corp')
  })

  it('warns the action cannot be undone', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('cannot be undone')
  })

  it('renders cancel and delete buttons', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="delete-cancel-btn"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="delete-confirm-btn"]').exists()).toBe(true)
  })

  it('disables the confirm button when name input does not match', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="delete-confirm-btn"]').attributes('disabled')).toBeDefined()
  })

  it('enables the confirm button when name matches exactly', async () => {
    const wrapper = mountComponent()
    const input = wrapper.find('[data-testid="confirm-name-input"] input')
    await input.setValue('Acme Corp')
    await wrapper.vm.$nextTick()
    expect(
      wrapper.find('[data-testid="delete-confirm-btn"]').attributes('disabled')
    ).toBeUndefined()
  })

  it('does not call mutateAsync when name does not match', async () => {
    const wrapper = mountComponent()
    const input = wrapper.find('[data-testid="confirm-name-input"] input')
    await input.setValue('wrong name')
    await wrapper.find('[data-testid="delete-confirm-btn"]').trigger('click')
    expect(mockMutateAsync).not.toHaveBeenCalled()
  })

  it('calls mutateAsync with workspace id when name matches and confirm is clicked', async () => {
    const wrapper = mountComponent()
    const input = wrapper.find('[data-testid="confirm-name-input"] input')
    await input.setValue('Acme Corp')
    await wrapper.vm.$nextTick()
    await wrapper.find('[data-testid="delete-confirm-btn"]').trigger('click')
    expect(mockMutateAsync).toHaveBeenCalledWith('ws-001')
  })

  it('emits deleted on successful deletion', async () => {
    const wrapper = mountComponent()
    const input = wrapper.find('[data-testid="confirm-name-input"] input')
    await input.setValue('Acme Corp')
    await wrapper.vm.$nextTick()
    await wrapper.find('[data-testid="delete-confirm-btn"]').trigger('click')
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('deleted')).toBeTruthy()
  })

  it('closes without deleting when cancel is clicked', async () => {
    const wrapper = mountComponent()
    await wrapper.find('[data-testid="delete-cancel-btn"]').trigger('click')
    expect(mockMutateAsync).not.toHaveBeenCalled()
    expect(wrapper.emitted('update:modelValue')![0]).toEqual([false])
  })

  it('disables cancel button while deletion is pending', async () => {
    const { useDeleteWorkspace } = await import('@/composables/useWorkspace')
    vi.mocked(useDeleteWorkspace).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: ref(true),
      isError: ref(false),
      error: ref(null),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useDeleteWorkspace>)

    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="delete-cancel-btn"]').attributes('disabled')).toBeDefined()
  })

  it('shows error alert when deletion fails', async () => {
    const { useDeleteWorkspace } = await import('@/composables/useWorkspace')
    vi.mocked(useDeleteWorkspace).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: ref(false),
      isError: ref(true),
      error: ref(new Error('Delete failed')),
      reset: vi.fn(),
    } as unknown as ReturnType<typeof useDeleteWorkspace>)

    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="delete-error"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="delete-error"]').text()).toContain(
      'Failed to delete workspace'
    )
  })
})
