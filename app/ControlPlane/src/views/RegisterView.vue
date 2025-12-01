<template>
  <v-container class="fill-height" fluid>
    <v-row align="center" justify="center">
      <v-col cols="12" sm="8" md="4">
        <v-card>
          <v-card-title class="text-h5 pa-6">Create account</v-card-title>
          <v-card-text>
            <v-alert
              v-if="error"
              type="error"
              class="mb-4"
              density="compact"
              data-testid="register-error"
              >{{ error }}</v-alert
            >
            <v-form ref="formRef" @submit.prevent="submit">
              <v-text-field
                v-model="name"
                label="Name"
                type="text"
                autocomplete="name"
                :rules="nameRules"
                data-testid="register-name-input"
                class="mb-2"
              />
              <v-text-field
                v-model="email"
                label="Email"
                type="email"
                autocomplete="email"
                :rules="emailRules"
                data-testid="register-email-input"
                class="mb-2"
              />
              <v-text-field
                v-model="password"
                label="Password"
                type="password"
                autocomplete="new-password"
                :rules="passwordRules"
                data-testid="register-password-input"
                class="mb-1"
              />
              <div v-if="password" class="mb-3">
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
                v-model="confirmPassword"
                label="Confirm password"
                type="password"
                autocomplete="new-password"
                :rules="confirmPasswordRules"
                data-testid="register-confirm-password-input"
                class="mb-2"
              />
              <v-checkbox
                v-model="acceptedTerms"
                :rules="termsRules"
                density="compact"
                data-testid="register-terms-checkbox"
                class="mb-4"
              >
                <template #label>
                  <span class="text-body-2">
                    I agree to the
                    <a href="#" class="text-decoration-none" @click.prevent>Terms of Service</a>
                    and
                    <a href="#" class="text-decoration-none" @click.prevent>Privacy Policy</a>
                  </span>
                </template>
              </v-checkbox>
              <v-btn
                type="submit"
                color="primary"
                block
                :loading="loading"
                data-testid="register-submit-btn"
                >Create account</v-btn
              >
            </v-form>
          </v-card-text>
          <v-card-text class="text-center pt-0">
            Already have an account?
            <router-link :to="{ name: 'login' }" data-testid="login-link">Sign in</router-link>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>
  </v-container>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { register } from '@/services/authService'
import type { VForm } from 'vuetify/components'

const formRef = ref<VForm | null>(null)
const name = ref('')
const email = ref('')
const password = ref('')
const confirmPassword = ref('')
const acceptedTerms = ref(false)
const loading = ref(false)
const error = ref<string | null>(null)

const nameRules = [
  (v: string) => !!v || 'Name is required',
  (v: string) => v.trim().length >= 2 || 'Name must be at least 2 characters',
]

const emailRules = [
  (v: string) => !!v || 'Email is required',
  (v: string) => /.+@.+\..+/.test(v) || 'Email must be valid',
]

const passwordRules = [
  (v: string) => !!v || 'Password is required',
  (v: string) => v.length >= 8 || 'Password must be at least 8 characters',
]

const confirmPasswordRules = [
  (v: string) => !!v || 'Please confirm your password',
  (v: string) => v === password.value || 'Passwords do not match',
]

const termsRules = [
  (v: boolean | null) => !!v || 'You must accept the terms of service to continue',
]

const passwordStrength = computed(() => {
  const p = password.value
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

watch(password, () => {
  if (confirmPassword.value) {
    formRef.value?.validate()
  }
})

function mapError(e: unknown): string {
  if (e instanceof Error) {
    const msg = e.message.toLowerCase()
    if (
      msg.includes('already exists') ||
      msg.includes('already registered') ||
      msg.includes('duplicate')
    )
      return 'An account with this email address already exists.'
    if (msg.includes('weak') || msg.includes('password strength') || msg.includes('too simple'))
      return 'Your password is too weak. Please choose a stronger password.'
    if (msg.includes('network') || msg.includes('fetch'))
      return 'A network error occurred. Please check your connection and try again.'
  }
  return 'Registration failed. Please try again.'
}

async function submit() {
  error.value = null
  const { valid } = await formRef.value!.validate()
  if (!valid) return

  loading.value = true
  try {
    await register(name.value, email.value, password.value)
  } catch (e) {
    error.value = mapError(e)
  } finally {
    loading.value = false
  }
}
</script>
