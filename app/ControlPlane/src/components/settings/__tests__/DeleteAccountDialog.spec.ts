import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { ref } from 'vue'
import DeleteAccountDialog from '../DeleteAccountDialog.vue'

vi.mock('@/composables/useSettings', () => ({
  useDeleteAccount: vi.fn(),
}))

vi.mock('@/services/authService', () => ({
  logout: vi.fn().mockResolvedValue(undefined),
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

function makeMutation(overrides: Record<string, unknown> = {}) {
  return {
    mutate: vi.fn(),
    mutateAsync: vi.fn().mockResolvedValue(undefined),
    isPending: ref(false),
    isError: ref(false),
    error: ref(null),
    reset: vi.fn(),
    ...overrides,
  }
}

function mountComponent(modelValue = true) {
  return mount(DeleteAccountDialog, {
    props: { modelValue },
    global: {
      plugins: [vuetify],
      stubs: { 'v-dialog': { template: '<div><slot /></div>' } },
    },
  })
}

describe('DeleteAccountDialog', () => {
  beforeEach(async () => {
    vi.clearAllMocks()
    const { useDeleteAccount } = await import('@/composables/useSettings')
    vi.mocked(useDeleteAccount).mockReturnValue(
      makeMutation() as unknown as ReturnType<typeof useDeleteAccount>
    )
  })

  it('renders the dialog title', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('Delete account')
  })

  it('shows a warning about permanent deletion', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="delete-warning"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="delete-warning"]').text()).toContain('cannot be undone')
  })

  it('renders the confirm text input', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="delete-confirm-input"]').exists()).toBe(true)
  })

  it('renders the cancel button', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="delete-cancel-btn"]').exists()).toBe(true)
  })

  it('renders the confirm button', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="delete-confirm-btn"]').exists()).toBe(true)
  })

  it('disables the confirm button when the input is empty', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="delete-confirm-btn"]').attributes('disabled')).toBeDefined()
  })

  it('disables the confirm button when text does not match DELETE', async () => {
    const wrapper = mountComponent()
    await wrapper.find('[data-testid="delete-confirm-input"] input').setValue('delete')
    await wrapper.vm.$nextTick()
    expect(wrapper.find('[data-testid="delete-confirm-btn"]').attributes('disabled')).toBeDefined()
  })

  it('disables the confirm button when text is partially correct', async () => {
    const wrapper = mountComponent()
    await wrapper.find('[data-testid="delete-confirm-input"] input').setValue('DELET')
    await wrapper.vm.$nextTick()
    expect(wrapper.find('[data-testid="delete-confirm-btn"]').attributes('disabled')).toBeDefined()
  })

  it('enables the confirm button when DELETE is typed exactly', async () => {
    const wrapper = mountComponent()
    await wrapper.find('[data-testid="delete-confirm-input"] input').setValue('DELETE')
    await wrapper.vm.$nextTick()
    expect(
      wrapper.find('[data-testid="delete-confirm-btn"]').attributes('disabled')
    ).toBeUndefined()
  })

  it('calls mutateAsync when the confirm button is clicked', async () => {
    const { useDeleteAccount } = await import('@/composables/useSettings')
    const mutateAsync = vi.fn().mockResolvedValue(undefined)
    vi.mocked(useDeleteAccount).mockReturnValue(
      makeMutation({ mutateAsync }) as unknown as ReturnType<typeof useDeleteAccount>
    )

    const wrapper = mountComponent()
    await wrapper.find('[data-testid="delete-confirm-input"] input').setValue('DELETE')
    await wrapper.vm.$nextTick()
    await wrapper.find('[data-testid="delete-confirm-btn"]').trigger('click')
    await wrapper.vm.$nextTick()

    expect(mutateAsync).toHaveBeenCalled()
  })

  it('calls logout after successful deletion', async () => {
    const { useDeleteAccount } = await import('@/composables/useSettings')
    vi.mocked(useDeleteAccount).mockReturnValue(
      makeMutation() as unknown as ReturnType<typeof useDeleteAccount>
    )
    const { logout } = await import('@/services/authService')

    const wrapper = mountComponent()
    await wrapper.find('[data-testid="delete-confirm-input"] input').setValue('DELETE')
    await wrapper.vm.$nextTick()
    await wrapper.find('[data-testid="delete-confirm-btn"]').trigger('click')
    await wrapper.vm.$nextTick()

    expect(logout).toHaveBeenCalled()
  })

  it('shows an error alert on deletion failure', async () => {
    const { useDeleteAccount } = await import('@/composables/useSettings')
    vi.mocked(useDeleteAccount).mockReturnValue(
      makeMutation({ isError: ref(true) }) as unknown as ReturnType<typeof useDeleteAccount>
    )

    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="delete-error"]').exists()).toBe(true)
  })

  it('emits update:modelValue false when cancel is clicked', async () => {
    const wrapper = mountComponent()
    await wrapper.find('[data-testid="delete-cancel-btn"]').trigger('click')
    expect(wrapper.emitted('update:modelValue')![0]).toEqual([false])
  })
})
