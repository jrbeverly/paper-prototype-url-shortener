<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useUpdateLink } from '@/composables/useLinks'
import type { LinkListItem, LinkStatus, RedirectType } from '@/types/links'

const props = defineProps<{ link: LinkListItem }>()
const emit = defineEmits<{ updated: [link: LinkListItem] }>()
const visible = defineModel<boolean>({ default: false })

const updateMutation = useUpdateLink(() => props.link.id)

const destinationUrl = ref('')
const redirectType = ref<RedirectType>('302')
const status = ref<LinkStatus>('active')
const expiresAt = ref('')
const clearExpiresAt = ref(false)
const maxClicks = ref<number | null>(null)
const clearMaxClicks = ref(false)

const destinationError = ref<string | null>(null)

const redirectOptions = [
  { value: '302', title: '302 — Temporary redirect' },
  { value: '301', title: '301 — Permanent redirect' },
  { value: '307', title: '307 — Temporary (preserve method)' },
  { value: '308', title: '308 — Permanent (preserve method)' },
]

const statusOptions = [
  { value: 'active', title: 'Active' },
  { value: 'paused', title: 'Paused' },
]

const canSubmit = computed(() => destinationUrl.value.trim().length > 0 && !destinationError.value)

watch(
  () => props.link,
  (link) => {
    destinationUrl.value = link.destinationUrl
    redirectType.value = link.redirectType
    status.value = link.status === 'expired' ? 'active' : link.status
    expiresAt.value = link.expiresAt ? new Date(link.expiresAt).toISOString().slice(0, 16) : ''
    clearExpiresAt.value = false
    maxClicks.value = link.maxClicks
    clearMaxClicks.value = false
    destinationError.value = null
  },
  { immediate: true }
)

function validateDestination() {
  const url = destinationUrl.value.trim()
  if (!url) {
    destinationError.value = 'Destination URL is required'
    return
  }
  try {
    const parsed = new URL(url)
    if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
      destinationError.value = 'URL must start with http:// or https://'
    } else {
      destinationError.value = null
    }
  } catch {
    destinationError.value = 'Enter a valid URL (e.g. https://example.com/page)'
  }
}

async function handleSave() {
  validateDestination()
  if (!canSubmit.value) return

  const updated = await updateMutation.mutateAsync({
    destinationUrl: destinationUrl.value.trim(),
    redirectType: redirectType.value,
    status: status.value,
    ...(clearExpiresAt.value
      ? { clearExpiresAt: true }
      : expiresAt.value
        ? { expiresAt: new Date(expiresAt.value).toISOString() }
        : {}),
    ...(clearMaxClicks.value
      ? { clearMaxClicks: true }
      : maxClicks.value != null && maxClicks.value > 0
        ? { maxClicks: maxClicks.value }
        : {}),
  })

  emit('updated', updated)
  visible.value = false
}

function handleClose() {
  visible.value = false
  updateMutation.reset()
}
</script>

<template>
  <v-dialog v-model="visible" max-width="560" @after-leave="handleClose">
    <v-card>
      <v-card-item>
        <template #prepend>
          <v-icon color="primary">mdi-pencil-outline</v-icon>
        </template>
        <v-card-title>Edit link</v-card-title>
        <v-card-subtitle class="font-weight-medium">{{ link.shortUrl }}</v-card-subtitle>
      </v-card-item>

      <v-card-text class="pt-2">
        <v-text-field
          v-model="destinationUrl"
          label="Destination URL"
          variant="outlined"
          :error-messages="destinationError ?? undefined"
          autofocus
          data-testid="edit-destination-input"
          @blur="validateDestination"
          @keyup.enter="handleSave"
        />

        <v-select
          v-model="redirectType"
          label="Redirect type"
          :items="redirectOptions"
          item-value="value"
          item-title="title"
          variant="outlined"
          class="mt-2"
          data-testid="edit-redirect-type-select"
        />

        <v-select
          v-model="status"
          label="Status"
          :items="statusOptions"
          item-value="value"
          item-title="title"
          variant="outlined"
          class="mt-2"
          data-testid="edit-status-select"
        />

        <v-text-field
          v-model="expiresAt"
          label="Expiration date"
          type="datetime-local"
          variant="outlined"
          class="mt-2"
          :disabled="clearExpiresAt"
          data-testid="edit-expires-at-input"
        />
        <v-checkbox
          v-if="link.expiresAt || expiresAt"
          v-model="clearExpiresAt"
          label="Remove expiration"
          density="compact"
          hide-details
          class="mb-2"
          data-testid="edit-clear-expires-checkbox"
        />

        <v-text-field
          v-model.number="maxClicks"
          label="Max clicks"
          type="number"
          min="1"
          variant="outlined"
          class="mt-2"
          :disabled="clearMaxClicks"
          data-testid="edit-max-clicks-input"
        />
        <v-checkbox
          v-if="link.maxClicks || maxClicks"
          v-model="clearMaxClicks"
          label="Remove click limit"
          density="compact"
          hide-details
          class="mb-2"
          data-testid="edit-clear-max-clicks-checkbox"
        />

        <v-alert
          v-if="updateMutation.isError.value"
          type="error"
          variant="tonal"
          class="mt-3"
          data-testid="edit-error"
        >
          Failed to update link. Please try again.
        </v-alert>
      </v-card-text>

      <v-divider />

      <v-card-actions class="pa-4">
        <v-spacer />
        <v-btn
          variant="text"
          :disabled="updateMutation.isPending.value"
          data-testid="edit-cancel-btn"
          @click="handleClose"
        >
          Cancel
        </v-btn>
        <v-btn
          color="primary"
          variant="flat"
          :loading="updateMutation.isPending.value"
          :disabled="!canSubmit"
          data-testid="edit-save-btn"
          @click="handleSave"
        >
          Save changes
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
