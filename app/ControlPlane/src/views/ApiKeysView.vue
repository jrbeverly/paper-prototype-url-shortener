<script setup lang="ts">
import { ref } from 'vue'
import { useApiKeys } from '@/composables/useApiKeys'
import CreateApiKeyDialog from '@/components/api-keys/CreateApiKeyDialog.vue'
import KeyRevealDialog from '@/components/api-keys/KeyRevealDialog.vue'
import RevokeApiKeyDialog from '@/components/api-keys/RevokeApiKeyDialog.vue'
import type { ApiKey, ApiKeyCreated } from '@/types/apiKeys'

const { data: apiKeys, isLoading, isError } = useApiKeys()

const showCreateDialog = ref(false)
const createdKey = ref<ApiKeyCreated | null>(null)
const showRevealDialog = ref(false)

const keyToRevoke = ref<ApiKey | null>(null)
const showRevokeDialog = ref(false)

function onKeyCreated(key: ApiKeyCreated) {
  createdKey.value = key
  showRevealDialog.value = true
}

function onRevealDialogClose() {
  createdKey.value = null
}

function openRevokeDialog(key: ApiKey) {
  keyToRevoke.value = key
  showRevokeDialog.value = true
}

function onRevokeDialogClose() {
  keyToRevoke.value = null
}

function formatDate(iso: string): string {
  return new Intl.DateTimeFormat('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  }).format(new Date(iso))
}

function roleLabel(permissions: string[]): string {
  const labels: Record<string, string> = {
    owner: 'Owner',
    admin: 'Admin',
    member: 'Member',
    viewer: 'Viewer',
  }
  return labels[permissions[0]] ?? permissions[0] ?? '—'
}
</script>

<template>
  <v-container>
    <v-row>
      <v-col class="d-flex align-center">
        <div class="flex-grow-1">
          <h1 class="text-h4 mb-2">API Keys</h1>
          <p class="text-body-1 text-medium-emphasis">
            Manage API keys for programmatic access to the Short.io API.
          </p>
        </div>
        <v-btn
          color="primary"
          variant="flat"
          prepend-icon="mdi-plus"
          :disabled="isLoading"
          data-testid="create-key-btn"
          @click="showCreateDialog = true"
        >
          Create API key
        </v-btn>
      </v-col>
    </v-row>

    <!-- Error state -->
    <v-row v-if="isError">
      <v-col>
        <v-alert type="error" variant="tonal" data-testid="list-error">
          Failed to load API keys. Please refresh the page.
        </v-alert>
      </v-col>
    </v-row>

    <!-- Keys table -->
    <v-row>
      <v-col>
        <v-card :loading="isLoading" data-testid="keys-card">
          <!-- Loading skeleton -->
          <template v-if="isLoading">
            <v-card-text>
              <v-skeleton-loader v-for="n in 3" :key="n" type="table-row" class="mb-1" />
            </v-card-text>
          </template>

          <!-- Empty state -->
          <template v-else-if="!apiKeys || apiKeys.length === 0">
            <v-card-text>
              <div class="text-center py-12" data-testid="empty-state">
                <v-icon size="56" color="medium-emphasis" class="mb-3"> mdi-key-outline </v-icon>
                <p class="text-h6 text-medium-emphasis mb-1">No API keys yet</p>
                <p class="text-body-2 text-medium-emphasis mb-4">
                  Create an API key to access the Short.io API programmatically.
                </p>
                <v-btn
                  color="primary"
                  variant="flat"
                  prepend-icon="mdi-plus"
                  data-testid="empty-create-btn"
                  @click="showCreateDialog = true"
                >
                  Create your first API key
                </v-btn>
              </div>
            </v-card-text>
          </template>

          <!-- Keys list -->
          <template v-else>
            <v-table data-testid="keys-table">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Key</th>
                  <th>Role</th>
                  <th>Created</th>
                  <th>Last used</th>
                  <th>Status</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="key in apiKeys"
                  :key="key.id"
                  :data-testid="`key-row-${key.id}`"
                  :class="{ 'text-medium-emphasis': key.isRevoked }"
                >
                  <td class="font-weight-medium" :data-testid="`key-name-${key.id}`">
                    {{ key.name }}
                  </td>
                  <td>
                    <code
                      class="text-body-2"
                      style="font-family: monospace"
                      :data-testid="`key-preview-${key.id}`"
                    >
                      {{ key.keyPreview }}
                    </code>
                  </td>
                  <td :data-testid="`key-role-${key.id}`">
                    {{ roleLabel(key.permissions) }}
                  </td>
                  <td class="text-no-wrap" :data-testid="`key-created-${key.id}`">
                    {{ formatDate(key.createdAt) }}
                  </td>
                  <td class="text-no-wrap" :data-testid="`key-last-used-${key.id}`">
                    {{ key.lastUsedAt ? formatDate(key.lastUsedAt) : 'Never' }}
                  </td>
                  <td :data-testid="`key-status-${key.id}`">
                    <v-chip
                      :color="key.isRevoked ? 'default' : 'success'"
                      size="small"
                      label
                      :data-testid="`key-status-chip-${key.id}`"
                    >
                      {{ key.isRevoked ? 'Revoked' : 'Active' }}
                    </v-chip>
                  </td>
                  <td class="text-right">
                    <v-btn
                      v-if="!key.isRevoked"
                      variant="text"
                      color="error"
                      size="small"
                      :data-testid="`revoke-btn-${key.id}`"
                      @click="openRevokeDialog(key)"
                    >
                      Revoke
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
    <CreateApiKeyDialog v-model="showCreateDialog" @created="onKeyCreated" />

    <KeyRevealDialog
      v-if="createdKey"
      v-model="showRevealDialog"
      :created-key="createdKey"
      @update:model-value="
        (v) => {
          if (!v) onRevealDialogClose()
        }
      "
    />

    <RevokeApiKeyDialog
      v-if="keyToRevoke"
      v-model="showRevokeDialog"
      :api-key="keyToRevoke"
      @update:model-value="
        (v) => {
          if (!v) onRevokeDialogClose()
        }
      "
    />
  </v-container>
</template>
