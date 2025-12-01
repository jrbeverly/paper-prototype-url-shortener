<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useDomain, useVerifyDomain, useUpdateDomain } from '@/composables/useDomains'
import DeleteDomainDialog from '@/components/domains/DeleteDomainDialog.vue'
import type {
  CertificateStatus,
  DomainStatus,
  DomainVerifyResult,
  NotFoundBehavior,
} from '@/types/domains'

const route = useRoute()
const router = useRouter()

const domainId = computed(() => route.params.id as string)

const { data: domain, isLoading, isError } = useDomain(domainId)
const verifyMutation = useVerifyDomain()
const updateMutation = useUpdateDomain(domainId)

const showDeleteDialog = ref(false)
const verifyResult = ref<DomainVerifyResult | null>(null)
const showCopiedTxt = ref(false)
const showCopiedCname = ref(false)

// Settings form state — initialised from domain data when it loads
const defaultRedirectUrl = ref('')
const notFoundBehavior = ref<NotFoundBehavior>('404')

watch(
  domain,
  (d) => {
    if (d) {
      defaultRedirectUrl.value = d.settings.defaultRedirectUrl ?? ''
      notFoundBehavior.value = d.settings.notFoundBehavior
    }
  },
  { immediate: true }
)

const notFoundOptions = [
  { value: '404', title: 'Show 404 page', subtitle: 'Return a 404 error page to visitors' },
  {
    value: 'redirect',
    title: 'Redirect to default URL',
    subtitle: 'Redirect to the default redirect URL',
  },
  { value: 'passthrough', title: 'Pass through', subtitle: 'Forward the request unchanged' },
]

const needsVerification = computed(
  () =>
    domain.value?.status === 'pending_verification' ||
    domain.value?.status === 'verification_failed'
)

async function handleVerify() {
  verifyResult.value = null
  verifyMutation.reset()
  verifyResult.value = await verifyMutation.mutateAsync(domainId.value)
}

async function handleSaveSettings() {
  await updateMutation.mutateAsync({
    defaultRedirectUrl: defaultRedirectUrl.value.trim() || null,
    notFoundBehavior: notFoundBehavior.value,
  })
}

async function copyToClipboard(text: string, field: 'txt' | 'cname') {
  await navigator.clipboard.writeText(text)
  if (field === 'txt') {
    showCopiedTxt.value = true
    setTimeout(() => {
      showCopiedTxt.value = false
    }, 2000)
  } else {
    showCopiedCname.value = true
    setTimeout(() => {
      showCopiedCname.value = false
    }, 2000)
  }
}

function onDeleted() {
  router.push({ name: 'domains' })
}

function statusColor(status: DomainStatus): string {
  const colors: Record<DomainStatus, string> = {
    active: 'success',
    pending_verification: 'warning',
    verifying: 'info',
    verification_failed: 'error',
  }
  return colors[status] ?? 'default'
}

function statusLabel(status: DomainStatus): string {
  const labels: Record<DomainStatus, string> = {
    active: 'Active',
    pending_verification: 'Pending verification',
    verifying: 'Verifying…',
    verification_failed: 'Verification failed',
  }
  return labels[status] ?? status
}

function certColor(status: CertificateStatus): string {
  const colors: Record<CertificateStatus, string> = {
    issued: 'success',
    pending: 'warning',
    failed: 'error',
  }
  return colors[status] ?? 'default'
}

function certLabel(status: CertificateStatus): string {
  const labels: Record<CertificateStatus, string> = {
    issued: 'Issued',
    pending: 'Pending',
    failed: 'Failed',
  }
  return labels[status] ?? status
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
    <!-- Loading state -->
    <template v-if="isLoading">
      <v-row>
        <v-col>
          <v-skeleton-loader type="heading" class="mb-4" data-testid="loading-skeleton" />
          <v-skeleton-loader type="card" />
        </v-col>
      </v-row>
    </template>

    <!-- Error state -->
    <v-row v-else-if="isError">
      <v-col>
        <v-alert type="error" variant="tonal" data-testid="load-error">
          Failed to load domain. Please refresh the page.
        </v-alert>
      </v-col>
    </v-row>

    <!-- Domain detail -->
    <template v-else-if="domain">
      <!-- Page header -->
      <v-row>
        <v-col>
          <div class="d-flex align-center flex-wrap ga-3 mb-1">
            <h1 class="text-h4" data-testid="domain-hostname">{{ domain.hostname }}</h1>
            <v-chip :color="statusColor(domain.status)" label data-testid="status-chip">
              {{ statusLabel(domain.status) }}
            </v-chip>
            <v-chip
              :color="certColor(domain.certificateStatus)"
              label
              variant="tonal"
              data-testid="cert-status-chip"
            >
              <v-icon start size="small">mdi-certificate-outline</v-icon>
              SSL: {{ certLabel(domain.certificateStatus) }}
            </v-chip>
          </div>
          <p class="text-body-2 text-medium-emphasis">
            Added {{ formatDate(domain.createdAt) }} · {{ domain.linkCount }}
            {{ domain.linkCount === 1 ? 'link' : 'links' }}
          </p>
        </v-col>
      </v-row>

      <!-- DNS setup card — shown when verification is needed -->
      <v-row v-if="needsVerification">
        <v-col>
          <v-card data-testid="dns-instructions-card">
            <v-card-item>
              <template #prepend>
                <v-icon color="warning">mdi-dns-outline</v-icon>
              </template>
              <v-card-title>Add DNS records</v-card-title>
              <v-card-subtitle>
                Add these records at your DNS provider to verify ownership and enable redirects.
              </v-card-subtitle>
            </v-card-item>

            <v-card-text v-if="domain.verificationInstructions">
              <!-- TXT record -->
              <p class="text-body-2 font-weight-medium mb-2">1. TXT record — verify ownership</p>
              <v-table density="compact" class="mb-6">
                <thead>
                  <tr>
                    <th class="text-left">Name / Host</th>
                    <th class="text-left">Value</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  <tr>
                    <td>
                      <code data-testid="txt-name">{{
                        domain.verificationInstructions.txtName
                      }}</code>
                    </td>
                    <td>
                      <code data-testid="txt-value">{{
                        domain.verificationInstructions.txtValue
                      }}</code>
                    </td>
                    <td class="text-right">
                      <v-btn
                        :icon="showCopiedTxt ? 'mdi-check' : 'mdi-content-copy'"
                        :color="showCopiedTxt ? 'success' : undefined"
                        variant="text"
                        size="small"
                        data-testid="copy-txt-btn"
                        @click="copyToClipboard(domain.verificationInstructions!.txtValue, 'txt')"
                      />
                    </td>
                  </tr>
                </tbody>
              </v-table>

              <!-- CNAME record -->
              <p class="text-body-2 font-weight-medium mb-2">2. CNAME record — point to Short.io</p>
              <v-table density="compact">
                <thead>
                  <tr>
                    <th class="text-left">Name / Host</th>
                    <th class="text-left">Target / Value</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  <tr>
                    <td>
                      <code data-testid="cname-name">{{
                        domain.verificationInstructions.cnameName
                      }}</code>
                    </td>
                    <td>
                      <code data-testid="cname-value">{{
                        domain.verificationInstructions.cnameValue
                      }}</code>
                    </td>
                    <td class="text-right">
                      <v-btn
                        :icon="showCopiedCname ? 'mdi-check' : 'mdi-content-copy'"
                        :color="showCopiedCname ? 'success' : undefined"
                        variant="text"
                        size="small"
                        data-testid="copy-cname-btn"
                        @click="
                          copyToClipboard(domain.verificationInstructions!.cnameValue, 'cname')
                        "
                      />
                    </td>
                  </tr>
                </tbody>
              </v-table>
            </v-card-text>

            <v-divider />

            <v-card-actions class="pa-4 d-flex align-center ga-2">
              <v-btn
                color="primary"
                variant="tonal"
                prepend-icon="mdi-refresh"
                :loading="verifyMutation.isPending.value"
                data-testid="verify-btn"
                @click="handleVerify"
              >
                Check DNS records
              </v-btn>
              <v-alert
                v-if="verifyMutation.isError.value"
                type="error"
                variant="tonal"
                density="compact"
                class="flex-grow-1"
                data-testid="verify-error"
              >
                Verification check failed. Please try again.
              </v-alert>
            </v-card-actions>
          </v-card>
        </v-col>
      </v-row>

      <!-- Verification result panel -->
      <v-row v-if="verifyResult">
        <v-col>
          <v-card data-testid="verify-result-card">
            <v-card-item>
              <template #prepend>
                <v-icon :color="verifyResult.status === 'active' ? 'success' : 'error'">
                  {{ verifyResult.status === 'active' ? 'mdi-check-circle' : 'mdi-alert-circle' }}
                </v-icon>
              </template>
              <v-card-title>DNS check result</v-card-title>
              <v-card-subtitle data-testid="verify-result-message">
                {{ verifyResult.message }}
              </v-card-subtitle>
            </v-card-item>

            <v-card-text>
              <v-list lines="two">
                <v-list-item :data-testid="`verify-txt-result`">
                  <template #prepend>
                    <v-icon :color="verifyResult.txtCheck.passed ? 'success' : 'error'">
                      {{
                        verifyResult.txtCheck.passed
                          ? 'mdi-check-circle-outline'
                          : 'mdi-close-circle-outline'
                      }}
                    </v-icon>
                  </template>
                  <v-list-item-title>TXT record</v-list-item-title>
                  <v-list-item-subtitle>
                    <template v-if="verifyResult.txtCheck.passed"
                      >Record found and matches</template
                    >
                    <template v-else>
                      {{
                        verifyResult.txtCheck.error ?? `Expected: ${verifyResult.txtCheck.expected}`
                      }}
                    </template>
                  </v-list-item-subtitle>
                </v-list-item>

                <v-list-item :data-testid="`verify-cname-result`">
                  <template #prepend>
                    <v-icon :color="verifyResult.cnameCheck.passed ? 'success' : 'error'">
                      {{
                        verifyResult.cnameCheck.passed
                          ? 'mdi-check-circle-outline'
                          : 'mdi-close-circle-outline'
                      }}
                    </v-icon>
                  </template>
                  <v-list-item-title>CNAME record</v-list-item-title>
                  <v-list-item-subtitle>
                    <template v-if="verifyResult.cnameCheck.passed"
                      >Record found and matches</template
                    >
                    <template v-else>
                      {{
                        verifyResult.cnameCheck.error ??
                        `Expected: ${verifyResult.cnameCheck.expected}`
                      }}
                    </template>
                  </v-list-item-subtitle>
                </v-list-item>
              </v-list>
            </v-card-text>
          </v-card>
        </v-col>
      </v-row>

      <!-- Domain settings -->
      <v-row>
        <v-col>
          <v-card data-testid="settings-card">
            <v-card-item>
              <template #prepend>
                <v-icon color="primary">mdi-cog-outline</v-icon>
              </template>
              <v-card-title>Domain settings</v-card-title>
            </v-card-item>

            <v-card-text>
              <v-text-field
                v-model="defaultRedirectUrl"
                label="Default redirect URL"
                placeholder="https://example.com"
                variant="outlined"
                hint="Visitors hitting the domain root are redirected here (when 404 behavior is set to redirect)."
                persistent-hint
                class="mb-4"
                data-testid="default-redirect-input"
              />

              <v-select
                v-model="notFoundBehavior"
                label="Not found behavior"
                :items="notFoundOptions"
                item-value="value"
                item-title="title"
                variant="outlined"
                data-testid="not-found-select"
              >
                <template #item="{ props: itemProps, item }">
                  <v-list-item v-bind="itemProps" :subtitle="item.raw.subtitle" />
                </template>
              </v-select>

              <v-alert
                v-if="updateMutation.isError.value"
                type="error"
                variant="tonal"
                class="mt-3"
                data-testid="settings-error"
              >
                Failed to save settings. Please try again.
              </v-alert>

              <v-alert
                v-if="updateMutation.isSuccess.value"
                type="success"
                variant="tonal"
                class="mt-3"
                data-testid="settings-saved"
              >
                Settings saved.
              </v-alert>
            </v-card-text>

            <v-divider />

            <v-card-actions class="pa-4">
              <v-spacer />
              <v-btn
                color="primary"
                variant="flat"
                :loading="updateMutation.isPending.value"
                data-testid="save-settings-btn"
                @click="handleSaveSettings"
              >
                Save settings
              </v-btn>
            </v-card-actions>
          </v-card>
        </v-col>
      </v-row>

      <!-- Danger zone -->
      <v-row>
        <v-col>
          <v-card data-testid="danger-zone-card">
            <v-card-item>
              <template #prepend>
                <v-icon color="error">mdi-alert-outline</v-icon>
              </template>
              <v-card-title class="text-error">Danger zone</v-card-title>
            </v-card-item>

            <v-card-text>
              <div class="d-flex align-center justify-space-between flex-wrap ga-4">
                <div>
                  <p class="text-body-2 font-weight-medium mb-1">Delete this domain</p>
                  <p class="text-body-2 text-medium-emphasis">
                    All links on this domain will be disabled. This action cannot be undone.
                  </p>
                </div>
                <v-btn
                  color="error"
                  variant="outlined"
                  prepend-icon="mdi-delete-outline"
                  data-testid="delete-domain-btn"
                  @click="showDeleteDialog = true"
                >
                  Delete domain
                </v-btn>
              </div>
            </v-card-text>
          </v-card>
        </v-col>
      </v-row>
    </template>

    <!-- Delete confirmation dialog -->
    <DeleteDomainDialog
      v-if="domain"
      v-model="showDeleteDialog"
      :domain="domain"
      @deleted="onDeleted"
    />
  </v-container>
</template>
