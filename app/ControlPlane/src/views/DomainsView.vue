<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useDomains } from '@/composables/useDomains'
import AddDomainDialog from '@/components/domains/AddDomainDialog.vue'
import DeleteDomainDialog from '@/components/domains/DeleteDomainDialog.vue'
import type { CertificateStatus, Domain, DomainCreated, DomainStatus } from '@/types/domains'

const router = useRouter()
const { data: domainList, isLoading, isError } = useDomains()

const showAddDialog = ref(false)
const domainToDelete = ref<Domain | null>(null)
const showDeleteDialog = ref(false)

function onDomainCreated(created: DomainCreated) {
  router.push({ name: 'domain-detail', params: { id: created.id } })
}

function openDeleteDialog(domain: Domain) {
  domainToDelete.value = domain
  showDeleteDialog.value = true
}

function onDeleteDialogClose() {
  domainToDelete.value = null
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
    pending_verification: 'Pending',
    verifying: 'Verifying',
    verification_failed: 'Failed',
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
    <v-row>
      <v-col class="d-flex align-center">
        <div class="flex-grow-1">
          <h1 class="text-h4 mb-2">Domains</h1>
          <p class="text-body-1 text-medium-emphasis">
            Manage your branded custom domains for short links.
          </p>
        </div>
        <v-btn
          color="primary"
          variant="flat"
          prepend-icon="mdi-plus"
          :disabled="isLoading"
          data-testid="add-domain-btn"
          @click="showAddDialog = true"
        >
          Add domain
        </v-btn>
      </v-col>
    </v-row>

    <!-- Error state -->
    <v-row v-if="isError">
      <v-col>
        <v-alert type="error" variant="tonal" data-testid="list-error">
          Failed to load domains. Please refresh the page.
        </v-alert>
      </v-col>
    </v-row>

    <!-- Domains table -->
    <v-row>
      <v-col>
        <v-card :loading="isLoading" data-testid="domains-card">
          <!-- Loading skeleton -->
          <template v-if="isLoading">
            <v-card-text>
              <v-skeleton-loader v-for="n in 3" :key="n" type="table-row" class="mb-1" />
            </v-card-text>
          </template>

          <!-- Empty state -->
          <template v-else-if="!domainList || domainList.items.length === 0">
            <v-card-text>
              <div class="text-center py-12" data-testid="empty-state">
                <v-icon size="56" color="medium-emphasis" class="mb-3">mdi-web-off</v-icon>
                <p class="text-h6 text-medium-emphasis mb-1">No domains yet</p>
                <p class="text-body-2 text-medium-emphasis mb-4">
                  Add a custom domain to brand your short links.
                </p>
                <v-btn
                  color="primary"
                  variant="flat"
                  prepend-icon="mdi-plus"
                  data-testid="empty-add-btn"
                  @click="showAddDialog = true"
                >
                  Add your first domain
                </v-btn>
              </div>
            </v-card-text>
          </template>

          <!-- Domains list -->
          <template v-else>
            <v-table data-testid="domains-table">
              <thead>
                <tr>
                  <th>Hostname</th>
                  <th>Status</th>
                  <th>Certificate</th>
                  <th>Links</th>
                  <th>Added</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="domain in domainList.items"
                  :key="domain.id"
                  :data-testid="`domain-row-${domain.id}`"
                  class="domain-row"
                  @click="router.push({ name: 'domain-detail', params: { id: domain.id } })"
                >
                  <td class="font-weight-medium" :data-testid="`domain-hostname-${domain.id}`">
                    {{ domain.hostname }}
                  </td>
                  <td :data-testid="`domain-status-${domain.id}`">
                    <v-chip
                      :color="statusColor(domain.status)"
                      size="small"
                      label
                      :data-testid="`domain-status-chip-${domain.id}`"
                    >
                      {{ statusLabel(domain.status) }}
                    </v-chip>
                  </td>
                  <td :data-testid="`domain-cert-${domain.id}`">
                    <v-chip
                      :color="certColor(domain.certificateStatus)"
                      size="small"
                      label
                      :data-testid="`domain-cert-chip-${domain.id}`"
                    >
                      {{ certLabel(domain.certificateStatus) }}
                    </v-chip>
                  </td>
                  <td :data-testid="`domain-links-${domain.id}`">
                    {{ domain.linkCount }}
                  </td>
                  <td class="text-no-wrap" :data-testid="`domain-created-${domain.id}`">
                    {{ formatDate(domain.createdAt) }}
                  </td>
                  <td class="text-right" @click.stop>
                    <v-btn
                      icon
                      variant="text"
                      color="error"
                      size="small"
                      :data-testid="`domain-delete-btn-${domain.id}`"
                      @click="openDeleteDialog(domain)"
                    >
                      <v-icon>mdi-delete-outline</v-icon>
                    </v-btn>
                  </td>
                </tr>
              </tbody>
            </v-table>
          </template>
        </v-card>
      </v-col>
    </v-row>

    <!-- Dialogs -->
    <AddDomainDialog v-model="showAddDialog" @created="onDomainCreated" />

    <DeleteDomainDialog
      v-if="domainToDelete"
      v-model="showDeleteDialog"
      :domain="domainToDelete"
      @update:model-value="
        (v) => {
          if (!v) onDeleteDialogClose()
        }
      "
    />
  </v-container>
</template>

<style scoped>
.domain-row {
  cursor: pointer;
}
</style>
