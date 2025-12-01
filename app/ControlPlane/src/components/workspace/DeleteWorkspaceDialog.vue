<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useDeleteWorkspace } from '@/composables/useWorkspace'
import type { WorkspaceSettings } from '@/types/workspace'

const visible = defineModel<boolean>({ default: false })

const props = defineProps<{
  workspace: WorkspaceSettings
}>()

const emit = defineEmits<{
  deleted: []
}>()

const router = useRouter()
const confirmName = ref('')
const deleteMutation = useDeleteWorkspace()

const nameMatches = () => confirmName.value.trim() === props.workspace.name

async function handleDelete() {
  await deleteMutation.mutateAsync(props.workspace.id)
  emit('deleted')
  await router.push({ name: 'dashboard' })
  handleClose()
}

function handleClose() {
  visible.value = false
  confirmName.value = ''
  deleteMutation.reset()
}
</script>

<template>
  <v-dialog v-model="visible" max-width="480">
    <v-card>
      <v-card-item>
        <template #prepend>
          <v-icon color="error">mdi-delete-alert-outline</v-icon>
        </template>
        <v-card-title>Delete workspace</v-card-title>
      </v-card-item>

      <v-card-text>
        <p class="text-body-2 mb-3">
          This will permanently delete
          <strong data-testid="delete-workspace-name">{{ workspace.name }}</strong>
          and all its links, domains, and API keys. This action cannot be undone.
        </p>
        <p class="text-body-2 text-medium-emphasis mb-4">
          Type <strong>{{ workspace.name }}</strong> to confirm.
        </p>
        <v-text-field
          v-model="confirmName"
          label="Workspace name"
          variant="outlined"
          density="compact"
          hide-details
          data-testid="confirm-name-input"
        />

        <v-alert
          v-if="deleteMutation.isError.value"
          type="error"
          variant="tonal"
          class="mt-3"
          data-testid="delete-error"
        >
          Failed to delete workspace. Please try again.
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
          :disabled="!nameMatches()"
          data-testid="delete-confirm-btn"
          @click="handleDelete"
        >
          Delete workspace
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
