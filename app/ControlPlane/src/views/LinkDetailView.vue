<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useLink } from '@/composables/useLinks'
import EditLinkDialog from '@/components/links/EditLinkDialog.vue'
import DeleteLinkDialog from '@/components/links/DeleteLinkDialog.vue'
import QrCodeDialog from '@/components/links/QrCodeDialog.vue'
import { Chart, registerables } from 'chart.js'
import type { LinkListItem, LinkStatus } from '@/types/links'

Chart.register(...registerables)

const route = useRoute()
const router = useRouter()
const linkId = route.params.id as string

const { data: link, isLoading, isError } = useLink(linkId)

const showEditDialog = ref(false)
const showDeleteDialog = ref(false)
const showQrDialog = ref(false)
const copyFeedback = ref(false)

const trendCanvas = ref<HTMLCanvasElement | null>(null)
const geoCanvas = ref<HTMLCanvasElement | null>(null)
const deviceCanvas = ref<HTMLCanvasElement | null>(null)

let trendChart: Chart | null = null
let geoChart: Chart | null = null
let deviceChart: Chart | null = null

async function copyShortUrl() {
  if (!link.value) return
  await navigator.clipboard.writeText(link.value.shortUrl)
  copyFeedback.value = true
  setTimeout(() => {
    copyFeedback.value = false
  }, 1500)
}

async function shareLink() {
  if (!link.value) return
  if (navigator.share) {
    await navigator.share({ title: link.value.shortUrl, url: link.value.shortUrl })
  } else {
    await copyShortUrl()
  }
}

function onUpdated() {
  // Query is invalidated by the composable's onSuccess handler
}

function onDeleted() {
  router.push({ name: 'links' })
}

function statusColor(status: LinkStatus): string {
  const colors: Record<LinkStatus, string> = {
    active: 'success',
    paused: 'warning',
    expired: 'default',
  }
  return colors[status] ?? 'default'
}

function formatDate(iso: string): string {
  return new Intl.DateTimeFormat('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(iso))
}

function formatShortDate(iso: string): string {
  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric',
  }).format(new Date(iso))
}

const trendLabels = computed(
  () => link.value?.analytics?.clickTrend?.map((d) => formatShortDate(d.date)) ?? []
)
const trendValues = computed(() => link.value?.analytics?.clickTrend?.map((d) => d.count) ?? [])

const countryLabels = computed(
  () => link.value?.analytics?.topCountries?.map((c) => c.countryName) ?? []
)
const countryValues = computed(() => link.value?.analytics?.topCountries?.map((c) => c.count) ?? [])

const deviceValues = computed(() => {
  const d = link.value?.analytics?.devices
  return d ? [d.desktop, d.mobile, d.tablet] : [0, 0, 0]
})

function destroyCharts() {
  trendChart?.destroy()
  trendChart = null
  geoChart?.destroy()
  geoChart = null
  deviceChart?.destroy()
  deviceChart = null
}

function createTrendChart() {
  if (!trendCanvas.value || trendLabels.value.length === 0) return
  trendChart = new Chart(trendCanvas.value, {
    type: 'line',
    data: {
      labels: trendLabels.value,
      datasets: [
        {
          label: 'Clicks',
          data: trendValues.value,
          borderColor: '#1976d2',
          backgroundColor: 'rgba(25, 118, 210, 0.08)',
          fill: true,
          tension: 0.3,
          pointRadius: 1,
          pointHitRadius: 8,
        },
      ],
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: { legend: { display: false } },
      scales: {
        x: { grid: { display: false }, ticks: { maxTicksLimit: 8 } },
        y: { beginAtZero: true, grid: { color: 'rgba(0,0,0,0.06)' } },
      },
    },
  })
}

function createGeoChart() {
  if (!geoCanvas.value || countryLabels.value.length === 0) return
  geoChart = new Chart(geoCanvas.value, {
    type: 'bar',
    data: {
      labels: countryLabels.value,
      datasets: [
        {
          label: 'Clicks',
          data: countryValues.value,
          backgroundColor: [
            '#1976d2',
            '#1565c0',
            '#1e88e5',
            '#42a5f5',
            '#64b5f6',
            '#90caf9',
            '#bbdefb',
          ],
        },
      ],
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      indexAxis: 'y',
      plugins: { legend: { display: false } },
      scales: {
        x: { beginAtZero: true, grid: { color: 'rgba(0,0,0,0.06)' } },
        y: { grid: { display: false } },
      },
    },
  })
}

function createDeviceChart() {
  if (!deviceCanvas.value) return
  deviceChart = new Chart(deviceCanvas.value, {
    type: 'doughnut',
    data: {
      labels: ['Desktop', 'Mobile', 'Tablet'],
      datasets: [
        {
          data: deviceValues.value,
          backgroundColor: ['#1976d2', '#43a047', '#ff9800'],
        },
      ],
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { position: 'bottom' },
      },
    },
  })
}

function mountCharts() {
  destroyCharts()
  createTrendChart()
  createGeoChart()
  createDeviceChart()
}

watch(
  () => link.value,
  () => {
    if (link.value) {
      // Wait for DOM update before mounting charts
      setTimeout(mountCharts, 0)
    }
  }
)

onMounted(() => {
  if (link.value) mountCharts()
})

onUnmounted(() => {
  destroyCharts()
})
</script>

<template>
  <v-container>
    <!-- Back navigation -->
    <v-row>
      <v-col>
        <v-btn
          variant="text"
          prepend-icon="mdi-arrow-left"
          class="mb-2"
          data-testid="back-btn"
          @click="router.push({ name: 'links' })"
        >
          All links
        </v-btn>
      </v-col>
    </v-row>

    <!-- Loading state -->
    <template v-if="isLoading">
      <v-row>
        <v-col>
          <v-skeleton-loader type="article" data-testid="loading-skeleton" />
        </v-col>
      </v-row>
    </template>

    <!-- Error state -->
    <template v-else-if="isError">
      <v-row>
        <v-col>
          <v-alert type="error" variant="tonal" data-testid="detail-error">
            Failed to load link details. Please go back and try again.
          </v-alert>
        </v-col>
      </v-row>
    </template>

    <!-- Link detail -->
    <template v-else-if="link">
      <!-- Header with URL and actions -->
      <v-row>
        <v-col class="d-flex align-center flex-wrap gap-3">
          <div class="flex-grow-1">
            <div class="d-flex align-center flex-wrap gap-2 mb-1">
              <h1 class="text-h5 font-weight-bold" data-testid="link-short-url">
                {{ link.shortUrl }}
              </h1>
              <v-chip
                :color="statusColor(link.status)"
                size="small"
                label
                data-testid="link-status-chip"
              >
                {{ link.status }}
              </v-chip>
              <v-chip
                v-if="link.expiryWarning?.isNearingExpiry"
                color="warning"
                size="small"
                label
                prepend-icon="mdi-alert-outline"
                data-testid="expiry-warning-chip"
              >
                {{ link.expiryWarning.message }}
              </v-chip>
            </div>
            <p class="text-body-2 text-medium-emphasis" data-testid="link-destination">
              → {{ link.destinationUrl }}
            </p>
          </div>
          <div class="d-flex flex-wrap gap-2">
            <v-btn
              variant="outlined"
              :color="copyFeedback ? 'success' : undefined"
              :prepend-icon="copyFeedback ? 'mdi-check' : 'mdi-content-copy'"
              data-testid="copy-btn"
              @click="copyShortUrl"
            >
              {{ copyFeedback ? 'Copied!' : 'Copy' }}
            </v-btn>
            <v-btn
              variant="outlined"
              prepend-icon="mdi-qrcode"
              data-testid="qr-btn"
              @click="showQrDialog = true"
            >
              QR
            </v-btn>
            <v-btn
              variant="outlined"
              prepend-icon="mdi-share-variant"
              data-testid="share-btn"
              @click="shareLink"
            >
              Share
            </v-btn>
            <v-btn
              variant="outlined"
              prepend-icon="mdi-open-in-new"
              :href="link.destinationUrl"
              target="_blank"
              rel="noopener noreferrer"
              data-testid="open-btn"
            >
              Open
            </v-btn>
            <v-btn
              color="primary"
              variant="flat"
              prepend-icon="mdi-pencil-outline"
              data-testid="edit-btn"
              @click="showEditDialog = true"
            >
              Edit
            </v-btn>
            <v-btn
              color="error"
              variant="outlined"
              prepend-icon="mdi-delete-outline"
              data-testid="delete-btn"
              @click="showDeleteDialog = true"
            >
              Delete
            </v-btn>
          </div>
        </v-col>
      </v-row>

      <!-- Quick stats cards -->
      <v-row>
        <v-col cols="12" sm="4">
          <v-card variant="tonal" color="primary" data-testid="clicks-card">
            <v-card-text>
              <div class="text-overline mb-1">Total clicks</div>
              <div class="text-h3 font-weight-bold" data-testid="total-clicks">
                {{ link.analytics.totalClicks.toLocaleString() }}
              </div>
            </v-card-text>
          </v-card>
        </v-col>
        <v-col cols="12" sm="4">
          <v-card variant="tonal" color="secondary" data-testid="unique-clicks-card">
            <v-card-text>
              <div class="text-overline mb-1">Unique visitors</div>
              <div class="text-h3 font-weight-bold" data-testid="unique-clicks">
                {{ link.analytics.uniqueClicks.toLocaleString() }}
              </div>
            </v-card-text>
          </v-card>
        </v-col>
        <v-col cols="12" sm="4">
          <v-card variant="tonal" color="success" data-testid="clicks-today-card">
            <v-card-text>
              <div class="text-overline mb-1">Clicks today</div>
              <div class="text-h3 font-weight-bold" data-testid="clicks-today">
                {{ link.analytics.clicksToday.toLocaleString() }}
              </div>
            </v-card-text>
          </v-card>
        </v-col>
      </v-row>

      <!-- Click trend chart -->
      <v-row>
        <v-col>
          <v-card data-testid="click-trend-card">
            <v-card-title class="d-flex align-center">
              <v-icon class="mr-2">mdi-chart-line</v-icon>
              Click trend (30 days)
            </v-card-title>
            <v-card-text>
              <div style="height: 240px">
                <canvas ref="trendCanvas" data-testid="click-trend-chart"></canvas>
              </div>
            </v-card-text>
          </v-card>
        </v-col>
      </v-row>

      <!-- Countries and referrers -->
      <v-row>
        <v-col cols="12" md="6">
          <v-card data-testid="countries-card">
            <v-card-title class="d-flex align-center">
              <v-icon class="mr-2">mdi-earth</v-icon>
              Top countries
            </v-card-title>
            <v-card-text>
              <div
                v-if="link.analytics.topCountries.length === 0"
                class="text-center py-6 text-medium-emphasis"
                data-testid="countries-empty"
              >
                No country data yet
              </div>
              <template v-else>
                <div style="height: 200px">
                  <canvas ref="geoCanvas" data-testid="countries-chart"></canvas>
                </div>
                <v-list density="compact" class="mt-2">
                  <v-list-item v-for="c in link.analytics.topCountries" :key="c.countryCode">
                    <template #prepend>
                      <span class="text-caption mr-2">{{ c.countryCode }}</span>
                    </template>
                    <v-list-item-title>{{ c.countryName }}</v-list-item-title>
                    <template #append>
                      <span class="text-caption">{{ c.count.toLocaleString() }}</span>
                    </template>
                  </v-list-item>
                </v-list>
              </template>
            </v-card-text>
          </v-card>
        </v-col>
        <v-col cols="12" md="6">
          <v-card data-testid="referrers-card">
            <v-card-title class="d-flex align-center">
              <v-icon class="mr-2">mdi-open-in-app</v-icon>
              Top referrers
            </v-card-title>
            <v-card-text>
              <div
                v-if="link.analytics.topReferrers.length === 0"
                class="text-center py-6 text-medium-emphasis"
                data-testid="referrers-empty"
              >
                No referrer data yet
              </div>
              <v-list v-else data-testid="referrers-list">
                <v-list-item v-for="r in link.analytics.topReferrers" :key="r.domain">
                  <template #prepend>
                    <v-icon class="mr-2">mdi-web</v-icon>
                  </template>
                  <v-list-item-title>{{ r.domain }}</v-list-item-title>
                  <template #append>
                    <span class="text-caption">{{ r.count.toLocaleString() }}</span>
                  </template>
                </v-list-item>
              </v-list>
            </v-card-text>
          </v-card>
        </v-col>
      </v-row>

      <!-- Device breakdown and link properties -->
      <v-row>
        <v-col cols="12" sm="6">
          <v-card data-testid="devices-card">
            <v-card-title class="d-flex align-center">
              <v-icon class="mr-2">mdi-cellphone</v-icon>
              Device breakdown
            </v-card-title>
            <v-card-text>
              <div style="height: 220px">
                <canvas ref="deviceCanvas" data-testid="device-chart"></canvas>
              </div>
            </v-card-text>
          </v-card>
        </v-col>
        <v-col cols="12" sm="6">
          <v-card data-testid="link-properties-card">
            <v-card-title class="d-flex align-center">
              <v-icon class="mr-2">mdi-information-outline</v-icon>
              Link details
            </v-card-title>
            <v-list>
              <v-list-item>
                <template #prepend>
                  <v-icon class="mr-2">mdi-web</v-icon>
                </template>
                <v-list-item-title>Domain</v-list-item-title>
                <v-list-item-subtitle data-testid="prop-domain">
                  {{ link.domainHostname }}
                </v-list-item-subtitle>
              </v-list-item>
              <v-divider />
              <v-list-item>
                <template #prepend>
                  <v-icon class="mr-2">mdi-link</v-icon>
                </template>
                <v-list-item-title>Slug</v-list-item-title>
                <v-list-item-subtitle data-testid="prop-slug">{{ link.slug }}</v-list-item-subtitle>
              </v-list-item>
              <v-divider />
              <v-list-item>
                <template #prepend>
                  <v-icon class="mr-2">mdi-swap-horizontal</v-icon>
                </template>
                <v-list-item-title>Redirect type</v-list-item-title>
                <v-list-item-subtitle data-testid="prop-redirect-type">
                  {{ link.redirectType }}
                </v-list-item-subtitle>
              </v-list-item>
              <template v-if="link.expiresAt">
                <v-divider />
                <v-list-item>
                  <template #prepend>
                    <v-icon class="mr-2">mdi-calendar-clock</v-icon>
                  </template>
                  <v-list-item-title>Expires at</v-list-item-title>
                  <v-list-item-subtitle data-testid="prop-expires-at">
                    {{ formatDate(link.expiresAt) }}
                  </v-list-item-subtitle>
                </v-list-item>
              </template>
              <template v-if="link.maxClicks">
                <v-divider />
                <v-list-item>
                  <template #prepend>
                    <v-icon class="mr-2">mdi-cursor-pointer</v-icon>
                  </template>
                  <v-list-item-title>Click limit</v-list-item-title>
                  <v-list-item-subtitle data-testid="prop-max-clicks">
                    {{ link.clickCount }} / {{ link.maxClicks }}
                  </v-list-item-subtitle>
                </v-list-item>
              </template>
              <v-divider />
              <v-list-item>
                <template #prepend>
                  <v-icon class="mr-2">mdi-calendar-plus</v-icon>
                </template>
                <v-list-item-title>Created</v-list-item-title>
                <v-list-item-subtitle data-testid="prop-created-at">
                  {{ formatDate(link.createdAt) }}
                </v-list-item-subtitle>
              </v-list-item>
              <template v-if="link.updatedAt && link.updatedAt !== link.createdAt">
                <v-divider />
                <v-list-item>
                  <template #prepend>
                    <v-icon class="mr-2">mdi-calendar-edit</v-icon>
                  </template>
                  <v-list-item-title>Last updated</v-list-item-title>
                  <v-list-item-subtitle data-testid="prop-updated-at">
                    {{ formatDate(link.updatedAt) }}
                  </v-list-item-subtitle>
                </v-list-item>
              </template>
            </v-list>
          </v-card>
        </v-col>
      </v-row>

      <!-- Audit trail -->
      <v-row>
        <v-col>
          <v-card data-testid="audit-trail-card">
            <v-card-title class="d-flex align-center">
              <v-icon class="mr-2">mdi-history</v-icon>
              Link history
            </v-card-title>
            <v-timeline density="compact" truncate-line="both" data-testid="audit-trail-timeline">
              <v-timeline-item
                v-for="entry in link.auditTrail"
                :key="entry.version"
                :dot-color="entry.action === 'created' ? 'success' : 'primary'"
                size="x-small"
              >
                <div>
                  <strong>v{{ entry.version }}</strong>
                  <span class="text-medium-emphasis"> — {{ entry.description }}</span>
                </div>
                <div class="text-caption text-medium-emphasis">
                  {{ formatDate(entry.timestamp) }}
                </div>
              </v-timeline-item>
              <v-timeline-item v-if="link.auditTrail.length === 0" dot-color="grey" size="x-small">
                <span class="text-medium-emphasis">No history recorded</span>
              </v-timeline-item>
            </v-timeline>
          </v-card>
        </v-col>
      </v-row>
    </template>

    <!-- Dialogs -->
    <EditLinkDialog
      v-if="link"
      v-model="showEditDialog"
      :link="link as unknown as LinkListItem"
      @updated="onUpdated"
    />

    <DeleteLinkDialog
      v-if="link"
      v-model="showDeleteDialog"
      :link="link as unknown as LinkListItem"
      @deleted="onDeleted"
    />

    <!-- QR Code dialog -->
    <QrCodeDialog v-if="link" v-model="showQrDialog" :short-url="link.shortUrl" />
  </v-container>
</template>
