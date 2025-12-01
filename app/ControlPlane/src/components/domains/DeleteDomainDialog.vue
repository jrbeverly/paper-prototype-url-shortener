<script setup lang="ts">
import { useDeleteDomain } from '@/composables/useDomains'
import type { Domain } from '@/types/domains'

const visible = defineModel<boolean>({ default: false })

const props = defineProps<{
  domain: Domain
}>()

const emit = defineEmits<{
  deleted: []
}>()

const deleteMutation = useDeleteDomain()

async function handleDelete() {
  await deleteMutation.mutateAsync(props.domain.id)
  emit('deleted')
  handleClose()
}

function handleClose() {
  visible.value = false
  deleteMutation.reset()
}
</script>

<template>
  <v-dialog v-model="visible" max-width="440">
    <v-card>
      <v-card-item>
        <template #prepend>
          <v-icon color="error">mdi-web-remove</v-icon>
        </template>
        <v-card-title>Delete domain</v-card-title>
      </v-card-item>

      <v-card-text>
        <p class="text-body-2 mb-2">
          Are you sure you want to delete
          <strong data-testid="delete-domain-hostname">{{ domain.hostname }}</strong
          >?
        </p>
        <p class="text-body-2 text-medium-emphasis">
          All links on this domain will be disabled. This action cannot be undone.
        </p>

        <v-alert
          v-if="deleteMutation.isError.value"
          type="error"
          variant="tonal"
          class="mt-3"
          data-testid="delete-error"
        >
          Failed to delete domain. Please try again.
        </v-alert>
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
          :loading="deleteMutation.isPending.value"
          data-testid="delete-confirm-btn"
          @click="handleDelete"
        >
          Delete domain
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
