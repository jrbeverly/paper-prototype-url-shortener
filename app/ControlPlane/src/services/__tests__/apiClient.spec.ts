import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useAuthStore } from '@/stores/auth'
import { authMiddleware } from '../apiClient'
import router from '@/router'

vi.mock('@/router', () => ({
  default: { push: vi.fn() },
}))

describe('authMiddleware', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    vi.mocked(router.push).mockReset()
  })

  describe('onRequest', () => {
    it('attaches Authorization header when a token is present', async () => {
      const authStore = useAuthStore()
      authStore.login({ id: '1', email: 'test@example.com', name: 'Test' }, 'tok_abc123')

      const request = new Request('http://localhost/api/test')
      await authMiddleware.onRequest?.({ request } as Parameters<
        NonNullable<typeof authMiddleware.onRequest>
      >[0])

      expect(request.headers.get('Authorization')).toBe('Bearer tok_abc123')
    })

    it('does not attach Authorization header when there is no token', async () => {
      const request = new Request('http://localhost/api/test')
      await authMiddleware.onRequest?.({ request } as Parameters<
        NonNullable<typeof authMiddleware.onRequest>
      >[0])

      expect(request.headers.get('Authorization')).toBeNull()
    })
  })

  describe('onResponse', () => {
    it('calls logout and redirects to login on 401', async () => {
      const authStore = useAuthStore()
      authStore.login({ id: '1', email: 'test@example.com', name: 'Test' }, 'tok_abc123')

      const response = new Response(null, { status: 401 })
      await authMiddleware.onResponse?.({ response } as Parameters<
        NonNullable<typeof authMiddleware.onResponse>
      >[0])

      expect(authStore.isAuthenticated).toBe(false)
      expect(router.push).toHaveBeenCalledWith({ name: 'login' })
    })

    it('does not log out on non-401 responses', async () => {
      const authStore = useAuthStore()
      authStore.login({ id: '1', email: 'test@example.com', name: 'Test' }, 'tok_abc123')

      const response = new Response(null, { status: 200 })
      await authMiddleware.onResponse?.({ response } as Parameters<
        NonNullable<typeof authMiddleware.onResponse>
      >[0])

      expect(authStore.isAuthenticated).toBe(true)
      expect(router.push).not.toHaveBeenCalled()
    })
  })
})
