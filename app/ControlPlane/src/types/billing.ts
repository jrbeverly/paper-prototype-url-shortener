export type PlanId = 'free' | 'starter' | 'pro' | 'enterprise'
export type SubscriptionStatus = 'active' | 'trialing' | 'past_due' | 'canceled' | 'unpaid'
export type InvoiceStatus = 'paid' | 'open' | 'void'
export type CancelReason =
  | 'too_expensive'
  | 'missing_features'
  | 'switching_service'
  | 'not_using'
  | 'other'

export interface PlanLimits {
  links: number | null
  clicksPerMonth: number | null
  domains: number | null
  teamMembers: number | null
}

export interface BillingPlan {
  id: PlanId
  name: string
  description: string
  price: number
  yearlyPrice: number
  limits: PlanLimits
  features: string[]
  popular?: boolean
}

export interface Subscription {
  planId: PlanId
  status: SubscriptionStatus
  currentPeriodStart: string
  currentPeriodEnd: string
  cancelAtPeriodEnd: boolean
  trialEnd?: string
}

export interface UsageMetrics {
  links: { used: number; limit: number | null }
  clicksThisMonth: { used: number; limit: number | null }
  domains: { used: number; limit: number | null }
  teamMembers: { used: number; limit: number | null }
}

export interface BillingOverview {
  subscription: Subscription
  usage: UsageMetrics
}

export interface Invoice {
  id: string
  number: string
  date: string
  amount: number
  currency: string
  status: InvoiceStatus
  downloadUrl: string | null
}

export interface PaymentMethod {
  id: string
  brand: string
  last4: string
  expMonth: number
  expYear: number
  isDefault: boolean
}

export interface CancelFeedback {
  reason: CancelReason
  comment?: string
}

export interface CheckoutSession {
  url: string
}

export interface BillingPortalSession {
  url: string
}
