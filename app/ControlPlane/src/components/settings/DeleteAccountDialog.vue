<script setup lang="ts">
import { ref, computed } from 'vue'
import { logout } from '@/services/authService'
import { useDeleteAccount } from '@/composables/useSettings'

const visible = defineModel<boolean>({ default: false })

const confirmText = ref('')
const canConfirm = computed(() => confirmText.value === 'DELETE')

const deleteMutation = useDeleteAccount()

function handleClose() {
  visible.value = false
  confirmText.value = ''
  deleteMutation.reset()
}

async function handleConfirm() {
  await deleteMutation.mutateAsync()
  await logout()
}
</script>

<template>
  <v-dialog v-model="visible" max-width="480" @after-leave="handleClose">
    <v-card>
      <v-card-item>
        <template #prepend>
          <v-icon color="error">mdi-alert-circle-outline</v-icon>
        </template>
        <v-card-title>Delete account</v-card-title>
      </v-card-item>

      <v-card-text>
        <v-alert type="warning" variant="tonal" class="mb-4" data-testid="delete-warning">
          This will permanently delete your account, all your links, domains, and associated data.
          This action cannot be undone.
        </v-alert>

        <v-alert
          v-if="deleteMutation.isError.value"
          type="error"
          density="compact"
          class="mb-4"
          data-testid="delete-error"
        >
          Failed to delete account. Please try again.
        </v-alert>

        <p class="text-body-2 mb-3">To confirm, type <strong>DELETE</strong> in the field below:</p>
        <v-text-field
          v-model="confirmText"
          label="Type DELETE to confirm"
          density="compact"
          data-testid="delete-confirm-input"
        />
      </v-card-text>

      <v-divider />

      <v-card-actions class="pa-4">
        <v-spacer />
        <v-btn
          variant="text"
          :disabled="deleteMutation.isPending.value"
          data-testid="delete-cancel-btn"
          @click="handleClose"
        >
          Cancel
        </v-btn>
        <v-btn
          color="error"
          variant="flat"
          :disabled="!canConfirm"
          :loading="deleteMutation.isPending.value"
          data-testid="delete-confirm-btn"
          @click="handleConfirm"
        >
          Delete my account
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
