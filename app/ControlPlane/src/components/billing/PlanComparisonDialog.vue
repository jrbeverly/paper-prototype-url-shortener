<script setup lang="ts">
import { computed } from 'vue'
import { usePlans, useCreateCheckout } from '@/composables/useBilling'
import type { PlanId } from '@/types/billing'

const props = defineProps<{
  currentPlanId: PlanId
}>()

const visible = defineModel<boolean>({ default: false })

const { data: plans } = usePlans()
const checkout = useCreateCheckout()

const planOrder: PlanId[] = ['free', 'starter', 'pro', 'enterprise']

function isCurrentPlan(planId: PlanId): boolean {
  return planId === props.currentPlanId
}

function isUpgrade(planId: PlanId): boolean {
  return planOrder.indexOf(planId) > planOrder.indexOf(props.currentPlanId)
}

const checkoutPlanId = computed(() => checkout.variables.value as string | undefined)

function ctaLabel(planId: PlanId): string {
  if (isCurrentPlan(planId)) return 'Current plan'
  return isUpgrade(planId) ? 'Upgrade' : 'Downgrade'
}

async function selectPlan(planId: PlanId) {
  if (isCurrentPlan(planId)) return
  await checkout.mutateAsync(planId)
}

function formatLimit(value: number | null): string {
  if (value === null) return 'Unlimited'
  return value.toLocaleString()
}

function formatPrice(price: number): string {
  if (price === 0) return 'Free'
  return `$${price}/mo`
}
</script>

<template>
  <v-dialog v-model="visible" max-width="960" scrollable>
    <v-card>
      <v-card-title class="pa-6 pb-2">
        <span class="text-h5">Choose a plan</span>
      </v-card-title>
      <v-card-subtitle class="px-6 pb-4">
        Select the plan that best fits your needs. Changes take effect immediately.
      </v-card-subtitle>

      <v-divider />

      <v-card-text class="pa-6">
        <v-row>
          <v-col v-for="plan in plans" :key="plan.id" cols="12" sm="6" md="3">
            <v-card
              :variant="isCurrentPlan(plan.id) ? 'tonal' : 'outlined'"
              :color="plan.popular ? 'primary' : undefined"
              height="100%"
              :data-testid="`plan-card-${plan.id}`"
            >
              <v-card-item>
                <template v-if="plan.popular" #append>
                  <v-chip color="primary" size="small" label>Popular</v-chip>
                </template>
                <v-card-title>{{ plan.name }}</v-card-title>
                <v-card-subtitle>{{ plan.description }}</v-card-subtitle>
              </v-card-item>

              <v-card-text>
                <div class="text-h5 font-weight-bold mb-4">
                  {{ formatPrice(plan.price) }}
                  <span
                    v-if="plan.price > 0"
                    class="text-body-2 font-weight-regular text-medium-emphasis"
                  >
                    / month
                  </span>
                </div>

                <v-divider class="mb-3" />

                <div
                  class="text-caption text-medium-emphasis mb-2 text-uppercase font-weight-medium"
                >
                  Limits
                </div>
                <div class="d-flex flex-column ga-1 mb-4">
                  <div class="d-flex align-center ga-2 text-body-2">
                    <v-icon size="16" color="medium-emphasis">mdi-link-variant</v-icon>
                    {{ formatLimit(plan.limits.links) }} links
                  </div>
                  <div class="d-flex align-center ga-2 text-body-2">
                    <v-icon size="16" color="medium-emphasis"
                      >mdi-cursor-default-click-outline</v-icon
                    >
                    {{ formatLimit(plan.limits.clicksPerMonth) }} clicks/mo
                  </div>
                  <div class="d-flex align-center ga-2 text-body-2">
                    <v-icon size="16" color="medium-emphasis">mdi-web</v-icon>
                    {{ formatLimit(plan.limits.domains) }} domains
                  </div>
                  <div class="d-flex align-center ga-2 text-body-2">
                    <v-icon size="16" color="medium-emphasis">mdi-account-group-outline</v-icon>
                    {{ formatLimit(plan.limits.teamMembers) }} team members
                  </div>
                </div>

                <div
                  class="text-caption text-medium-emphasis mb-2 text-uppercase font-weight-medium"
                >
                  Features
                </div>
                <div class="d-flex flex-column ga-1">
                  <div
                    v-for="feature in plan.features"
                    :key="feature"
                    class="d-flex align-center ga-2 text-body-2"
                  >
                    <v-icon size="16" color="success">mdi-check</v-icon>
                    {{ feature }}
                  </div>
                </div>
              </v-card-text>

              <v-card-actions class="pa-4 pt-0">
                <v-btn
                  :disabled="isCurrentPlan(plan.id)"
                  :loading="checkout.isPending.value && checkoutPlanId === plan.id"
                  :variant="isCurrentPlan(plan.id) ? 'tonal' : 'flat'"
                  :color="isCurrentPlan(plan.id) ? undefined : 'primary'"
                  block
                  :data-testid="`plan-select-${plan.id}`"
                  @click="selectPlan(plan.id)"
                >
                  {{ ctaLabel(plan.id) }}
                </v-btn>
              </v-card-actions>
            </v-card>
          </v-col>
        </v-row>

        <v-alert
          v-if="checkout.isError.value"
          type="error"
          class="mt-4"
          data-testid="checkout-error"
        >
          Failed to start checkout. Please try again.
        </v-alert>
      </v-card-text>

      <v-divider />

      <v-card-actions class="pa-4">
        <v-spacer />
        <v-btn variant="text" @click="visible = false">Close</v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
