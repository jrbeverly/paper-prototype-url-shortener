<script setup lang="ts">
import { useRevokeApiKey } from '@/composables/useApiKeys'
import type { ApiKey } from '@/types/apiKeys'

const visible = defineModel<boolean>({ default: false })

const props = defineProps<{
  apiKey: ApiKey
}>()

const revokeMutation = useRevokeApiKey()

async function handleRevoke() {
  await revokeMutation.mutateAsync(props.apiKey.id)
  visible.value = false
}

function handleClose() {
  visible.value = false
  revokeMutation.reset()
}
</script>

<template>
  <v-dialog v-model="visible" max-width="440" @after-leave="revokeMutation.reset()">
    <v-card>
      <v-card-item>
        <template #prepend>
          <v-icon color="error">mdi-key-remove</v-icon>
        </template>
        <v-card-title>Revoke API key</v-card-title>
      </v-card-item>

      <v-card-text>
        <p class="text-body-1 mb-3">
          Are you sure you want to revoke
          <strong>{{ apiKey.name }}</strong
          >?
        </p>

        <v-alert type="error" variant="tonal" data-testid="revoke-warning">
          This action is immediate and cannot be undone. Any application using this key will lose
          access instantly.
        </v-alert>

        <v-alert
          v-if="revokeMutation.isError.value"
          type="error"
          class="mt-3"
          data-testid="revoke-error"
        >
          Failed to revoke API key. Please try again.
        </v-alert>
      </v-card-text>

      <v-divider />

      <v-card-actions class="pa-4">
        <v-spacer />
        <v-btn
          variant="text"
          :disabled="revokeMutation.isPending.value"
          data-testid="revoke-cancel-btn"
          @click="handleClose"
        >
          Cancel
        </v-btn>
        <v-btn
          color="error"
          variant="flat"
          :loading="revokeMutation.isPending.value"
          data-testid="revoke-confirm-btn"
          @click="handleRevoke"
        >
          Revoke key
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
