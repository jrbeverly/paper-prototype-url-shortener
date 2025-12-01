<script setup lang="ts">
import { useDeleteLink } from '@/composables/useLinks'
import type { LinkListItem } from '@/types/links'

const props = defineProps<{ link: LinkListItem }>()
const emit = defineEmits<{ deleted: [] }>()
const visible = defineModel<boolean>({ default: false })

const deleteMutation = useDeleteLink()

async function handleDelete() {
  await deleteMutation.mutateAsync(props.link.id)
  emit('deleted')
  visible.value = false
}

function handleClose() {
  visible.value = false
  deleteMutation.reset()
}
</script>

<template>
  <v-dialog v-model="visible" max-width="440" @after-leave="deleteMutation.reset()">
    <v-card>
      <v-card-item>
        <template #prepend>
          <v-icon color="error">mdi-delete-outline</v-icon>
        </template>
        <v-card-title>Delete link</v-card-title>
      </v-card-item>

      <v-card-text>
        <p class="text-body-1 mb-2">
          Are you sure you want to delete
          <strong>{{ link.shortUrl }}</strong
          >?
        </p>
        <p class="text-body-2 text-medium-emphasis">
          This will immediately stop redirects. The link cannot be recovered after deletion.
        </p>

        <v-alert
          v-if="deleteMutation.isError.value"
          type="error"
          variant="tonal"
          class="mt-3"
          data-testid="delete-error"
        >
          Failed to delete link. Please try again.
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
          Delete
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
