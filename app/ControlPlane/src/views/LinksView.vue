<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useLinks } from '@/composables/useLinks'
import { useDomains } from '@/composables/useDomains'
import { useDeleteLink } from '@/composables/useLinks'
import CreateLinkDialog from '@/components/links/CreateLinkDialog.vue'
import DeleteLinkDialog from '@/components/links/DeleteLinkDialog.vue'
import type { LinkListItem, LinkListQuery, LinkStatus } from '@/types/links'

const router = useRouter()

const searchInput = ref('')
const search = ref('')
const filterDomainId = ref('')
const filterStatus = ref<LinkStatus | ''>('')
const cursor = ref<string | undefined>(undefined)
const allItems = ref<LinkListItem[]>([])

const selected = ref<string[]>([])
const showCreateDialog = ref(false)
const linkToDelete = ref<LinkListItem | null>(null)
const showDeleteDialog = ref(false)
const copyFeedback = ref<string | null>(null)

let searchTimer: ReturnType<typeof setTimeout> | null = null

const query = computed<LinkListQuery>(() => ({
  ...(search.value && { search: search.value }),
  ...(filterDomainId.value && { domainId: filterDomainId.value }),
  ...(filterStatus.value && { status: filterStatus.value }),
  ...(cursor.value && { cursor: cursor.value }),
  pageSize: 20,
}))

const { data: linkPage, isLoading, isError, isFetching } = useLinks(query)
const { data: domainList } = useDomains()
const bulkDelete = useDeleteLink()

watch(searchInput, (val) => {
  if (searchTimer) clearTimeout(searchTimer)
  searchTimer = setTimeout(() => {
    search.value = val
    cursor.value = undefined
    allItems.value = []
    selected.value = []
  }, 300)
})

watch([filterDomainId, filterStatus], () => {
  cursor.value = undefined
  allItems.value = []
  selected.value = []
})

watch(
  linkPage,
  (page) => {
    if (!page) return
    if (!cursor.value) {
      allItems.value = page.items
    } else {
      const existingIds = new Set(allItems.value.map((l) => l.id))
      const newItems = page.items.filter((l) => !existingIds.has(l.id))
      allItems.value = [...allItems.value, ...newItems]
    }
  },
  { immediate: true }
)

const hasMore = computed(() => linkPage.value?.nextCursor != null)
const totalCount = computed(() => linkPage.value?.totalCount ?? 0)

const domainOptions = computed(() => [
  { id: '', hostname: 'All domains' },
  ...(domainList.value?.items ?? []),
])

const statusOptions = [
  { value: '', title: 'All statuses' },
  { value: 'active', title: 'Active' },
  { value: 'paused', title: 'Paused' },
  { value: 'expired', title: 'Expired' },
]

const allSelected = computed(
  () => allItems.value.length > 0 && selected.value.length === allItems.value.length
)
const someSelected = computed(
  () => selected.value.length > 0 && selected.value.length < allItems.value.length
)

function toggleSelectAll() {
  if (allSelected.value) {
    selected.value = []
  } else {
    selected.value = allItems.value.map((l) => l.id)
  }
}

function toggleSelect(id: string) {
  if (selected.value.includes(id)) {
    selected.value = selected.value.filter((s) => s !== id)
  } else {
    selected.value = [...selected.value, id]
  }
}

function loadMore() {
  if (linkPage.value?.nextCursor) {
    cursor.value = linkPage.value.nextCursor
  }
}

function openDeleteDialog(link: LinkListItem) {
  linkToDelete.value = link
  showDeleteDialog.value = true
}

function onDeleteDialogClose() {
  linkToDelete.value = null
}

function onLinkCreated(link: LinkListItem) {
  allItems.value = [link, ...allItems.value]
}

async function copyShortUrl(link: LinkListItem) {
  await navigator.clipboard.writeText(link.shortUrl)
  copyFeedback.value = link.id
  setTimeout(() => {
    if (copyFeedback.value === link.id) copyFeedback.value = null
  }, 1500)
}

async function bulkDeleteSelected() {
  for (const id of selected.value) {
    await bulkDelete.mutateAsync(id)
  }
  allItems.value = allItems.value.filter((l) => !selected.value.includes(l.id))
  selected.value = []
}

function exportSelected() {
  const rows = allItems.value.filter((l) => selected.value.includes(l.id))
  const header = 'id,slug,destination_url,short_url,domain,status,clicks,created_at'
  const lines = rows.map((l) =>
    [
      l.id,
      l.slug,
      l.destinationUrl,
      l.shortUrl,
      l.domainHostname,
      l.status,
      l.clickCount,
      l.createdAt,
    ]
      .map((v) => `"${String(v).replace(/"/g, '""')}"`)
      .join(',')
  )
  const csv = [header, ...lines].join('\n')
  const blob = new Blob([csv], { type: 'text/csv' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = 'links.csv'
  a.click()
  URL.revokeObjectURL(url)
}

function statusColor(status: LinkStatus): string {
  const colors: Record<LinkStatus, string> = {
    active: 'success',
    paused: 'warning',
    expired: 'default',
  }
  return colors[status] ?? 'default'
}

function truncate(url: string, max = 60): string {
  return url.length > max ? url.slice(0, max) + '…' : url
}

function formatDate(iso: string): string {
  return new Intl.DateTimeFormat('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  }).format(new Date(iso))
}
</script>

<template>
  <v-container>
    <!-- Header -->
    <v-row>
      <v-col class="d-flex align-center">
        <div class="flex-grow-1">
          <h1 class="text-h4 mb-2">Links</h1>
          <p class="text-body-1 text-medium-emphasis">
            Manage your short links. {{ totalCount > 0 ? `${totalCount} total.` : '' }}
          </p>
        </div>
        <v-btn
          color="primary"
          variant="flat"
          prepend-icon="mdi-plus"
          data-testid="create-link-btn"
          @click="showCreateDialog = true"
        >
          New link
        </v-btn>
      </v-col>
    </v-row>

    <!-- Bulk action toolbar -->
    <v-row v-if="selected.length > 0">
      <v-col>
        <v-card color="primary" variant="tonal" data-testid="bulk-toolbar">
          <v-card-text class="d-flex align-center gap-4 py-2">
            <span class="text-body-2 font-weight-medium"> {{ selected.length }} selected </span>
            <v-spacer />
            <v-btn
              size="small"
              variant="text"
              prepend-icon="mdi-download-outline"
              data-testid="bulk-export-btn"
              @click="exportSelected"
            >
              Export
            </v-btn>
            <v-btn
              size="small"
              variant="text"
              color="error"
              prepend-icon="mdi-delete-outline"
              :loading="bulkDelete.isPending.value"
              data-testid="bulk-delete-btn"
              @click="bulkDeleteSelected"
            >
              Delete
            </v-btn>
            <v-btn size="small" variant="text" data-testid="bulk-clear-btn" @click="selected = []">
              Clear
            </v-btn>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>

    <!-- Search & filters -->
    <v-row>
      <v-col cols="12" md="5">
        <v-text-field
          v-model="searchInput"
          placeholder="Search by slug or destination…"
          variant="outlined"
          density="compact"
          prepend-inner-icon="mdi-magnify"
          clearable
          hide-details
          data-testid="search-input"
        />
      </v-col>
      <v-col cols="6" md="3">
        <v-select
          v-model="filterDomainId"
          :items="domainOptions"
          item-value="id"
          item-title="hostname"
          variant="outlined"
          density="compact"
          hide-details
          data-testid="domain-filter"
        />
      </v-col>
      <v-col cols="6" md="2">
        <v-select
          v-model="filterStatus"
          :items="statusOptions"
          item-value="value"
          item-title="title"
          variant="outlined"
          density="compact"
          hide-details
          data-testid="status-filter"
        />
      </v-col>
    </v-row>

    <!-- Error state -->
    <v-row v-if="isError">
      <v-col>
        <v-alert type="error" variant="tonal" data-testid="list-error">
          Failed to load links. Please refresh the page.
        </v-alert>
      </v-col>
    </v-row>

    <!-- Links table -->
    <v-row>
      <v-col>
        <v-card :loading="isLoading" data-testid="links-card">
          <!-- Loading skeleton -->
          <template v-if="isLoading && allItems.length === 0">
            <v-card-text>
              <v-skeleton-loader v-for="n in 5" :key="n" type="table-row" class="mb-1" />
            </v-card-text>
          </template>

          <!-- Empty state -->
          <template v-else-if="!isLoading && allItems.length === 0">
            <v-card-text>
              <div class="text-center py-12" data-testid="empty-state">
                <v-icon size="56" color="medium-emphasis" class="mb-3">mdi-link-off</v-icon>
                <p class="text-h6 text-medium-emphasis mb-1">
                  {{
                    search || filterDomainId || filterStatus
                      ? 'No links match your filters'
                      : 'No links yet'
                  }}
                </p>
                <p class="text-body-2 text-medium-emphasis mb-4">
                  {{
                    search || filterDomainId || filterStatus
                      ? 'Try adjusting your search or filters.'
                      : 'Create your first short link to get started.'
                  }}
                </p>
                <v-btn
                  v-if="!search && !filterDomainId && !filterStatus"
                  color="primary"
                  variant="flat"
                  prepend-icon="mdi-plus"
                  data-testid="empty-create-btn"
                  @click="showCreateDialog = true"
                >
                  Create your first link
                </v-btn>
              </div>
            </v-card-text>
          </template>

          <!-- Links table -->
          <template v-else>
            <v-table data-testid="links-table">
              <thead>
                <tr>
                  <th style="width: 40px">
                    <v-checkbox
                      :model-value="allSelected"
                      :indeterminate="someSelected"
                      hide-details
                      density="compact"
                      data-testid="select-all-checkbox"
                      @click="toggleSelectAll"
                    />
                  </th>
                  <th>Short URL</th>
                  <th>Destination</th>
                  <th>Domain</th>
                  <th>Clicks</th>
                  <th>Status</th>
                  <th>Created</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="link in allItems"
                  :key="link.id"
                  :data-testid="`link-row-${link.id}`"
                  class="link-row"
                  @click="router.push({ name: 'link-detail', params: { id: link.id } })"
                >
                  <td @click.stop>
                    <v-checkbox
                      :model-value="selected.includes(link.id)"
                      hide-details
                      density="compact"
                      :data-testid="`select-link-${link.id}`"
                      @click="toggleSelect(link.id)"
                    />
                  </td>
                  <td class="font-weight-medium" :data-testid="`link-slug-${link.id}`">
                    <span class="text-primary">{{ link.domainHostname }}</span
                    ><span class="text-medium-emphasis">/</span>{{ link.slug }}
                  </td>
                  <td
                    class="text-medium-emphasis text-body-2"
                    :data-testid="`link-destination-${link.id}`"
                    :title="link.destinationUrl"
                  >
                    {{ truncate(link.destinationUrl) }}
                  </td>
                  <td class="text-body-2" :data-testid="`link-domain-${link.id}`">
                    {{ link.domainHostname }}
                  </td>
                  <td :data-testid="`link-clicks-${link.id}`">{{ link.clickCount }}</td>
                  <td :data-testid="`link-status-${link.id}`">
                    <v-chip
                      :color="statusColor(link.status)"
                      size="small"
                      label
                      :data-testid="`link-status-chip-${link.id}`"
                    >
                      {{ link.status }}
                    </v-chip>
                    <v-tooltip
                      v-if="link.expiryWarning?.isNearingExpiry"
                      :text="link.expiryWarning.message"
                    >
                      <template #activator="{ props: tp }">
                        <v-icon
                          v-bind="tp"
                          color="warning"
                          size="16"
                          class="ml-1"
                          data-testid="expiry-warning-icon"
                        >
                          mdi-alert-outline
                        </v-icon>
                      </template>
                    </v-tooltip>
                  </td>
                  <td class="text-no-wrap text-body-2" :data-testid="`link-created-${link.id}`">
                    {{ formatDate(link.createdAt) }}
                  </td>
                  <td class="text-right" @click.stop>
                    <v-btn
                      icon
                      variant="text"
                      size="small"
                      :color="copyFeedback === link.id ? 'success' : undefined"
                      :data-testid="`copy-btn-${link.id}`"
                      @click="copyShortUrl(link)"
                    >
                      <v-icon>
                        {{ copyFeedback === link.id ? 'mdi-check' : 'mdi-content-copy' }}
                      </v-icon>
                      <v-tooltip activator="parent">Copy short URL</v-tooltip>
                    </v-btn>
                    <v-btn
                      icon
                      variant="text"
                      size="small"
                      :href="link.destinationUrl"
                      target="_blank"
                      rel="noopener noreferrer"
                      :data-testid="`open-btn-${link.id}`"
                    >
                      <v-icon>mdi-open-in-new</v-icon>
                      <v-tooltip activator="parent">Open destination</v-tooltip>
                    </v-btn>
                    <v-btn
                      icon
                      variant="text"
                      color="error"
                      size="small"
                      :data-testid="`delete-btn-${link.id}`"
                      @click="openDeleteDialog(link)"
                    >
                      <v-icon>mdi-delete-outline</v-icon>
                      <v-tooltip activator="parent">Delete</v-tooltip>
                    </v-btn>
                  </td>
                </tr>
              </tbody>
            </v-table>

            <!-- Load more -->
            <v-card-text v-if="hasMore || isFetching" class="text-center pt-2">
              <v-btn
                v-if="hasMore"
                variant="text"
                :loading="isFetching"
                data-testid="load-more-btn"
                @click="loadMore"
              >
                Load more
              </v-btn>
              <v-progress-circular v-else-if="isFetching" size="24" indeterminate />
            </v-card-text>
          </template>
        </v-card>
      </v-col>
    </v-row>

    <!-- Dialogs -->
    <CreateLinkDialog v-model="showCreateDialog" @created="onLinkCreated" />

    <DeleteLinkDialog
      v-if="linkToDelete"
      v-model="showDeleteDialog"
      :link="linkToDelete"
      @deleted="allItems = allItems.filter((l) => l.id !== linkToDelete?.id)"
      @update:model-value="
        (v) => {
          if (!v) onDeleteDialogClose()
        }
      "
    />
  </v-container>
</template>

<style scoped>
.link-row {
  cursor: pointer;
}
</style>
