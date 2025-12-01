<script setup lang="ts">
import { ref, computed } from 'vue'
import {
  useBillingOverview,
  useInvoices,
  usePaymentMethods,
  useManagePayment,
} from '@/composables/useBilling'
import PlanComparisonDialog from '@/components/billing/PlanComparisonDialog.vue'
import CancelSubscriptionDialog from '@/components/billing/CancelSubscriptionDialog.vue'
import type { InvoiceStatus, SubscriptionStatus } from '@/types/billing'

const { data: overview, isLoading: overviewLoading, isError: overviewError } = useBillingOverview()
const { data: invoices, isLoading: invoicesLoading } = useInvoices()
const { data: paymentMethods, isLoading: paymentLoading } = usePaymentMethods()
const managePayment = useManagePayment()

const showPlanDialog = ref(false)
const showCancelDialog = ref(false)

const subscription = computed(() => overview.value?.subscription)
const usage = computed(() => overview.value?.usage)
const defaultPaymentMethod = computed(() => paymentMethods.value?.find((pm) => pm.isDefault))

function statusColor(status: SubscriptionStatus): string {
  const colors: Record<SubscriptionStatus, string> = {
    active: 'success',
    trialing: 'info',
    past_due: 'warning',
    canceled: 'error',
    unpaid: 'error',
  }
  return colors[status] ?? 'grey'
}

function statusLabel(status: SubscriptionStatus): string {
  const labels: Record<SubscriptionStatus, string> = {
    active: 'Active',
    trialing: 'Trial',
    past_due: 'Past Due',
    canceled: 'Canceled',
    unpaid: 'Unpaid',
  }
  return labels[status] ?? status
}

function invoiceStatusColor(status: InvoiceStatus): string {
  const colors: Record<InvoiceStatus, string> = {
    paid: 'success',
    open: 'warning',
    void: 'default',
  }
  return colors[status] ?? 'default'
}

function invoiceStatusLabel(status: InvoiceStatus): string {
  const labels: Record<InvoiceStatus, string> = {
    paid: 'Paid',
    open: 'Open',
    void: 'Void',
  }
  return labels[status] ?? status
}

function formatDate(iso: string): string {
  return new Intl.DateTimeFormat('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  }).format(new Date(iso))
}

function formatAmount(cents: number, currency: string): string {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: currency.toUpperCase(),
  }).format(cents / 100)
}

function formatExpiry(month: number, year: number): string {
  return `${String(month).padStart(2, '0')}/${String(year).slice(-2)}`
}

function cardBrandLabel(brand: string): string {
  const brands: Record<string, string> = {
    visa: 'Visa',
    mastercard: 'Mastercard',
    amex: 'American Express',
    discover: 'Discover',
    jcb: 'JCB',
    unionpay: 'UnionPay',
  }
  return brands[brand.toLowerCase()] ?? brand.charAt(0).toUpperCase() + brand.slice(1)
}

function usagePercent(used: number, limit: number | null): number {
  if (limit === null || limit === 0) return 0
  return Math.min(100, Math.round((used / limit) * 100))
}

function usageProgressColor(used: number, limit: number | null): string {
  if (limit === null) return 'primary'
  const pct = usagePercent(used, limit)
  if (pct >= 90) return 'error'
  if (pct >= 75) return 'warning'
  return 'primary'
}

function formatUsage(used: number, limit: number | null): string {
  const usedStr = used.toLocaleString()
  if (limit === null) return `${usedStr} / Unlimited`
  return `${usedStr} / ${limit.toLocaleString()}`
}

const planName = computed(() => {
  if (!subscription.value) return ''
  const names: Record<string, string> = {
    free: 'Free',
    starter: 'Starter',
    pro: 'Pro',
    enterprise: 'Enterprise',
  }
  return names[subscription.value.planId] ?? subscription.value.planId
})

const isPastDue = computed(
  () => subscription.value?.status === 'past_due' || subscription.value?.status === 'unpaid'
)
</script>

<template>
  <v-container>
    <v-row>
      <v-col>
        <h1 class="text-h4 mb-2">Billing</h1>
        <p class="text-body-1 text-medium-emphasis">
          Manage your subscription and payment details.
        </p>
      </v-col>
    </v-row>

    <!-- Billing alerts -->
    <v-row v-if="!overviewLoading && subscription">
      <v-col>
        <v-alert
          v-if="isPastDue"
          type="error"
          variant="tonal"
          class="mb-2"
          data-testid="past-due-alert"
        >
          Your payment is past due. Please update your payment method to avoid service interruption.
          <template #append>
            <v-btn
              variant="flat"
              color="error"
              size="small"
              :loading="managePayment.isPending.value"
              @click="managePayment.mutateAsync()"
            >
              Update payment
            </v-btn>
          </template>
        </v-alert>

        <v-alert
          v-if="subscription.cancelAtPeriodEnd"
          type="warning"
          variant="tonal"
          data-testid="cancel-scheduled-alert"
        >
          Your subscription is scheduled to cancel on
          <strong>{{ formatDate(subscription.currentPeriodEnd) }}</strong
          >. You will revert to the Free plan after this date.
        </v-alert>
      </v-col>
    </v-row>

    <!-- Error state -->
    <v-row v-if="overviewError">
      <v-col>
        <v-alert type="error" variant="tonal" data-testid="overview-error">
          Failed to load billing information. Please refresh the page.
        </v-alert>
      </v-col>
    </v-row>

    <!-- Plan and payment method row -->
    <v-row>
      <!-- Current plan card -->
      <v-col cols="12" md="8">
        <v-card :loading="overviewLoading" data-testid="plan-card">
          <v-card-item>
            <v-card-title>Current plan</v-card-title>
          </v-card-item>

          <v-card-text v-if="!overviewLoading && subscription">
            <div class="d-flex align-center ga-3 mb-4">
              <span class="text-h5 font-weight-bold" data-testid="plan-name">{{ planName }}</span>
              <v-chip
                :color="statusColor(subscription.status)"
                size="small"
                label
                :data-testid="`status-${subscription.status}`"
              >
                {{ statusLabel(subscription.status) }}
              </v-chip>
            </div>

            <div class="d-flex ga-6 text-body-2 text-medium-emphasis">
              <div>
                <div class="text-caption text-uppercase font-weight-medium mb-1">Period start</div>
                <div data-testid="period-start">
                  {{ formatDate(subscription.currentPeriodStart) }}
                </div>
              </div>
              <div>
                <div class="text-caption text-uppercase font-weight-medium mb-1">Period end</div>
                <div data-testid="period-end">{{ formatDate(subscription.currentPeriodEnd) }}</div>
              </div>
              <div v-if="subscription.trialEnd">
                <div class="text-caption text-uppercase font-weight-medium mb-1">Trial ends</div>
                <div>{{ formatDate(subscription.trialEnd) }}</div>
              </div>
            </div>
          </v-card-text>

          <v-card-text v-else-if="overviewLoading">
            <v-skeleton-loader type="heading" class="mb-4" />
            <v-skeleton-loader type="text" />
          </v-card-text>

          <v-card-actions class="pa-4 pt-0">
            <v-btn
              color="primary"
              variant="flat"
              prepend-icon="mdi-arrow-up-circle-outline"
              :disabled="overviewLoading"
              data-testid="upgrade-plan-btn"
              @click="showPlanDialog = true"
            >
              Upgrade plan
            </v-btn>
            <v-spacer />
            <v-btn
              v-if="
                subscription &&
                subscription.status !== 'canceled' &&
                !subscription.cancelAtPeriodEnd
              "
              variant="text"
              color="error"
              :disabled="overviewLoading"
              data-testid="cancel-subscription-btn"
              @click="showCancelDialog = true"
            >
              Cancel subscription
            </v-btn>
          </v-card-actions>
        </v-card>
      </v-col>

      <!-- Payment method card -->
      <v-col cols="12" md="4">
        <v-card :loading="paymentLoading" data-testid="payment-card">
          <v-card-item>
            <v-card-title>Payment method</v-card-title>
          </v-card-item>

          <v-card-text v-if="!paymentLoading">
            <div v-if="defaultPaymentMethod" class="d-flex align-center ga-3">
              <v-icon size="32" color="medium-emphasis">mdi-credit-card-outline</v-icon>
              <div>
                <div class="text-body-1 font-weight-medium" data-testid="card-brand">
                  {{ cardBrandLabel(defaultPaymentMethod.brand) }}
                </div>
                <div class="text-body-2 text-medium-emphasis" data-testid="card-last4">
                  •••• •••• •••• {{ defaultPaymentMethod.last4 }}
                </div>
                <div class="text-caption text-medium-emphasis" data-testid="card-expiry">
                  Expires
                  {{ formatExpiry(defaultPaymentMethod.expMonth, defaultPaymentMethod.expYear) }}
                </div>
              </div>
            </div>
            <div v-else class="text-body-2 text-medium-emphasis">No payment method on file.</div>
          </v-card-text>

          <v-card-text v-else>
            <v-skeleton-loader type="list-item-avatar-two-line" />
          </v-card-text>

          <v-card-actions class="pa-4 pt-0">
            <v-btn
              variant="tonal"
              prepend-icon="mdi-pencil-outline"
              :loading="managePayment.isPending.value"
              data-testid="update-payment-btn"
              @click="managePayment.mutateAsync()"
            >
              {{ defaultPaymentMethod ? 'Update' : 'Add payment method' }}
            </v-btn>
          </v-card-actions>
        </v-card>
      </v-col>
    </v-row>

    <!-- Usage card -->
    <v-row>
      <v-col cols="12">
        <v-card :loading="overviewLoading" data-testid="usage-card">
          <v-card-item>
            <v-card-title>Usage this period</v-card-title>
          </v-card-item>

          <v-card-text v-if="!overviewLoading && usage">
            <v-row>
              <!-- Links -->
              <v-col cols="12" sm="6" md="3" data-testid="usage-links">
                <div class="d-flex justify-space-between mb-1">
                  <span class="text-body-2">Links</span>
                  <span class="text-body-2 text-medium-emphasis">
                    {{ formatUsage(usage.links.used, usage.links.limit) }}
                  </span>
                </div>
                <v-progress-linear
                  v-if="usage.links.limit !== null"
                  :model-value="usagePercent(usage.links.used, usage.links.limit)"
                  :color="usageProgressColor(usage.links.used, usage.links.limit)"
                  rounded
                  height="6"
                />
              </v-col>

              <!-- Clicks -->
              <v-col cols="12" sm="6" md="3" data-testid="usage-clicks">
                <div class="d-flex justify-space-between mb-1">
                  <span class="text-body-2">Clicks this month</span>
                  <span class="text-body-2 text-medium-emphasis">
                    {{ formatUsage(usage.clicksThisMonth.used, usage.clicksThisMonth.limit) }}
                  </span>
                </div>
                <v-progress-linear
                  v-if="usage.clicksThisMonth.limit !== null"
                  :model-value="
                    usagePercent(usage.clicksThisMonth.used, usage.clicksThisMonth.limit)
                  "
                  :color="
                    usageProgressColor(usage.clicksThisMonth.used, usage.clicksThisMonth.limit)
                  "
                  rounded
                  height="6"
                />
              </v-col>

              <!-- Domains -->
              <v-col cols="12" sm="6" md="3" data-testid="usage-domains">
                <div class="d-flex justify-space-between mb-1">
                  <span class="text-body-2">Custom domains</span>
                  <span class="text-body-2 text-medium-emphasis">
                    {{ formatUsage(usage.domains.used, usage.domains.limit) }}
                  </span>
                </div>
                <v-progress-linear
                  v-if="usage.domains.limit !== null"
                  :model-value="usagePercent(usage.domains.used, usage.domains.limit)"
                  :color="usageProgressColor(usage.domains.used, usage.domains.limit)"
                  rounded
                  height="6"
                />
              </v-col>

              <!-- Team members -->
              <v-col cols="12" sm="6" md="3" data-testid="usage-team">
                <div class="d-flex justify-space-between mb-1">
                  <span class="text-body-2">Team members</span>
                  <span class="text-body-2 text-medium-emphasis">
                    {{ formatUsage(usage.teamMembers.used, usage.teamMembers.limit) }}
                  </span>
                </div>
                <v-progress-linear
                  v-if="usage.teamMembers.limit !== null"
                  :model-value="usagePercent(usage.teamMembers.used, usage.teamMembers.limit)"
                  :color="usageProgressColor(usage.teamMembers.used, usage.teamMembers.limit)"
                  rounded
                  height="6"
                />
              </v-col>
            </v-row>
          </v-card-text>

          <v-card-text v-else-if="overviewLoading">
            <v-row>
              <v-col v-for="n in 4" :key="n" cols="12" sm="6" md="3">
                <v-skeleton-loader type="text" />
              </v-col>
            </v-row>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>

    <!-- Invoice history -->
    <v-row>
      <v-col cols="12">
        <v-card :loading="invoicesLoading" data-testid="invoices-card">
          <v-card-item>
            <v-card-title>Invoice history</v-card-title>
          </v-card-item>

          <v-card-text v-if="!invoicesLoading">
            <v-table v-if="invoices && invoices.length > 0" data-testid="invoices-table">
              <thead>
                <tr>
                  <th>Invoice</th>
                  <th>Date</th>
                  <th>Amount</th>
                  <th>Status</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="invoice in invoices"
                  :key="invoice.id"
                  :data-testid="`invoice-row-${invoice.id}`"
                >
                  <td class="font-weight-medium">{{ invoice.number }}</td>
                  <td class="text-medium-emphasis">{{ formatDate(invoice.date) }}</td>
                  <td>{{ formatAmount(invoice.amount, invoice.currency) }}</td>
                  <td>
                    <v-chip :color="invoiceStatusColor(invoice.status)" size="small" label>
                      {{ invoiceStatusLabel(invoice.status) }}
                    </v-chip>
                  </td>
                  <td class="text-right">
                    <v-btn
                      v-if="invoice.downloadUrl"
                      :href="invoice.downloadUrl"
                      target="_blank"
                      rel="noopener noreferrer"
                      icon
                      variant="text"
                      size="small"
                      :data-testid="`invoice-download-${invoice.id}`"
                    >
                      <v-icon>mdi-download-outline</v-icon>
                      <v-tooltip activator="parent" location="top">Download PDF</v-tooltip>
                    </v-btn>
                  </td>
                </tr>
              </tbody>
            </v-table>

            <div v-else class="text-center py-8 text-medium-emphasis" data-testid="no-invoices">
              <v-icon size="40" class="mb-2">mdi-receipt-text-outline</v-icon>
              <p class="text-body-2">No invoices yet.</p>
            </div>
          </v-card-text>

          <v-card-text v-else>
            <v-skeleton-loader type="table" />
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>

    <!-- Dialogs -->
    <PlanComparisonDialog
      v-if="subscription"
      v-model="showPlanDialog"
      :current-plan-id="subscription.planId"
    />

    <CancelSubscriptionDialog v-model="showCancelDialog" />
  </v-container>
</template>
