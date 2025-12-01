import createClient, { type Middleware } from 'openapi-fetch'
import type { paths } from './generated/api'
import { useAuthStore } from '@/stores/auth'
import router from '@/router'

export const apiClient = createClient<paths>({
  baseUrl: import.meta.env.VITE_API_URL as string,
  credentials: 'include',
})

// Exported for testability
export const authMiddleware: Middleware = {
  async onRequest({ request }) {
    const authStore = useAuthStore()
    if (authStore.token) {
      request.headers.set('Authorization', `Bearer ${authStore.token}`)
    }
    return request
  },

  async onResponse({ response }) {
    if (response.status === 401) {
      const authStore = useAuthStore()
      authStore.logout()
      await router.push({ name: 'login' })
    }
    return response
  },
}

apiClient.use(authMiddleware)

export type { paths }
