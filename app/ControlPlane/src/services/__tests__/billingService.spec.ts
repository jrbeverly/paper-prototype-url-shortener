import { describe, it, expect } from 'vitest'
import { billingService, PLANS } from '../billingService'

describe('billingService', () => {
  describe('PLANS', () => {
    it('exports four plans: free, starter, pro, enterprise', () => {
      const ids = PLANS.map((p) => p.id)
      expect(ids).toEqual(['free', 'starter', 'pro', 'enterprise'])
    })

    it('each plan has a name, description, price, yearlyPrice, limits, and features', () => {
      for (const plan of PLANS) {
        expect(typeof plan.name).toBe('string')
        expect(typeof plan.description).toBe('string')
        expect(typeof plan.price).toBe('number')
        expect(typeof plan.yearlyPrice).toBe('number')
        expect(plan.limits).toBeDefined()
        expect(Array.isArray(plan.features)).toBe(true)
        expect(plan.features.length).toBeGreaterThan(0)
      }
    })

    it('free plan has a price of 0', () => {
      const free = PLANS.find((p) => p.id === 'free')!
      expect(free.price).toBe(0)
      expect(free.yearlyPrice).toBe(0)
    })

    it('pro plan is marked popular', () => {
      const pro = PLANS.find((p) => p.id === 'pro')!
      expect(pro.popular).toBe(true)
    })

    it('enterprise plan has null limits (unlimited)', () => {
      const enterprise = PLANS.find((p) => p.id === 'enterprise')!
      expect(enterprise.limits.links).toBeNull()
      expect(enterprise.limits.clicksPerMonth).toBeNull()
      expect(enterprise.limits.domains).toBeNull()
      expect(enterprise.limits.teamMembers).toBeNull()
    })
  })

  describe('getOverview', () => {
    it('returns a BillingOverview with subscription and usage', async () => {
      const overview = await billingService.getOverview()
      expect(overview.subscription).toBeDefined()
      expect(overview.usage).toBeDefined()
    })

    it('subscription has a valid planId', async () => {
      const { subscription } = await billingService.getOverview()
      expect(['free', 'starter', 'pro', 'enterprise']).toContain(subscription.planId)
    })

    it('subscription has a valid status', async () => {
      const { subscription } = await billingService.getOverview()
      expect(['active', 'trialing', 'past_due', 'canceled', 'unpaid']).toContain(
        subscription.status
      )
    })

    it('subscription has currentPeriodStart and currentPeriodEnd strings', async () => {
      const { subscription } = await billingService.getOverview()
      expect(typeof subscription.currentPeriodStart).toBe('string')
      expect(typeof subscription.currentPeriodEnd).toBe('string')
      expect(() => new Date(subscription.currentPeriodStart)).not.toThrow()
      expect(() => new Date(subscription.currentPeriodEnd)).not.toThrow()
    })

    it('usage has links, clicksThisMonth, domains, teamMembers', async () => {
      const { usage } = await billingService.getOverview()
      expect(usage.links).toBeDefined()
      expect(usage.clicksThisMonth).toBeDefined()
      expect(usage.domains).toBeDefined()
      expect(usage.teamMembers).toBeDefined()
    })

    it('each usage metric has used (number) and limit (number or null)', async () => {
      const { usage } = await billingService.getOverview()
      for (const metric of Object.values(usage)) {
        expect(typeof metric.used).toBe('number')
        expect(metric.limit === null || typeof metric.limit === 'number').toBe(true)
      }
    })
  })

  describe('getInvoices', () => {
    it('returns an array of invoices', async () => {
      const invoices = await billingService.getInvoices()
      expect(Array.isArray(invoices)).toBe(true)
    })

    it('each invoice has the required fields', async () => {
      const invoices = await billingService.getInvoices()
      for (const invoice of invoices) {
        expect(typeof invoice.id).toBe('string')
        expect(typeof invoice.number).toBe('string')
        expect(typeof invoice.date).toBe('string')
        expect(typeof invoice.amount).toBe('number')
        expect(typeof invoice.currency).toBe('string')
        expect(['paid', 'open', 'void']).toContain(invoice.status)
      }
    })

    it('each invoice has a downloadUrl that is a string or null', async () => {
      const invoices = await billingService.getInvoices()
      for (const invoice of invoices) {
        expect(invoice.downloadUrl === null || typeof invoice.downloadUrl === 'string').toBe(true)
      }
    })
  })

  describe('getPaymentMethods', () => {
    it('returns an array of payment methods', async () => {
      const methods = await billingService.getPaymentMethods()
      expect(Array.isArray(methods)).toBe(true)
    })

    it('each payment method has the required fields', async () => {
      const methods = await billingService.getPaymentMethods()
      for (const pm of methods) {
        expect(typeof pm.id).toBe('string')
        expect(typeof pm.brand).toBe('string')
        expect(typeof pm.last4).toBe('string')
        expect(pm.last4).toHaveLength(4)
        expect(typeof pm.expMonth).toBe('number')
        expect(typeof pm.expYear).toBe('number')
        expect(typeof pm.isDefault).toBe('boolean')
      }
    })

    it('has at most one default payment method', async () => {
      const methods = await billingService.getPaymentMethods()
      const defaults = methods.filter((pm) => pm.isDefault)
      expect(defaults.length).toBeLessThanOrEqual(1)
    })
  })

  describe('createCheckoutSession', () => {
    it('returns a session with a url string', async () => {
      const session = await billingService.createCheckoutSession('pro')
      expect(typeof session.url).toBe('string')
      expect(session.url.length).toBeGreaterThan(0)
    })

    it('includes the plan id in the url', async () => {
      const session = await billingService.createCheckoutSession('enterprise')
      expect(session.url).toContain('enterprise')
    })
  })

  describe('createBillingPortalSession', () => {
    it('returns a session with a url string', async () => {
      const session = await billingService.createBillingPortalSession()
      expect(typeof session.url).toBe('string')
      expect(session.url.length).toBeGreaterThan(0)
    })
  })

  describe('cancelSubscription', () => {
    it('resolves without error for a valid reason', async () => {
      await expect(
        billingService.cancelSubscription({ reason: 'too_expensive' })
      ).resolves.toBeUndefined()
    })

    it('resolves without error when a comment is provided', async () => {
      await expect(
        billingService.cancelSubscription({
          reason: 'other',
          comment: 'Some additional feedback',
        })
      ).resolves.toBeUndefined()
    })
  })
})
