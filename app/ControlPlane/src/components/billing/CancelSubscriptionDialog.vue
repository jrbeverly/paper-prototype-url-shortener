<script setup lang="ts">
import { ref, computed } from 'vue'
import { useCancelSubscription } from '@/composables/useBilling'
import type { CancelReason } from '@/types/billing'

const visible = defineModel<boolean>({ default: false })

const cancelMutation = useCancelSubscription()

const selectedReason = ref<CancelReason | null>(null)
const comment = ref('')

const reasonOptions: Array<{ value: CancelReason; label: string }> = [
  { value: 'too_expensive', label: 'Too expensive' },
  { value: 'missing_features', label: 'Missing features I need' },
  { value: 'switching_service', label: 'Switching to another service' },
  { value: 'not_using', label: 'Not using it enough' },
  { value: 'other', label: 'Other reason' },
]

const canSubmit = computed(() => selectedReason.value !== null)

async function handleCancel() {
  if (!selectedReason.value) return
  await cancelMutation.mutateAsync({
    reason: selectedReason.value,
    comment: comment.value.trim() || undefined,
  })
  visible.value = false
}

function handleClose() {
  visible.value = false
}
</script>

<template>
  <v-dialog v-model="visible" max-width="500">
    <v-card>
      <v-card-item>
        <template #prepend>
          <v-icon color="error">mdi-alert-circle-outline</v-icon>
        </template>
        <v-card-title>Cancel subscription</v-card-title>
      </v-card-item>

      <v-card-text>
        <v-alert type="warning" variant="tonal" class="mb-4" data-testid="cancel-warning">
          Your subscription will remain active until the end of the current billing period. After
          that, your account will revert to the Free plan and you will lose access to paid features.
        </v-alert>

        <p class="text-body-2 mb-4">
          Before you go, please let us know why you're cancelling. Your feedback helps us improve.
        </p>

        <v-radio-group
          v-model="selectedReason"
          label="Reason for cancelling"
          data-testid="cancel-reason"
        >
          <v-radio
            v-for="option in reasonOptions"
            :key="option.value"
            :label="option.label"
            :value="option.value"
            :data-testid="`reason-${option.value}`"
          />
        </v-radio-group>

        <v-textarea
          v-model="comment"
          label="Additional comments (optional)"
          placeholder="Tell us more about your experience..."
          rows="3"
          variant="outlined"
          class="mt-2"
          data-testid="cancel-comment"
        />

        <v-alert
          v-if="cancelMutation.isError.value"
          type="error"
          class="mt-4"
          data-testid="cancel-error"
        >
          Failed to cancel subscription. Please try again or contact support.
        </v-alert>
      </v-card-text>

      <v-divider />

      <v-card-actions class="pa-4">
        <v-spacer />
        <v-btn
          variant="text"
          :disabled="cancelMutation.isPending.value"
          data-testid="cancel-close-btn"
          @click="handleClose"
        >
          Keep my plan
        </v-btn>
        <v-btn
          color="error"
          variant="flat"
          :loading="cancelMutation.isPending.value"
          :disabled="!canSubmit"
          data-testid="cancel-submit-btn"
          @click="handleCancel"
        >
          Cancel subscription
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
