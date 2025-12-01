// TODO: Replace mock implementations with apiClient calls once billing API endpoints are defined.
// All functions are shaped to match the expected API contract so the switch-over is a one-line change per function.

import type {
  BillingOverview,
  BillingPlan,
  BillingPortalSession,
  CancelFeedback,
  CheckoutSession,
  Invoice,
  PaymentMethod,
} from '@/types/billing'

export const PLANS: BillingPlan[] = [
  {
    id: 'free',
    name: 'Free',
    description: 'Get started with URL shortening',
    price: 0,
    yearlyPrice: 0,
    limits: { links: 10, clicksPerMonth: 1_000, domains: 1, teamMembers: 1 },
    features: [
      '10 shortened links',
      '1,000 clicks per month',
      '1 custom domain',
      'Basic click analytics',
      '90-day link expiry',
    ],
  },
  {
    id: 'starter',
    name: 'Starter',
    description: 'For individuals and small projects',
    price: 19,
    yearlyPrice: 15,
    limits: { links: 500, clicksPerMonth: 50_000, domains: 3, teamMembers: 1 },
    features: [
      '500 shortened links',
      '50,000 clicks per month',
      '3 custom domains',
      'Custom slugs',
      'Advanced analytics',
      'SSL certificates',
      'API access',
      'No link expiry',
    ],
  },
  {
    id: 'pro',
    name: 'Pro',
    description: 'For growing businesses and teams',
    price: 49,
    yearlyPrice: 39,
    limits: { links: null, clicksPerMonth: 500_000, domains: 10, teamMembers: 5 },
    features: [
      'Unlimited shortened links',
      '500,000 clicks per month',
      '10 custom domains',
      'Everything in Starter',
      'Team collaboration (5 seats)',
      'Priority support',
      'Webhooks',
      'Bulk link creation',
    ],
    popular: true,
  },
  {
    id: 'enterprise',
    name: 'Enterprise',
    description: 'For large organisations with custom needs',
    price: 149,
    yearlyPrice: 119,
    limits: { links: null, clicksPerMonth: null, domains: null, teamMembers: null },
    features: [
      'Unlimited shortened links',
      'Unlimited clicks',
      'Unlimited custom domains',
      'Everything in Pro',
      'Unlimited team members',
      'SSO / SAML',
      'Dedicated support',
      'Custom integrations',
      'SLA guarantee',
    ],
  },
]

export const billingService = {
  async getOverview(): Promise<BillingOverview> {
    return {
      subscription: {
        planId: 'starter',
        status: 'active',
        currentPeriodStart: '2025-06-01T00:00:00Z',
        currentPeriodEnd: '2025-07-01T00:00:00Z',
        cancelAtPeriodEnd: false,
      },
      usage: {
        links: { used: 312, limit: 500 },
        clicksThisMonth: { used: 43_891, limit: 50_000 },
        domains: { used: 2, limit: 3 },
        teamMembers: { used: 1, limit: 1 },
      },
    }
  },

  async getInvoices(): Promise<Invoice[]> {
    return [
      {
        id: 'inv_001',
        number: 'INV-2025-003',
        date: '2025-06-01T00:00:00Z',
        amount: 1900,
        currency: 'usd',
        status: 'paid',
        downloadUrl: '/api/v1/billing/invoices/inv_001/download',
      },
      {
        id: 'inv_002',
        number: 'INV-2025-002',
        date: '2025-05-01T00:00:00Z',
        amount: 1900,
        currency: 'usd',
        status: 'paid',
        downloadUrl: '/api/v1/billing/invoices/inv_002/download',
      },
      {
        id: 'inv_003',
        number: 'INV-2025-001',
        date: '2025-04-01T00:00:00Z',
        amount: 1900,
        currency: 'usd',
        status: 'paid',
        downloadUrl: '/api/v1/billing/invoices/inv_003/download',
      },
    ]
  },

  async getPaymentMethods(): Promise<PaymentMethod[]> {
    return [
      {
        id: 'pm_001',
        brand: 'visa',
        last4: '4242',
        expMonth: 12,
        expYear: 2027,
        isDefault: true,
      },
    ]
  },

  async createCheckoutSession(planId: string): Promise<CheckoutSession> {
    // TODO: apiClient.POST('/api/v1/billing/checkout', { body: { planId } })
    return { url: `https://checkout.stripe.com/pay/cs_test_mock?plan=${planId}` }
  },

  async createBillingPortalSession(): Promise<BillingPortalSession> {
    // TODO: apiClient.POST('/api/v1/billing/portal')
    return { url: 'https://billing.stripe.com/session/test_mock' }
  },

  async cancelSubscription(feedback: CancelFeedback): Promise<void> {
    // TODO: apiClient.POST('/api/v1/billing/cancel', { body: feedback })
    void feedback
  },
}
