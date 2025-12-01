import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { ref } from 'vue'
import PlanComparisonDialog from '../PlanComparisonDialog.vue'
import { PLANS } from '@/services/billingService'

vi.mock('@/composables/useBilling', () => ({
  usePlans: vi.fn(),
  useCreateCheckout: vi.fn(),
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

function mountComponent(currentPlanId = 'starter', modelValue = true) {
  return mount(PlanComparisonDialog, {
    props: { modelValue, currentPlanId },
    global: {
      plugins: [vuetify],
      stubs: { 'v-dialog': { template: '<div><slot /></div>' } },
    },
  })
}

describe('PlanComparisonDialog', () => {
  let mockMutateAsync: ReturnType<typeof vi.fn>

  beforeEach(async () => {
    vi.clearAllMocks()
    mockMutateAsync = vi.fn().mockResolvedValue(undefined)

    const { usePlans, useCreateCheckout } = await import('@/composables/useBilling')

    vi.mocked(usePlans).mockReturnValue({
      data: ref(PLANS),
      isLoading: ref(false),
      isError: ref(false),
      error: ref(null),
    } as ReturnType<typeof usePlans>)

    vi.mocked(useCreateCheckout).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: { value: false },
      isError: { value: false },
      error: { value: null },
      variables: { value: undefined },
    } as unknown as ReturnType<typeof useCreateCheckout>)
  })

  it('renders the dialog title', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('Choose a plan')
  })

  it('renders a card for each plan', () => {
    const wrapper = mountComponent()
    for (const plan of PLANS) {
      expect(wrapper.find(`[data-testid="plan-card-${plan.id}"]`).exists()).toBe(true)
    }
  })

  it('renders all plan names', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('Free')
    expect(wrapper.text()).toContain('Starter')
    expect(wrapper.text()).toContain('Pro')
    expect(wrapper.text()).toContain('Enterprise')
  })

  it('renders plan prices', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('$19')
    expect(wrapper.text()).toContain('$49')
    expect(wrapper.text()).toContain('$149')
  })

  it('marks the pro plan as popular', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('Popular')
  })

  it('shows "Current plan" on the select button for the current plan', () => {
    const wrapper = mountComponent('starter')
    const btn = wrapper.find('[data-testid="plan-select-starter"]')
    expect(btn.text()).toContain('Current plan')
  })

  it('disables the select button for the current plan', () => {
    const wrapper = mountComponent('starter')
    const btn = wrapper.find('[data-testid="plan-select-starter"]')
    expect(btn.attributes('disabled')).toBeDefined()
  })

  it('shows "Upgrade" label for plans above the current plan', () => {
    const wrapper = mountComponent('starter')
    const proBtn = wrapper.find('[data-testid="plan-select-pro"]')
    expect(proBtn.text()).toContain('Upgrade')
  })

  it('shows "Downgrade" label for plans below the current plan', () => {
    const wrapper = mountComponent('pro')
    const starterBtn = wrapper.find('[data-testid="plan-select-starter"]')
    expect(starterBtn.text()).toContain('Downgrade')
  })

  it('calls checkout mutation with the plan id when upgrade is clicked', async () => {
    const wrapper = mountComponent('starter')
    await wrapper.find('[data-testid="plan-select-pro"]').trigger('click')
    expect(mockMutateAsync).toHaveBeenCalledWith('pro')
  })

  it('does not call checkout mutation when current plan button is clicked', async () => {
    const wrapper = mountComponent('starter')
    await wrapper.find('[data-testid="plan-select-starter"]').trigger('click')
    expect(mockMutateAsync).not.toHaveBeenCalled()
  })

  it('shows an error alert when checkout fails', async () => {
    const { useCreateCheckout } = await import('@/composables/useBilling')
    vi.mocked(useCreateCheckout).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: { value: false },
      isError: { value: true },
      error: { value: new Error('Checkout error') },
      variables: { value: undefined },
    } as unknown as ReturnType<typeof useCreateCheckout>)

    const wrapper = mountComponent()
    expect(wrapper.find('[data-testid="checkout-error"]').exists()).toBe(true)
  })

  it('emits update:modelValue false when close is clicked', async () => {
    const wrapper = mountComponent()
    const closeBtn = wrapper.findAll('button').find((b) => b.text().includes('Close'))
    expect(closeBtn).toBeDefined()
    await closeBtn!.trigger('click')
    expect(wrapper.emitted('update:modelValue')).toBeTruthy()
    expect(wrapper.emitted('update:modelValue')![0]).toEqual([false])
  })
})
