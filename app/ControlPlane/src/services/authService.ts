import router from '@/router'
import { useAuthStore } from '@/stores/auth'
import type { User } from '@/types/auth'
import type { RouteLocationRaw } from 'vue-router'

interface AuthResponse {
  accessToken: string
  refreshToken: string
  expiresIn: number
  user: User
}

interface RefreshResponse {
  accessToken: string
  refreshToken: string
  expiresIn: number
}

let refreshTimer: ReturnType<typeof setTimeout> | null = null

// Exported for testing
export function scheduleRefresh(expiresIn: number): void {
  if (refreshTimer !== null) {
    clearTimeout(refreshTimer)
  }
  // Fire 5 minutes before expiry so the token is replaced before it becomes invalid
  const delayMs = Math.max(0, expiresIn - 5 * 60) * 1000
  refreshTimer = setTimeout(() => {
    refreshAccessToken().catch(() => {
      // refreshAccessToken handles failure internally (logout + redirect)
    })
  }, delayMs)
}

function clearRefreshTimer(): void {
  if (refreshTimer !== null) {
    clearTimeout(refreshTimer)
    refreshTimer = null
  }
}

function authApiUrl(path: string): string {
  const base = (import.meta.env.VITE_API_URL as string | undefined) ?? ''
  return `${base}/api/v1/auth/${path}`
}

async function postJson(path: string, body: Record<string, unknown>): Promise<Response> {
  return fetch(authApiUrl(path), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify(body),
  })
}

export async function login(
  email: string,
  password: string,
  redirectTo?: RouteLocationRaw
): Promise<void> {
  const res = await postJson('login', { email, password })
  if (!res.ok) {
    const err = (await res.json().catch(() => ({}))) as { detail?: string }
    throw new Error(err.detail ?? 'Login failed')
  }
  const data = (await res.json()) as AuthResponse
  useAuthStore().login(data.user, data.accessToken, data.refreshToken)
  scheduleRefresh(data.expiresIn)
  await router.push(redirectTo ?? { name: 'dashboard' })
}

export async function register(name: string, email: string, password: string): Promise<void> {
  const res = await postJson('register', { name, email, password })
  if (!res.ok) {
    const err = (await res.json().catch(() => ({}))) as { detail?: string }
    throw new Error(err.detail ?? 'Registration failed')
  }
  const data = (await res.json()) as AuthResponse
  useAuthStore().login(data.user, data.accessToken, data.refreshToken)
  scheduleRefresh(data.expiresIn)
  await router.push({ name: 'dashboard' })
}

export async function logout(): Promise<void> {
  const authStore = useAuthStore()
  const hadToken = Boolean(authStore.token)
  clearRefreshTimer()
  authStore.logout()
  if (hadToken) {
    // Best-effort: notify server. Failure does not block client-side logout.
    await postJson('logout', {}).catch(() => {})
  }
  await router.push({ name: 'login' })
}

export async function refreshAccessToken(): Promise<void> {
  const authStore = useAuthStore()
  const storedRefreshToken = authStore.refreshToken
  if (!storedRefreshToken) {
    await handleRefreshFailure()
    return
  }
  const res = await postJson('refresh', { refreshToken: storedRefreshToken })
  if (!res.ok) {
    await handleRefreshFailure()
    return
  }
  const data = (await res.json()) as RefreshResponse
  authStore.setTokens(data.accessToken, data.refreshToken)
  scheduleRefresh(data.expiresIn)
}

async function handleRefreshFailure(): Promise<void> {
  clearRefreshTimer()
  useAuthStore().logout()
  await router.push({ name: 'login' })
}

export async function getCurrentUser(): Promise<User> {
  const authStore = useAuthStore()
  if (!authStore.token) {
    throw new Error('Not authenticated')
  }
  const base = (import.meta.env.VITE_API_URL as string | undefined) ?? ''
  const res = await fetch(`${base}/api/v1/auth/me`, {
    headers: { Authorization: `Bearer ${authStore.token}` },
    credentials: 'include',
  })
  if (!res.ok) {
    throw new Error('Failed to fetch user profile')
  }
  const user = (await res.json()) as User
  authStore.setUser(user)
  return user
}
