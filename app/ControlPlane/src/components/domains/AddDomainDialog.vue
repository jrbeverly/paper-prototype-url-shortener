<script setup lang="ts">
import { ref, computed } from 'vue'
import { useAddDomain } from '@/composables/useDomains'
import type { DomainCreated } from '@/types/domains'

const visible = defineModel<boolean>({ default: false })

const emit = defineEmits<{
  created: [domain: DomainCreated]
}>()

const addMutation = useAddDomain()

const hostname = ref('')
const hostnameError = ref<string | null>(null)

const hostnamePattern = /^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,}$/

function validateHostname(): boolean {
  const value = hostname.value.trim().toLowerCase()
  if (!value) {
    hostnameError.value = 'Hostname is required'
    return false
  }
  if (/^https?:\/\//i.test(value)) {
    hostnameError.value = 'Enter the hostname only, without https:// or http://'
    return false
  }
  if (value.includes('/')) {
    hostnameError.value =
      'Hostname must not include a path — enter just the domain, e.g. go.example.com'
    return false
  }
  if (!hostnamePattern.test(value)) {
    hostnameError.value = 'Enter a valid hostname, e.g. go.example.com'
    return false
  }
  hostnameError.value = null
  return true
}

const canSubmit = computed(() => hostname.value.trim().length > 0)

async function handleAdd() {
  if (!validateHostname()) return
  const created = await addMutation.mutateAsync({ hostname: hostname.value.trim().toLowerCase() })
  emit('created', created)
  handleClose()
}

function handleClose() {
  visible.value = false
  hostname.value = ''
  hostnameError.value = null
  addMutation.reset()
}
</script>

<template>
  <v-dialog v-model="visible" max-width="480" @after-leave="handleClose">
    <v-card>
      <v-card-item>
        <template #prepend>
          <v-icon color="primary">mdi-web-plus</v-icon>
        </template>
        <v-card-title>Add domain</v-card-title>
      </v-card-item>

      <v-card-text>
        <p class="text-body-2 text-medium-emphasis mb-4">
          Enter the hostname you want to use for short links. You'll need to add DNS records to
          verify ownership.
        </p>

        <v-text-field
          v-model="hostname"
          label="Hostname"
          placeholder="go.example.com"
          variant="outlined"
          :error-messages="hostnameError ?? undefined"
          autofocus
          data-testid="hostname-input"
          @blur="validateHostname"
          @keyup.enter="handleAdd"
        />

        <v-alert
          v-if="addMutation.isError.value"
          type="error"
          variant="tonal"
          class="mt-2"
          data-testid="add-error"
        >
          Failed to add domain. Please try again.
        </v-alert>
      </v-card-text>

      <v-divider />

      <v-card-actions class="pa-4">
        <v-spacer />
        <v-btn
          variant="text"
          :disabled="addMutation.isPending.value"
          data-testid="add-cancel-btn"
          @click="handleClose"
        >
          Cancel
        </v-btn>
        <v-btn
          color="primary"
          variant="flat"
          :loading="addMutation.isPending.value"
          :disabled="!canSubmit"
          data-testid="add-submit-btn"
          @click="handleAdd"
        >
          Add domain
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
