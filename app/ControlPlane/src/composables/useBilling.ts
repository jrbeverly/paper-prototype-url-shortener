import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { PLANS, billingService } from '@/services/billingService'
import type { CancelFeedback } from '@/types/billing'

export function useBillingOverview() {
  return useQuery({
    queryKey: ['billing', 'overview'],
    queryFn: billingService.getOverview,
    staleTime: 5 * 60 * 1000,
    retry: false,
  })
}

export function usePlans() {
  return useQuery({
    queryKey: ['billing', 'plans'],
    queryFn: () => Promise.resolve(PLANS),
    staleTime: Infinity,
    retry: false,
  })
}

export function useInvoices() {
  return useQuery({
    queryKey: ['billing', 'invoices'],
    queryFn: billingService.getInvoices,
    staleTime: 5 * 60 * 1000,
    retry: false,
  })
}

export function usePaymentMethods() {
  return useQuery({
    queryKey: ['billing', 'payment-methods'],
    queryFn: billingService.getPaymentMethods,
    staleTime: 5 * 60 * 1000,
    retry: false,
  })
}

export function useCreateCheckout() {
  return useMutation({
    mutationFn: (planId: string) => billingService.createCheckoutSession(planId),
    onSuccess: (session) => {
      window.location.href = session.url
    },
  })
}

export function useManagePayment() {
  return useMutation({
    mutationFn: billingService.createBillingPortalSession,
    onSuccess: (session) => {
      window.location.href = session.url
    },
  })
}

export function useCancelSubscription() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (feedback: CancelFeedback) => billingService.cancelSubscription(feedback),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['billing'] })
    },
  })
}
