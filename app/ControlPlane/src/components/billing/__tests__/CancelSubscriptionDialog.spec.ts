import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import CancelSubscriptionDialog from '../CancelSubscriptionDialog.vue'

vi.mock('@/composables/useBilling', () => ({
  useCancelSubscription: vi.fn(),
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
  return mount(CancelSubscriptionDialog, {
    props: { modelValue },
    global: {
      plugins: [vuetify],
      stubs: { 'v-dialog': { template: '<div><slot /></div>' } },
    },
  })
}

describe('CancelSubscriptionDialog', () => {
  let mockMutateAsync: ReturnType<typeof vi.fn>

  beforeEach(async () => {
    vi.clearAllMocks()
    mockMutateAsync = vi.fn().mockResolvedValue(undefined)

    const { useCancelSubscription } = await import('@/composables/useBilling')
    vi.mocked(useCancelSubscription).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: { value: false },
      isError: { value: false },
      error: { value: null },
    } as unknown as ReturnType<typeof useCancelSubscription>)
  })

  it('renders the cancel subscription title', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('Cancel subscription')
  })

  it('renders the warning about losing access', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="cancel-warning"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="cancel-warning"]').text()).toContain('Free plan')
  })

  it('renders all cancel reason radio options', () => {
    const wrapper = mountComponent()
    const reasons = wrapper.find('[data-testid="cancel-reason"]')
    expect(reasons.exists()).toBe(true)
    expect(wrapper.find('[data-testid="reason-too_expensive"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="reason-missing_features"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="reason-switching_service"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="reason-not_using"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="reason-other"]').exists()).toBe(true)
  })

  it('renders the optional comment textarea', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="cancel-comment"]').exists()).toBe(true)
  })

  it('renders the submit button as disabled when no reason is selected', () => {
    const wrapper = mountComponent()
    const submitBtn = wrapper.find('[data-testid="cancel-submit-btn"]')
    expect(submitBtn.exists()).toBe(true)
    expect(submitBtn.attributes('disabled')).toBeDefined()
  })

  it('renders the keep-my-plan button', () => {
    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="cancel-close-btn"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="cancel-close-btn"]').text()).toContain('Keep my plan')
  })

  it('emits update:modelValue false when keep-my-plan is clicked', async () => {
    const wrapper = mountComponent()
    await wrapper.find('[data-testid="cancel-close-btn"]').trigger('click')
    expect(wrapper.emitted('update:modelValue')).toBeTruthy()
    expect(wrapper.emitted('update:modelValue')![0]).toEqual([false])
  })

  it('calls cancelSubscription mutation with selected reason on submit', async () => {
    const wrapper = mountComponent()

    // Select a reason via the v-radio-group model
    await wrapper.findComponent({ name: 'VRadioGroup' }).setValue('too_expensive')
    await wrapper.vm.$nextTick()

    await wrapper.find('[data-testid="cancel-submit-btn"]').trigger('click')
    await wrapper.vm.$nextTick()

    expect(mockMutateAsync).toHaveBeenCalledWith({
      reason: 'too_expensive',
      comment: undefined,
    })
  })

  it('includes comment in the mutation payload when provided', async () => {
    const wrapper = mountComponent()

    await wrapper.findComponent({ name: 'VRadioGroup' }).setValue('other')
    await wrapper.vm.$nextTick()

    const textarea = wrapper.find('[data-testid="cancel-comment"] textarea')
    if (textarea.exists()) {
      await textarea.setValue('Additional feedback here')
    }
    await wrapper.vm.$nextTick()

    await wrapper.find('[data-testid="cancel-submit-btn"]').trigger('click')
    await wrapper.vm.$nextTick()

    expect(mockMutateAsync).toHaveBeenCalledWith(expect.objectContaining({ reason: 'other' }))
  })

  it('shows an error alert when the mutation fails', async () => {
    const { useCancelSubscription } = await import('@/composables/useBilling')
    vi.mocked(useCancelSubscription).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: { value: false },
      isError: { value: true },
      error: { value: new Error('Server error') },
    } as unknown as ReturnType<typeof useCancelSubscription>)

    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="cancel-error"]').exists()).toBe(true)
  })
})
