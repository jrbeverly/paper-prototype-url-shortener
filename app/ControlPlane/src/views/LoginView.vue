<template>
  <v-container class="fill-height" fluid>
    <v-row align="center" justify="center">
      <v-col cols="12" sm="8" md="4">
        <v-card>
          <v-card-title class="text-h5 pa-6">Sign in</v-card-title>
          <v-card-text>
            <v-alert
              v-if="error"
              type="error"
              class="mb-4"
              density="compact"
              data-testid="login-error"
              >{{ error }}</v-alert
            >
            <v-form ref="formRef" @submit.prevent="submit">
              <v-text-field
                v-model="email"
                label="Email"
                type="email"
                autocomplete="email"
                :rules="emailRules"
                data-testid="email-input"
                class="mb-2"
              />
              <v-text-field
                v-model="password"
                label="Password"
                type="password"
                autocomplete="current-password"
                :rules="passwordRules"
                data-testid="password-input"
                class="mb-2"
              />
              <div class="d-flex justify-space-between align-center mb-4">
                <v-checkbox
                  v-model="rememberMe"
                  label="Remember me"
                  density="compact"
                  hide-details
                  data-testid="remember-me-checkbox"
                />
                <a
                  href="#"
                  class="text-caption text-decoration-none"
                  data-testid="forgot-password-link"
                  @click.prevent="onForgotPassword"
                  >Forgot password?</a
                >
              </div>
              <v-btn
                type="submit"
                color="primary"
                block
                :loading="loading"
                data-testid="login-submit-btn"
                >Sign in</v-btn
              >
            </v-form>
          </v-card-text>
          <v-card-text class="text-center pt-0">
            Don't have an account?
            <router-link :to="{ name: 'register' }" data-testid="register-link"
              >Create account</router-link
            >
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>
  </v-container>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, type RouteLocationRaw } from 'vue-router'
import { login } from '@/services/authService'
import type { VForm } from 'vuetify/components'

const route = useRoute()

const formRef = ref<VForm | null>(null)
const email = ref('')
const password = ref('')
const rememberMe = ref(false)
const loading = ref(false)
const error = ref<string | null>(null)

const emailRules = [
  (v: string) => !!v || 'Email is required',
  (v: string) => /.+@.+\..+/.test(v) || 'Email must be valid',
]

const passwordRules = [(v: string) => !!v || 'Password is required']

function mapError(e: unknown): string {
  if (e instanceof Error) {
    const msg = e.message.toLowerCase()
    if (msg.includes('invalid credentials') || msg.includes('invalid email or password'))
      return 'The email address or password you entered is incorrect.'
    if (msg.includes('suspended') || msg.includes('disabled'))
      return 'Your account has been suspended.'
    if (msg.includes('network') || msg.includes('fetch') || msg.includes('failed to fetch'))
      return 'A network error occurred. Please check your connection and try again.'
  }
  return 'Login failed. Please try again.'
}

function resolveRedirect(): RouteLocationRaw {
  const q = route.query.redirect
  if (typeof q === 'string' && q.startsWith('/')) return q
  return { name: 'dashboard' }
}

async function submit() {
  error.value = null
  const { valid } = await formRef.value!.validate()
  if (!valid) return

  loading.value = true
  try {
    await login(email.value, password.value, resolveRedirect())
  } catch (e) {
    error.value = mapError(e)
  } finally {
    loading.value = false
  }
}

function onForgotPassword() {
  // Stubbed — will be implemented in a future iteration
}
</script>
