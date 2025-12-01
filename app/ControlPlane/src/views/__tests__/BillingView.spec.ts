import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { defineComponent, ref } from 'vue'
import BillingView from '../BillingView.vue'

vi.mock('@/composables/useBilling', () => ({
  useBillingOverview: vi.fn(),
  useInvoices: vi.fn(),
  usePaymentMethods: vi.fn(),
  useManagePayment: vi.fn(),
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

const mockSubscription = {
  planId: 'starter' as const,
  status: 'active' as const,
  currentPeriodStart: '2025-06-15T12:00:00Z',
  currentPeriodEnd: '2025-07-15T12:00:00Z',
  cancelAtPeriodEnd: false,
}

const mockUsage = {
  links: { used: 312, limit: 500 },
  clicksThisMonth: { used: 43_891, limit: 50_000 },
  domains: { used: 2, limit: 3 },
  teamMembers: { used: 1, limit: 1 },
}

const mockOverview = { subscription: mockSubscription, usage: mockUsage }

const mockInvoices = [
  {
    id: 'inv_001',
    number: 'INV-2025-003',
    date: '2025-06-01T00:00:00Z',
    amount: 1900,
    currency: 'usd',
    status: 'paid' as const,
    downloadUrl: '/api/v1/billing/invoices/inv_001/download',
  },
  {
    id: 'inv_002',
    number: 'INV-2025-002',
    date: '2025-05-01T00:00:00Z',
    amount: 1900,
    currency: 'usd',
    status: 'paid' as const,
    downloadUrl: '/api/v1/billing/invoices/inv_002/download',
  },
]

const mockPaymentMethod = {
  id: 'pm_001',
  brand: 'visa',
  last4: '4242',
  expMonth: 12,
  expYear: 2027,
  isDefault: true,
}

const stubDialogComponent = (name: string) =>
  defineComponent({
    name,
    props: ['modelValue', 'currentPlanId'],
    emits: ['update:modelValue'],
    template: `<div :data-testid="'${name.toLowerCase()}-stub'" :data-visible="String(modelValue)"></div>`,
  })

function makeStubs() {
  return {
    PlanComparisonDialog: stubDialogComponent('PlanComparisonDialog'),
    CancelSubscriptionDialog: stubDialogComponent('CancelSubscriptionDialog'),
  }
}

function mountComponent() {
  return mount(BillingView, {
    global: {
      plugins: [vuetify],
      stubs: makeStubs(),
    },
  })
}

async function mockOverviewWith(override: object) {
  const { useBillingOverview } = await import('@/composables/useBilling')
  vi.mocked(useBillingOverview).mockReturnValue({
    data: ref({ ...mockOverview, ...override }),
    isLoading: ref(false),
    isError: ref(false),
    error: ref(null),
  } as ReturnType<typeof useBillingOverview>)
}

describe('BillingView', () => {
  let mockMutateAsync: ReturnType<typeof vi.fn>

  beforeEach(async () => {
    vi.clearAllMocks()
    mockMutateAsync = vi.fn().mockResolvedValue(undefined)

    const { useBillingOverview, useInvoices, usePaymentMethods, useManagePayment } =
      await import('@/composables/useBilling')

    vi.mocked(useBillingOverview).mockReturnValue({
      data: ref(mockOverview),
      isLoading: ref(false),
      isError: ref(false),
      error: ref(null),
    } as ReturnType<typeof useBillingOverview>)

    vi.mocked(useInvoices).mockReturnValue({
      data: ref(mockInvoices),
      isLoading: ref(false),
      isError: ref(false),
      error: ref(null),
    } as ReturnType<typeof useInvoices>)

    vi.mocked(usePaymentMethods).mockReturnValue({
      data: ref([mockPaymentMethod]),
      isLoading: ref(false),
      isError: ref(false),
      error: ref(null),
    } as ReturnType<typeof usePaymentMethods>)

    vi.mocked(useManagePayment).mockReturnValue({
      mutateAsync: mockMutateAsync,
      isPending: ref(false),
      isError: ref(false),
      error: ref(null),
    } as unknown as ReturnType<typeof useManagePayment>)
  })

  describe('loading state', () => {
    it('shows skeleton loader in plan card while loading', async () => {
      const { useBillingOverview } = await import('@/composables/useBilling')
      vi.mocked(useBillingOverview).mockReturnValue({
        data: ref(undefined),
        isLoading: ref(true),
        isError: ref(false),
        error: ref(null),
      } as ReturnType<typeof useBillingOverview>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="plan-card"]').exists()).toBe(true)
    })
  })

  describe('error state', () => {
    it('shows an error alert when overview request fails', async () => {
      const { useBillingOverview } = await import('@/composables/useBilling')
      vi.mocked(useBillingOverview).mockReturnValue({
        data: ref(undefined),
        isLoading: ref(false),
        isError: ref(true),
        error: ref(new Error('Request failed')),
      } as ReturnType<typeof useBillingOverview>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="overview-error"]').exists()).toBe(true)
    })
  })

  describe('current plan section', () => {
    it('displays the current plan name', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="plan-name"]').text()).toContain('Starter')
    })

    it('displays an active status chip', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="status-active"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="status-active"]').text()).toContain('Active')
    })

    it('displays the billing period start date', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="period-start"]').text()).toContain('Jun')
    })

    it('displays the billing period end date', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="period-end"]').text()).toContain('Jul')
    })

    it('renders the upgrade plan button', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="upgrade-plan-btn"]').exists()).toBe(true)
    })

    it('renders the cancel subscription button for active subscriptions', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="cancel-subscription-btn"]').exists()).toBe(true)
    })

    it('does not render the cancel button when subscription is already canceled', async () => {
      const { useBillingOverview } = await import('@/composables/useBilling')
      vi.mocked(useBillingOverview).mockReturnValue({
        data: ref({
          ...mockOverview,
          subscription: { ...mockSubscription, status: 'canceled' as const },
        }),
        isLoading: ref(false),
        isError: ref(false),
        error: ref(null),
      } as ReturnType<typeof useBillingOverview>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="cancel-subscription-btn"]').exists()).toBe(false)
    })

    it('does not render the cancel button when cancelAtPeriodEnd is true', async () => {
      const { useBillingOverview } = await import('@/composables/useBilling')
      vi.mocked(useBillingOverview).mockReturnValue({
        data: ref({
          ...mockOverview,
          subscription: { ...mockSubscription, cancelAtPeriodEnd: true },
        }),
        isLoading: ref(false),
        isError: ref(false),
        error: ref(null),
      } as ReturnType<typeof useBillingOverview>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="cancel-subscription-btn"]').exists()).toBe(false)
    })
  })

  describe('billing alerts', () => {
    it('shows a past due alert when status is past_due', async () => {
      await mockOverviewWith({
        subscription: { ...mockSubscription, status: 'past_due' as const },
      })
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="past-due-alert"]').exists()).toBe(true)
    })

    it('shows a past due alert when status is unpaid', async () => {
      await mockOverviewWith({
        subscription: { ...mockSubscription, status: 'unpaid' as const },
      })
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="past-due-alert"]').exists()).toBe(true)
    })

    it('shows a cancellation notice when cancelAtPeriodEnd is true', async () => {
      await mockOverviewWith({
        subscription: { ...mockSubscription, cancelAtPeriodEnd: true },
      })
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="cancel-scheduled-alert"]').exists()).toBe(true)
    })

    it('does not show billing alerts for an active subscription', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="past-due-alert"]').exists()).toBe(false)
      expect(wrapper.find('[data-testid="cancel-scheduled-alert"]').exists()).toBe(false)
    })
  })

  describe('usage section', () => {
    it('shows links usage', () => {
      const wrapper = mountComponent()
      const linksCol = wrapper.find('[data-testid="usage-links"]')
      expect(linksCol.exists()).toBe(true)
      expect(linksCol.text()).toContain('312')
      expect(linksCol.text()).toContain('500')
    })

    it('shows clicks usage', () => {
      const wrapper = mountComponent()
      const clicksCol = wrapper.find('[data-testid="usage-clicks"]')
      expect(clicksCol.exists()).toBe(true)
      expect(clicksCol.text()).toContain('43,891')
    })

    it('shows domains usage', () => {
      const wrapper = mountComponent()
      const domainsCol = wrapper.find('[data-testid="usage-domains"]')
      expect(domainsCol.exists()).toBe(true)
      expect(domainsCol.text()).toContain('2')
      expect(domainsCol.text()).toContain('3')
    })

    it('shows "Unlimited" for unlimited metrics', async () => {
      const { useBillingOverview } = await import('@/composables/useBilling')
      vi.mocked(useBillingOverview).mockReturnValue({
        data: ref({
          ...mockOverview,
          usage: { ...mockUsage, links: { used: 150, limit: null } },
        }),
        isLoading: ref(false),
        isError: ref(false),
        error: ref(null),
      } as ReturnType<typeof useBillingOverview>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="usage-links"]').text()).toContain('Unlimited')
    })
  })

  describe('payment method section', () => {
    it('shows the card brand', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="card-brand"]').text()).toContain('Visa')
    })

    it('shows the last 4 digits', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="card-last4"]').text()).toContain('4242')
    })

    it('shows the expiry date', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="card-expiry"]').text()).toContain('12/27')
    })

    it('renders the update payment button', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="update-payment-btn"]').exists()).toBe(true)
    })

    it('calls managePayment.mutateAsync when update payment is clicked', async () => {
      const wrapper = mountComponent()
      await wrapper.find('[data-testid="update-payment-btn"]').trigger('click')
      expect(mockMutateAsync).toHaveBeenCalled()
    })
  })

  describe('invoice history', () => {
    it('renders the invoice table', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="invoices-table"]').exists()).toBe(true)
    })

    it('shows each invoice number', () => {
      const wrapper = mountComponent()
      expect(wrapper.text()).toContain('INV-2025-003')
      expect(wrapper.text()).toContain('INV-2025-002')
    })

    it('shows invoice amounts formatted as currency', () => {
      const wrapper = mountComponent()
      expect(wrapper.text()).toContain('$19.00')
    })

    it('shows a download button for each invoice with a downloadUrl', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="invoice-download-inv_001"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="invoice-download-inv_002"]').exists()).toBe(true)
    })

    it('shows "No invoices" when the list is empty', async () => {
      const { useInvoices } = await import('@/composables/useBilling')
      vi.mocked(useInvoices).mockReturnValue({
        data: ref([]),
        isLoading: ref(false),
        isError: ref(false),
        error: ref(null),
      } as ReturnType<typeof useInvoices>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="no-invoices"]').exists()).toBe(true)
    })
  })

  describe('dialog interactions', () => {
    it('opens the plan comparison dialog when upgrade plan is clicked', async () => {
      const wrapper = mountComponent()
      const stub = wrapper.find('[data-testid="plancomparisondialog-stub"]')
      expect(stub.attributes('data-visible')).toBe('false')

      await wrapper.find('[data-testid="upgrade-plan-btn"]').trigger('click')
      await wrapper.vm.$nextTick()

      expect(stub.attributes('data-visible')).toBe('true')
    })

    it('opens the cancel dialog when cancel subscription is clicked', async () => {
      const wrapper = mountComponent()
      const stub = wrapper.find('[data-testid="cancelsubscriptiondialog-stub"]')
      expect(stub.attributes('data-visible')).toBe('false')

      await wrapper.find('[data-testid="cancel-subscription-btn"]').trigger('click')
      await wrapper.vm.$nextTick()

      expect(stub.attributes('data-visible')).toBe('true')
    })
  })
})
