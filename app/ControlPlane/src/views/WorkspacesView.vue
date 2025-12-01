<script setup lang="ts">
// Workspace settings page — general, branding, defaults, team stub, danger zone
import { ref, watch } from 'vue'
import type { VForm } from 'vuetify/components'
import { useWorkspaceStore } from '@/stores/workspace'
import { useUIStore } from '@/stores/ui'
import { useWorkspaceSettings, useUpdateWorkspaceSettings } from '@/composables/useWorkspace'
import DeleteWorkspaceDialog from '@/components/workspace/DeleteWorkspaceDialog.vue'

const workspaceStore = useWorkspaceStore()
const uiStore = useUIStore()
const workspaceId = workspaceStore.currentWorkspaceId ?? 'ws_default'

const { data: settings, isLoading, isError } = useWorkspaceSettings(workspaceId)
const updateSettings = useUpdateWorkspaceSettings(workspaceId)

// ── General ───────────────────────────────────────────────────────────────────
const generalFormRef = ref<VForm | null>(null)
const name = ref('')
const slug = ref('')
const generalSuccess = ref(false)
const generalError = ref<string | null>(null)

// ── Branding ──────────────────────────────────────────────────────────────────
const primaryColor = ref('#1867C0')
const showColorPicker = ref(false)
const brandingSuccess = ref(false)
const brandingError = ref<string | null>(null)

// ── Defaults ──────────────────────────────────────────────────────────────────
const defaultsFormRef = ref<VForm | null>(null)
const fallback404Url = ref('')
const defaultRedirectType = ref<'301' | '302'>('302')
const defaultsSuccess = ref(false)
const defaultsError = ref<string | null>(null)

// ── Danger Zone ───────────────────────────────────────────────────────────────
const showDeleteDialog = ref(false)

watch(
  settings,
  (s) => {
    if (!s) return
    name.value = s.name
    slug.value = s.slug
    primaryColor.value = s.branding.primaryColor
    fallback404Url.value = s.defaults.fallback404Url ?? ''
    defaultRedirectType.value = s.defaults.defaultRedirectType
  },
  { immediate: true }
)

// ── Validation rules ──────────────────────────────────────────────────────────
const nameRules = [
  (v: string) => !!v.trim() || 'Workspace name is required',
  (v: string) => v.trim().length >= 2 || 'Name must be at least 2 characters',
  (v: string) => v.trim().length <= 80 || 'Name must be 80 characters or fewer',
]

const slugRules = [
  (v: string) => !!v.trim() || 'Slug is required',
  (v: string) =>
    /^[a-z0-9-]+$/.test(v) || 'Slug may only contain lowercase letters, numbers, and hyphens',
  (v: string) => v.length >= 2 || 'Slug must be at least 2 characters',
  (v: string) => v.length <= 50 || 'Slug must be 50 characters or fewer',
]

const urlRules = [
  (v: string) => {
    if (!v) return true
    try {
      new URL(v)
      return true
    } catch {
      return 'Must be a valid URL (e.g. https://example.com/404)'
    }
  },
]

const redirectTypeItems = [
  { title: '302 — Temporary redirect (default)', value: '302' },
  { title: '301 — Permanent redirect (cached by browsers)', value: '301' },
]

// ── Submit handlers ───────────────────────────────────────────────────────────
async function submitGeneral() {
  generalError.value = null
  generalSuccess.value = false
  const { valid } = await generalFormRef.value!.validate()
  if (!valid) return
  try {
    await updateSettings.mutateAsync({ name: name.value.trim(), slug: slug.value.trim() })
    generalSuccess.value = true
    uiStore.addNotification({ message: 'Workspace settings saved.', type: 'success' })
  } catch {
    generalError.value = 'Failed to save workspace settings. Please try again.'
  }
}

async function submitBranding() {
  brandingError.value = null
  brandingSuccess.value = false
  try {
    await updateSettings.mutateAsync({ branding: { primaryColor: primaryColor.value } })
    brandingSuccess.value = true
    uiStore.addNotification({ message: 'Branding saved.', type: 'success' })
  } catch {
    brandingError.value = 'Failed to save branding. Please try again.'
  }
}

async function submitDefaults() {
  defaultsError.value = null
  defaultsSuccess.value = false
  const { valid } = await defaultsFormRef.value!.validate()
  if (!valid) return
  try {
    await updateSettings.mutateAsync({
      defaults: {
        fallback404Url: fallback404Url.value.trim() || null,
        defaultRedirectType: defaultRedirectType.value,
      },
    })
    defaultsSuccess.value = true
    uiStore.addNotification({ message: 'Default settings saved.', type: 'success' })
  } catch {
    defaultsError.value = 'Failed to save default settings. Please try again.'
  }
}
</script>

<template>
  <v-container>
    <!-- Header -->
    <v-row>
      <v-col>
        <h1 class="text-h4 mb-2">Workspace</h1>
        <p class="text-body-1 text-medium-emphasis">Manage your workspace settings and branding.</p>
      </v-col>
    </v-row>

    <!-- Load error -->
    <v-row v-if="isError">
      <v-col>
        <v-alert type="error" variant="tonal" data-testid="load-error">
          Failed to load workspace settings. Please refresh the page.
        </v-alert>
      </v-col>
    </v-row>

    <!-- General -->
    <v-row>
      <v-col>
        <v-card :loading="isLoading" data-testid="general-card">
          <v-card-title class="pa-4 pb-0">General</v-card-title>
          <v-card-subtitle class="px-4">Update your workspace name and URL slug.</v-card-subtitle>
          <v-card-text>
            <v-alert
              v-if="generalError"
              type="error"
              density="compact"
              class="mb-4"
              data-testid="general-error"
              >{{ generalError }}</v-alert
            >
            <v-alert
              v-if="generalSuccess"
              type="success"
              density="compact"
              class="mb-4"
              data-testid="general-success"
              >Workspace settings saved.</v-alert
            >

            <v-form ref="generalFormRef" @submit.prevent="submitGeneral">
              <v-text-field
                v-model="name"
                label="Workspace name"
                :rules="nameRules"
                class="mb-2"
                data-testid="name-input"
              />
              <v-text-field
                v-model="slug"
                label="Slug"
                hint="Used in your workspace URL. Lowercase letters, numbers, and hyphens only."
                persistent-hint
                :rules="slugRules"
                data-testid="slug-input"
              />
            </v-form>
          </v-card-text>
          <v-card-actions class="px-4 pb-4">
            <v-btn
              color="primary"
              variant="flat"
              :loading="updateSettings.isPending.value"
              data-testid="general-save-btn"
              @click="submitGeneral"
            >
              Save changes
            </v-btn>
          </v-card-actions>
        </v-card>
      </v-col>
    </v-row>

    <!-- Branding -->
    <v-row>
      <v-col>
        <v-card data-testid="branding-card">
          <v-card-title class="pa-4 pb-0">Branding</v-card-title>
          <v-card-subtitle class="px-4">Customize your workspace appearance.</v-card-subtitle>
          <v-card-text>
            <v-alert
              v-if="brandingError"
              type="error"
              density="compact"
              class="mb-4"
              data-testid="branding-error"
              >{{ brandingError }}</v-alert
            >
            <v-alert
              v-if="brandingSuccess"
              type="success"
              density="compact"
              class="mb-4"
              data-testid="branding-success"
              >Branding saved.</v-alert
            >

            <!-- Logo upload placeholder -->
            <div class="mb-6">
              <div class="text-body-2 font-weight-medium mb-2">Logo</div>
              <div class="d-flex align-center gap-4">
                <v-avatar color="surface-variant" size="64" rounded="lg" data-testid="logo-preview">
                  <v-icon size="32" color="medium-emphasis">mdi-image-outline</v-icon>
                </v-avatar>
                <div>
                  <v-btn
                    variant="outlined"
                    prepend-icon="mdi-upload"
                    size="small"
                    data-testid="logo-upload-btn"
                    disabled
                  >
                    Upload logo
                  </v-btn>
                  <div class="text-caption text-medium-emphasis mt-1">
                    PNG or SVG, max 1 MB. Coming soon.
                  </div>
                </div>
              </div>
            </div>

            <!-- Primary color -->
            <div>
              <div class="text-body-2 font-weight-medium mb-2">Primary color</div>
              <div class="d-flex align-center gap-3 flex-wrap">
                <v-menu
                  v-model="showColorPicker"
                  :close-on-content-click="false"
                  location="bottom start"
                >
                  <template #activator="{ props: menuProps }">
                    <v-btn v-bind="menuProps" variant="outlined" data-testid="color-picker-btn">
                      <template #prepend>
                        <span
                          class="color-swatch"
                          :style="{ background: primaryColor }"
                          data-testid="color-swatch"
                        />
                      </template>
                      {{ primaryColor }}
                    </v-btn>
                  </template>
                  <v-card>
                    <v-color-picker
                      v-model="primaryColor"
                      mode="hex"
                      hide-inputs
                      data-testid="color-picker"
                    />
                    <v-card-actions>
                      <v-spacer />
                      <v-btn size="small" variant="text" @click="showColorPicker = false"
                        >Done</v-btn
                      >
                    </v-card-actions>
                  </v-card>
                </v-menu>

                <!-- Live preview -->
                <v-chip :color="primaryColor" variant="flat" data-testid="color-preview-chip">
                  Preview
                </v-chip>
              </div>
              <div class="text-caption text-medium-emphasis mt-2">
                Used for buttons and accent elements in your workspace.
              </div>
            </div>
          </v-card-text>
          <v-card-actions class="px-4 pb-4">
            <v-btn
              color="primary"
              variant="flat"
              :loading="updateSettings.isPending.value"
              data-testid="branding-save-btn"
              @click="submitBranding"
            >
              Save branding
            </v-btn>
          </v-card-actions>
        </v-card>
      </v-col>
    </v-row>

    <!-- Defaults -->
    <v-row>
      <v-col>
        <v-card data-testid="defaults-card">
          <v-card-title class="pa-4 pb-0">Redirect Defaults</v-card-title>
          <v-card-subtitle class="px-4">
            Default behavior for new links created in this workspace.
          </v-card-subtitle>
          <v-card-text>
            <v-alert
              v-if="defaultsError"
              type="error"
              density="compact"
              class="mb-4"
              data-testid="defaults-error"
              >{{ defaultsError }}</v-alert
            >
            <v-alert
              v-if="defaultsSuccess"
              type="success"
              density="compact"
              class="mb-4"
              data-testid="defaults-success"
              >Default settings saved.</v-alert
            >

            <v-form ref="defaultsFormRef" @submit.prevent="submitDefaults">
              <v-select
                v-model="defaultRedirectType"
                :items="redirectTypeItems"
                item-title="title"
                item-value="value"
                label="Default redirect type"
                variant="outlined"
                class="mb-4"
                data-testid="redirect-type-select"
              />
              <v-text-field
                v-model="fallback404Url"
                label="404 fallback URL"
                placeholder="https://example.com/not-found"
                hint="Where visitors land when a short link is not found. Leave blank to show the default 404 page."
                persistent-hint
                :rules="urlRules"
                data-testid="fallback-url-input"
              />
            </v-form>
          </v-card-text>
          <v-card-actions class="px-4 pb-4">
            <v-btn
              color="primary"
              variant="flat"
              :loading="updateSettings.isPending.value"
              data-testid="defaults-save-btn"
              @click="submitDefaults"
            >
              Save defaults
            </v-btn>
          </v-card-actions>
        </v-card>
      </v-col>
    </v-row>

    <!-- Team members (stub) -->
    <v-row>
      <v-col>
        <v-card data-testid="team-card">
          <v-card-title class="pa-4 pb-0">Team Members</v-card-title>
          <v-card-subtitle class="px-4"
            >Invite and manage people in this workspace.</v-card-subtitle
          >
          <v-card-text>
            <div class="text-center py-8" data-testid="team-coming-soon">
              <v-icon size="48" color="medium-emphasis" class="mb-3"
                >mdi-account-group-outline</v-icon
              >
              <p class="text-body-1 text-medium-emphasis mb-1">Team management coming soon</p>
              <p class="text-body-2 text-medium-emphasis">
                Invite teammates and assign roles to collaborate on your workspace.
              </p>
            </div>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>

    <!-- Danger Zone -->
    <v-row>
      <v-col>
        <v-card border="error" data-testid="danger-zone-card">
          <v-card-title class="pa-4 pb-0 text-error">Danger Zone</v-card-title>
          <v-card-subtitle class="px-4">Irreversible actions — proceed with care.</v-card-subtitle>
          <v-card-text>
            <div class="d-flex align-center justify-space-between flex-wrap gap-3">
              <div>
                <div class="text-body-1 font-weight-medium">Delete workspace</div>
                <div class="text-caption text-medium-emphasis">
                  Permanently delete this workspace and all its links, domains, and API keys. This
                  cannot be undone.
                </div>
              </div>
              <v-btn
                color="error"
                variant="outlined"
                data-testid="delete-workspace-btn"
                @click="showDeleteDialog = true"
              >
                Delete workspace
              </v-btn>
            </div>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>

    <DeleteWorkspaceDialog
      v-if="settings"
      v-model="showDeleteDialog"
      :workspace="settings"
      data-testid="delete-workspace-dialog"
    />
  </v-container>
</template>

<style scoped>
.color-swatch {
  display: inline-block;
  width: 16px;
  height: 16px;
  border-radius: 4px;
  border: 1px solid rgba(0, 0, 0, 0.12);
}
</style>
