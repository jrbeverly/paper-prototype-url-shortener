<script setup lang="ts">
import { ref, computed } from 'vue'
import { useCreateApiKey } from '@/composables/useApiKeys'
import type { ApiKeyCreated } from '@/types/apiKeys'

const visible = defineModel<boolean>({ default: false })

const emit = defineEmits<{
  created: [key: ApiKeyCreated]
}>()

const createMutation = useCreateApiKey()

const name = ref('')
const role = ref('viewer')
const nameError = ref<string | null>(null)

const roleOptions = [
  { value: 'viewer', title: 'Viewer', subtitle: 'Read-only access' },
  { value: 'member', title: 'Member', subtitle: 'Create and manage links and domains' },
  { value: 'admin', title: 'Admin', subtitle: 'Full access except billing' },
]

const canSubmit = computed(() => name.value.trim().length > 0 && name.value.trim().length <= 100)

function validateName() {
  const trimmed = name.value.trim()
  if (!trimmed) {
    nameError.value = 'Name is required'
  } else if (trimmed.length > 100) {
    nameError.value = 'Name cannot exceed 100 characters'
  } else {
    nameError.value = null
  }
}

async function handleCreate() {
  validateName()
  if (!canSubmit.value) return

  const created = await createMutation.mutateAsync({
    name: name.value.trim(),
    permissions: [role.value],
  })

  emit('created', created)
  handleClose()
}

function handleClose() {
  visible.value = false
  name.value = ''
  role.value = 'viewer'
  nameError.value = null
  createMutation.reset()
}
</script>

<template>
  <v-dialog v-model="visible" max-width="480" @after-leave="handleClose">
    <v-card>
      <v-card-item>
        <template #prepend>
          <v-icon color="primary">mdi-key-plus</v-icon>
        </template>
        <v-card-title>Create API key</v-card-title>
      </v-card-item>

      <v-card-text>
        <v-text-field
          v-model="name"
          label="Key name"
          placeholder="e.g. Production, CI/CD, My app"
          variant="outlined"
          :error-messages="nameError ?? undefined"
          maxlength="100"
          autofocus
          data-testid="key-name-input"
          @blur="validateName"
          @keyup.enter="handleCreate"
        />

        <v-select
          v-model="role"
          label="Role"
          :items="roleOptions"
          item-value="value"
          item-title="title"
          variant="outlined"
          class="mt-2"
          data-testid="key-role-select"
        >
          <template #item="{ props, item }">
            <v-list-item v-bind="props" :subtitle="item.raw.subtitle" />
          </template>
        </v-select>

        <v-alert
          v-if="createMutation.isError.value"
          type="error"
          variant="tonal"
          class="mt-2"
          data-testid="create-error"
        >
          Failed to create API key. Please try again.
        </v-alert>
      </v-card-text>

      <v-divider />

      <v-card-actions class="pa-4">
        <v-spacer />
        <v-btn
          variant="text"
          :disabled="createMutation.isPending.value"
          data-testid="create-cancel-btn"
          @click="handleClose"
        >
          Cancel
        </v-btn>
        <v-btn
          color="primary"
          variant="flat"
          :loading="createMutation.isPending.value"
          :disabled="!canSubmit"
          data-testid="create-submit-btn"
          @click="handleCreate"
        >
          Create key
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
