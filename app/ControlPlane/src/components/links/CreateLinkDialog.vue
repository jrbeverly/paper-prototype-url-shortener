<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useCreateLink } from '@/composables/useLinks'
import { useDomains } from '@/composables/useDomains'
import { linkService } from '@/services/linkService'
import { useWorkspaceStore } from '@/stores/workspace'
import type { LinkListItem, RedirectType } from '@/types/links'

const emit = defineEmits<{ created: [link: LinkListItem] }>()
const visible = defineModel<boolean>({ default: false })

const workspaceStore = useWorkspaceStore()
const { data: domainList } = useDomains()
const createMutation = useCreateLink()

const domainId = ref('')
const destinationUrl = ref('')
const slug = ref('')
const redirectType = ref<RedirectType>('302')
const expiresAt = ref('')
const maxClicks = ref<number | null>(null)

const destinationError = ref<string | null>(null)
const slugError = ref<string | null>(null)
const slugAvailable = ref<boolean | null>(null)
const checkingSlug = ref(false)
let slugCheckTimer: ReturnType<typeof setTimeout> | null = null

const redirectOptions = [
  { value: '302', title: '302 — Temporary redirect' },
  { value: '301', title: '301 — Permanent redirect' },
  { value: '307', title: '307 — Temporary (preserve method)' },
  { value: '308', title: '308 — Permanent (preserve method)' },
]

const domainOptions = computed(
  () => domainList.value?.items.filter((d) => d.status === 'active') ?? []
)

const canSubmit = computed(
  () =>
    domainId.value.length > 0 &&
    destinationUrl.value.trim().length > 0 &&
    !destinationError.value &&
    !slugError.value &&
    slugAvailable.value !== false
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

function validateSlug() {
  const s = slug.value.trim()
  if (!s) {
    slugError.value = null
    slugAvailable.value = null
    return true
  }
  if (!/^[a-zA-Z0-9-]+$/.test(s)) {
    slugError.value = 'Only letters, numbers, and hyphens allowed'
    slugAvailable.value = null
    return false
  }
  if (s.length > 100) {
    slugError.value = 'Slug cannot exceed 100 characters'
    slugAvailable.value = null
    return false
  }
  slugError.value = null
  return true
}

watch(slug, () => {
  if (!validateSlug()) return
  const s = slug.value.trim()
  if (!s || !domainId.value) {
    slugAvailable.value = null
    return
  }
  if (slugCheckTimer) clearTimeout(slugCheckTimer)
  checkingSlug.value = true
  slugAvailable.value = null
  const tenantId = workspaceStore.currentWorkspaceId ?? 'mock-tenant-id'
  slugCheckTimer = setTimeout(async () => {
    try {
      const result = await linkService.checkSlugAvailability(tenantId, domainId.value, s)
      slugAvailable.value = result.available
    } finally {
      checkingSlug.value = false
    }
  }, 400)
})

watch(domainId, () => {
  if (slug.value.trim()) {
    slug.value = slug.value
  }
})

async function handleCreate() {
  validateDestination()
  if (!canSubmit.value) return

  const created = await createMutation.mutateAsync({
    domainId: domainId.value,
    destinationUrl: destinationUrl.value.trim(),
    ...(slug.value.trim() && { slug: slug.value.trim() }),
    redirectType: redirectType.value,
    ...(expiresAt.value && { expiresAt: new Date(expiresAt.value).toISOString() }),
    ...(maxClicks.value != null && maxClicks.value > 0 && { maxClicks: maxClicks.value }),
  })

  emit('created', created)
  handleClose()
}

function handleClose() {
  visible.value = false
  domainId.value = ''
  destinationUrl.value = ''
  slug.value = ''
  redirectType.value = '302'
  expiresAt.value = ''
  maxClicks.value = null
  destinationError.value = null
  slugError.value = null
  slugAvailable.value = null
  createMutation.reset()
}
</script>

<template>
  <v-dialog v-model="visible" max-width="560" @after-leave="handleClose">
    <v-card>
      <v-card-item>
        <template #prepend>
          <v-icon color="primary">mdi-link-plus</v-icon>
        </template>
        <v-card-title>Create link</v-card-title>
      </v-card-item>

      <v-card-text class="pt-2">
        <v-select
          v-model="domainId"
          label="Domain"
          :items="domainOptions"
          item-value="id"
          item-title="hostname"
          variant="outlined"
          :no-data-text="'No active domains — add one in Domains'"
          data-testid="domain-select"
        />

        <v-text-field
          v-model="destinationUrl"
          label="Destination URL"
          placeholder="https://example.com/your-page"
          variant="outlined"
          class="mt-2"
          :error-messages="destinationError ?? undefined"
          autofocus
          data-testid="destination-input"
          @blur="validateDestination"
          @keyup.enter="handleCreate"
        />

        <v-text-field
          v-model="slug"
          label="Custom slug (optional)"
          placeholder="leave empty to auto-generate"
          variant="outlined"
          class="mt-2"
          :error-messages="slugError ?? undefined"
          :hint="
            checkingSlug
              ? 'Checking availability…'
              : slugAvailable === true
                ? 'Slug is available'
                : slugAvailable === false
                  ? 'Slug is already taken'
                  : ''
          "
          :persistent-hint="slugAvailable !== null || checkingSlug"
          data-testid="slug-input"
          @blur="validateSlug"
        >
          <template v-if="!checkingSlug && slugAvailable !== null" #append-inner>
            <v-icon :color="slugAvailable ? 'success' : 'error'" size="18">
              {{ slugAvailable ? 'mdi-check-circle' : 'mdi-close-circle' }}
            </v-icon>
          </template>
          <template v-else-if="checkingSlug" #append-inner>
            <v-progress-circular size="18" width="2" indeterminate />
          </template>
        </v-text-field>

        <v-select
          v-model="redirectType"
          label="Redirect type"
          :items="redirectOptions"
          item-value="value"
          item-title="title"
          variant="outlined"
          class="mt-2"
          data-testid="redirect-type-select"
        />

        <v-text-field
          v-model="expiresAt"
          label="Expiration date (optional)"
          type="datetime-local"
          variant="outlined"
          class="mt-2"
          data-testid="expires-at-input"
        />

        <v-text-field
          v-model.number="maxClicks"
          label="Max clicks (optional)"
          type="number"
          min="1"
          variant="outlined"
          class="mt-2"
          data-testid="max-clicks-input"
        />

        <v-alert
          v-if="createMutation.isError.value"
          type="error"
          variant="tonal"
          class="mt-3"
          data-testid="create-error"
        >
          Failed to create link. Please try again.
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
          Create link
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
