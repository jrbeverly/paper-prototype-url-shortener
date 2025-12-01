<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import type { VForm } from 'vuetify/components'
import {
  useProfile,
  useUpdateProfile,
  useChangePassword,
  useNotificationPreferences,
  useUpdateNotificationPreferences,
  useSessions,
  useRevokeSession,
  useRevokeAllOtherSessions,
  useAppearance,
} from '@/composables/useSettings'
import DeleteAccountDialog from '@/components/settings/DeleteAccountDialog.vue'
import type { NotificationPreferences } from '@/types/settings'

// ── Profile ───────────────────────────────────────────────────────────────────
const profileFormRef = ref<VForm | null>(null)
const profileName = ref('')
const profileEmail = ref('')
const profileSuccess = ref(false)
const profileError = ref<string | null>(null)

const { data: profile, isLoading: profileLoading } = useProfile()
const updateProfile = useUpdateProfile()

const profileInitials = computed(() => {
  const name = profileName.value
  if (!name) return '?'
  return name
    .split(' ')
    .map((w) => w[0])
    .join('')
    .toUpperCase()
    .slice(0, 2)
})

watch(
  profile,
  (p) => {
    if (p) {
      profileName.value = p.name
      profileEmail.value = p.email
    }
  },
  { immediate: true }
)

const nameRules = [
  (v: string) => !!v || 'Name is required',
  (v: string) => v.trim().length >= 2 || 'Name must be at least 2 characters',
]

const profileEmailRules = [
  (v: string) => !!v || 'Email is required',
  (v: string) => /.+@.+\..+/.test(v) || 'Email must be valid',
]

async function submitProfile() {
  profileError.value = null
  profileSuccess.value = false
  const { valid } = await profileFormRef.value!.validate()
  if (!valid) return
  try {
    await updateProfile.mutateAsync({ name: profileName.value, email: profileEmail.value })
    profileSuccess.value = true
  } catch {
    profileError.value = 'Failed to update profile. Please try again.'
  }
}

// ── Password ──────────────────────────────────────────────────────────────────
const passwordFormRef = ref<VForm | null>(null)
const currentPassword = ref('')
const newPassword = ref('')
const confirmNewPassword = ref('')
const passwordSuccess = ref(false)
const passwordError = ref<string | null>(null)

const changePassword = useChangePassword()

const passwordStrength = computed(() => {
  const p = newPassword.value
  if (!p) return 0
  let score = 0
  if (p.length >= 8) score++
  if (/[A-Z]/.test(p)) score++
  if (/[0-9]/.test(p)) score++
  if (/[^A-Za-z0-9]/.test(p)) score++
  return Math.max(1, score)
})

const passwordStrengthLabel = computed(() => {
  const labels = ['', 'Weak', 'Fair', 'Good', 'Strong']
  return labels[passwordStrength.value] ?? ''
})

const passwordStrengthColor = computed(() => {
  const colors = ['', 'error', 'warning', 'info', 'success']
  return colors[passwordStrength.value] ?? ''
})

const currentPasswordRules = [(v: string) => !!v || 'Current password is required']

const newPasswordRules = [
  (v: string) => !!v || 'New password is required',
  (v: string) => v.length >= 8 || 'Password must be at least 8 characters',
]

const confirmNewPasswordRules = [
  (v: string) => !!v || 'Please confirm your new password',
  (v: string) => v === newPassword.value || 'Passwords do not match',
]

function mapPasswordError(e: unknown): string {
  if (e instanceof Error) {
    const msg = e.message.toLowerCase()
    if (msg.includes('incorrect') || msg.includes('invalid') || msg.includes('wrong'))
      return 'The current password you entered is incorrect.'
    if (msg.includes('network') || msg.includes('fetch'))
      return 'A network error occurred. Please check your connection and try again.'
  }
  return 'Failed to update password. Please try again.'
}

async function submitPassword() {
  passwordError.value = null
  passwordSuccess.value = false
  const { valid } = await passwordFormRef.value!.validate()
  if (!valid) return
  try {
    await changePassword.mutateAsync({
      currentPassword: currentPassword.value,
      newPassword: newPassword.value,
    })
    passwordSuccess.value = true
    currentPassword.value = ''
    newPassword.value = ''
    confirmNewPassword.value = ''
    passwordFormRef.value!.resetValidation()
  } catch (e) {
    passwordError.value = mapPasswordError(e)
  }
}

// ── Notifications ─────────────────────────────────────────────────────────────
const { data: notifPrefs, isLoading: notifLoading } = useNotificationPreferences()
const updateNotifPrefs = useUpdateNotificationPreferences()
const notifSuccess = ref(false)
const notifError = ref<string | null>(null)

const localNotifPrefs = ref<NotificationPreferences>({
  linkEvents: true,
  domainEvents: true,
  billingEvents: true,
  weeklyDigest: false,
})

watch(
  notifPrefs,
  (prefs) => {
    if (prefs) localNotifPrefs.value = { ...prefs }
  },
  { immediate: true }
)

async function submitNotifications() {
  notifError.value = null
  notifSuccess.value = false
  try {
    await updateNotifPrefs.mutateAsync({ ...localNotifPrefs.value })
    notifSuccess.value = true
  } catch {
    notifError.value = 'Failed to save notification preferences. Please try again.'
  }
}

// ── Appearance ────────────────────────────────────────────────────────────────
const { theme, toggleTheme } = useAppearance()

// ── Sessions ──────────────────────────────────────────────────────────────────
const { data: sessions, isLoading: sessionsLoading, isError: sessionsError } = useSessions()
const revokeSession = useRevokeSession()
const revokeAllSessions = useRevokeAllOtherSessions()

const otherSessions = computed(() => (sessions.value ?? []).filter((s) => !s.isCurrent))

function formatLastActive(iso: string): string {
  const diffMs = Date.now() - new Date(iso).getTime()
  const diffMins = Math.floor(diffMs / 60_000)
  if (diffMins < 1) return 'Just now'
  if (diffMins < 60) return `${diffMins} minute${diffMins === 1 ? '' : 's'} ago`
  const diffHours = Math.floor(diffMins / 60)
  if (diffHours < 24) return `${diffHours} hour${diffHours === 1 ? '' : 's'} ago`
  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  }).format(new Date(iso))
}

function sessionIcon(device: string): string {
  const d = device.toLowerCase()
  if (d.includes('iphone') || d.includes('android') || d.includes('mobile')) return 'mdi-cellphone'
  if (d.includes('ipad') || d.includes('tablet')) return 'mdi-tablet'
  return 'mdi-laptop'
}

// ── Account Deletion ──────────────────────────────────────────────────────────
const showDeleteDialog = ref(false)
</script>

<template>
  <v-container>
    <!-- Header -->
    <v-row>
      <v-col>
        <h1 class="text-h4 mb-2">Settings</h1>
        <p class="text-body-1 text-medium-emphasis">Manage your account settings.</p>
      </v-col>
    </v-row>

    <!-- Profile -->
    <v-row>
      <v-col>
        <v-card :loading="profileLoading" data-testid="profile-card">
          <v-card-title class="pa-4 pb-0">Profile</v-card-title>
          <v-card-subtitle class="px-4">Update your name and email address.</v-card-subtitle>
          <v-card-text>
            <v-alert
              v-if="profileError"
              type="error"
              density="compact"
              class="mb-4"
              data-testid="profile-error"
              >{{ profileError }}</v-alert
            >
            <v-alert
              v-if="profileSuccess"
              type="success"
              density="compact"
              class="mb-4"
              data-testid="profile-success"
              >Profile updated successfully.</v-alert
            >

            <div class="d-flex align-center mb-4">
              <v-avatar color="primary" size="64" data-testid="profile-avatar">
                <span class="text-h6">{{ profileInitials }}</span>
              </v-avatar>
            </div>

            <v-form ref="profileFormRef" @submit.prevent="submitProfile">
              <v-text-field
                v-model="profileName"
                label="Name"
                autocomplete="name"
                :rules="nameRules"
                class="mb-2"
                data-testid="profile-name-input"
              />
              <v-text-field
                v-model="profileEmail"
                label="Email"
                type="email"
                autocomplete="email"
                :rules="profileEmailRules"
                data-testid="profile-email-input"
              />
            </v-form>
          </v-card-text>
          <v-card-actions class="px-4 pb-4">
            <v-btn
              color="primary"
              variant="flat"
              :loading="updateProfile.isPending.value"
              data-testid="profile-save-btn"
              @click="submitProfile"
            >
              Save changes
            </v-btn>
          </v-card-actions>
        </v-card>
      </v-col>
    </v-row>

    <!-- Password -->
    <v-row>
      <v-col>
        <v-card data-testid="password-card">
          <v-card-title class="pa-4 pb-0">Change Password</v-card-title>
          <v-card-subtitle class="px-4"
            >Choose a strong password to keep your account secure.</v-card-subtitle
          >
          <v-card-text>
            <v-alert
              v-if="passwordError"
              type="error"
              density="compact"
              class="mb-4"
              data-testid="password-error"
              >{{ passwordError }}</v-alert
            >
            <v-alert
              v-if="passwordSuccess"
              type="success"
              density="compact"
              class="mb-4"
              data-testid="password-success"
              >Password updated successfully.</v-alert
            >

            <v-form ref="passwordFormRef" @submit.prevent="submitPassword">
              <v-text-field
                v-model="currentPassword"
                label="Current password"
                type="password"
                autocomplete="current-password"
                :rules="currentPasswordRules"
                class="mb-2"
                data-testid="current-password-input"
              />
              <v-text-field
                v-model="newPassword"
                label="New password"
                type="password"
                autocomplete="new-password"
                :rules="newPasswordRules"
                class="mb-1"
                data-testid="new-password-input"
              />
              <div v-if="newPassword" class="mb-3">
                <v-progress-linear
                  :model-value="passwordStrength * 25"
                  :color="passwordStrengthColor"
                  height="4"
                  rounded
                  data-testid="password-strength-bar"
                />
                <div
                  class="text-caption mt-1"
                  :class="`text-${passwordStrengthColor}`"
                  data-testid="password-strength-label"
                >
                  {{ passwordStrengthLabel }}
                </div>
              </div>
              <v-text-field
                v-model="confirmNewPassword"
                label="Confirm new password"
                type="password"
                autocomplete="new-password"
                :rules="confirmNewPasswordRules"
                data-testid="confirm-password-input"
              />
            </v-form>
          </v-card-text>
          <v-card-actions class="px-4 pb-4">
            <v-btn
              color="primary"
              variant="flat"
              :loading="changePassword.isPending.value"
              data-testid="password-save-btn"
              @click="submitPassword"
            >
              Update password
            </v-btn>
          </v-card-actions>
        </v-card>
      </v-col>
    </v-row>

    <!-- Notifications -->
    <v-row>
      <v-col>
        <v-card :loading="notifLoading" data-testid="notifications-card">
          <v-card-title class="pa-4 pb-0">Notifications</v-card-title>
          <v-card-subtitle class="px-4"
            >Choose which email notifications you receive.</v-card-subtitle
          >
          <v-card-text>
            <v-alert
              v-if="notifError"
              type="error"
              density="compact"
              class="mb-4"
              data-testid="notifications-error"
              >{{ notifError }}</v-alert
            >
            <v-alert
              v-if="notifSuccess"
              type="success"
              density="compact"
              class="mb-4"
              data-testid="notifications-success"
              >Notification preferences saved.</v-alert
            >

            <v-switch
              v-model="localNotifPrefs.linkEvents"
              label="Link events"
              hint="Get notified when links reach click milestones or expire"
              persistent-hint
              density="compact"
              class="mb-3"
              data-testid="notif-link-events"
            />
            <v-switch
              v-model="localNotifPrefs.domainEvents"
              label="Domain events"
              hint="Get notified about domain verification and SSL certificate updates"
              persistent-hint
              density="compact"
              class="mb-3"
              data-testid="notif-domain-events"
            />
            <v-switch
              v-model="localNotifPrefs.billingEvents"
              label="Billing events"
              hint="Get notified about invoices, payment failures, and plan changes"
              persistent-hint
              density="compact"
              class="mb-3"
              data-testid="notif-billing-events"
            />
            <v-switch
              v-model="localNotifPrefs.weeklyDigest"
              label="Weekly digest"
              hint="Receive a weekly summary of your link performance"
              persistent-hint
              density="compact"
              data-testid="notif-weekly-digest"
            />
          </v-card-text>
          <v-card-actions class="px-4 pb-4">
            <v-btn
              color="primary"
              variant="flat"
              :loading="updateNotifPrefs.isPending.value"
              data-testid="notifications-save-btn"
              @click="submitNotifications"
            >
              Save preferences
            </v-btn>
          </v-card-actions>
        </v-card>
      </v-col>
    </v-row>

    <!-- Appearance -->
    <v-row>
      <v-col>
        <v-card data-testid="appearance-card">
          <v-card-title class="pa-4 pb-0">Appearance</v-card-title>
          <v-card-subtitle class="px-4">Choose your preferred theme.</v-card-subtitle>
          <v-card-text>
            <div class="d-flex align-center">
              <div class="flex-grow-1">
                <div class="text-body-1">{{ theme === 'dark' ? 'Dark mode' : 'Light mode' }}</div>
                <div class="text-caption text-medium-emphasis">
                  {{ theme === 'dark' ? 'Using dark theme' : 'Using light theme' }}
                </div>
              </div>
              <v-btn
                :icon="theme === 'dark' ? 'mdi-weather-sunny' : 'mdi-weather-night'"
                variant="tonal"
                data-testid="theme-toggle"
                @click="toggleTheme"
              />
            </div>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>

    <!-- Sessions -->
    <v-row>
      <v-col>
        <v-card :loading="sessionsLoading" data-testid="sessions-card">
          <v-card-title class="pa-4 pb-0">Active Sessions</v-card-title>
          <v-card-subtitle class="px-4">Manage where you're signed in.</v-card-subtitle>
          <v-card-text>
            <v-alert
              v-if="sessionsError"
              type="error"
              density="compact"
              class="mb-4"
              data-testid="sessions-error"
            >
              Failed to load sessions. Please refresh the page.
            </v-alert>

            <template v-if="!sessionsLoading && sessions">
              <v-list lines="two">
                <v-list-item
                  v-for="session in sessions"
                  :key="session.id"
                  :prepend-icon="sessionIcon(session.device)"
                  :data-testid="`session-row-${session.id}`"
                >
                  <v-list-item-title>
                    {{ session.device }} — {{ session.browser }}
                    <v-chip
                      v-if="session.isCurrent"
                      size="x-small"
                      color="success"
                      label
                      class="ml-2"
                      data-testid="current-session-chip"
                    >
                      Current
                    </v-chip>
                  </v-list-item-title>
                  <v-list-item-subtitle>
                    {{ session.location }} · {{ formatLastActive(session.lastActive) }}
                  </v-list-item-subtitle>
                  <template #append>
                    <v-btn
                      v-if="!session.isCurrent"
                      variant="text"
                      color="error"
                      size="small"
                      :loading="revokeSession.isPending.value"
                      :data-testid="`revoke-session-btn-${session.id}`"
                      @click="revokeSession.mutate(session.id)"
                    >
                      Revoke
                    </v-btn>
                  </template>
                </v-list-item>
              </v-list>

              <div v-if="otherSessions.length > 0" class="mt-2">
                <v-btn
                  variant="outlined"
                  color="error"
                  size="small"
                  :loading="revokeAllSessions.isPending.value"
                  data-testid="revoke-all-sessions-btn"
                  @click="revokeAllSessions.mutate()"
                >
                  Revoke all other sessions
                </v-btn>
              </div>
            </template>
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
                <div class="text-body-1 font-weight-medium">Delete account</div>
                <div class="text-caption text-medium-emphasis">
                  Permanently delete your account and all associated data. This cannot be undone.
                </div>
              </div>
              <v-btn
                color="error"
                variant="outlined"
                data-testid="delete-account-btn"
                @click="showDeleteDialog = true"
              >
                Delete account
              </v-btn>
            </div>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>

    <DeleteAccountDialog v-model="showDeleteDialog" data-testid="delete-account-dialog" />
  </v-container>
</template>
