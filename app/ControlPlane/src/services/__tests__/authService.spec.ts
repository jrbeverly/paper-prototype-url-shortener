import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useAuthStore } from '@/stores/auth'
import router from '@/router'
import * as authService from '../authService'

vi.mock('@/router', () => ({
  default: { push: vi.fn() },
}))

const mockUser = { id: 'u1', email: 'test@example.com', name: 'Test User' }

const mockAuthResponse = {
  accessToken: 'access_abc',
  refreshToken: 'refresh_xyz',
  expiresIn: 3600,
  user: mockUser,
}

const mockRefreshResponse = {
  accessToken: 'access_new',
  refreshToken: 'refresh_new',
  expiresIn: 3600,
}

function stubFetchOk(body: unknown) {
  vi.stubGlobal(
    'fetch',
    vi.fn().mockResolvedValue(new Response(JSON.stringify(body), { status: 200 }))
  )
}

function stubFetchError(status: number, body: unknown = {}) {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(body), { status })))
}

describe('authService', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    vi.mocked(router.push).mockReset()
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  // ── login ────────────────────────────────────────────────────────────────

  describe('login', () => {
    it('stores access token, refresh token, and user on success', async () => {
      stubFetchOk(mockAuthResponse)
      const authStore = useAuthStore()

      await authService.login('test@example.com', 'password')

      expect(authStore.token).toBe('access_abc')
      expect(authStore.refreshToken).toBe('refresh_xyz')
      expect(authStore.user).toEqual(mockUser)
      expect(authStore.isAuthenticated).toBe(true)
    })

    it('persists both tokens to localStorage', async () => {
      stubFetchOk(mockAuthResponse)

      await authService.login('test@example.com', 'password')

      expect(localStorage.getItem('auth_token')).toBe('access_abc')
      expect(localStorage.getItem('refresh_token')).toBe('refresh_xyz')
    })

    it('redirects to dashboard on success', async () => {
      stubFetchOk(mockAuthResponse)

      await authService.login('test@example.com', 'password')

      expect(router.push).toHaveBeenCalledWith({ name: 'dashboard' })
    })

    it('posts credentials to the login endpoint', async () => {
      const fetchMock = vi
        .fn()
        .mockResolvedValue(new Response(JSON.stringify(mockAuthResponse), { status: 200 }))
      vi.stubGlobal('fetch', fetchMock)

      await authService.login('test@example.com', 'secret')

      const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
      expect(url).toContain('/auth/login')
      expect(JSON.parse(init.body as string)).toEqual({
        email: 'test@example.com',
        password: 'secret',
      })
    })

    it('throws with server detail message on API failure', async () => {
      stubFetchError(401, { detail: 'Invalid credentials' })

      await expect(authService.login('bad@example.com', 'wrong')).rejects.toThrow(
        'Invalid credentials'
      )
    })

    it('throws generic message when server returns no detail', async () => {
      stubFetchError(500)

      await expect(authService.login('test@example.com', 'password')).rejects.toThrow(
        'Login failed'
      )
    })

    it('schedules a token refresh before expiry', async () => {
      stubFetchOk(mockAuthResponse)
      await authService.login('test@example.com', 'password')

      // Prepare the refresh response before the timer fires
      stubFetchOk(mockRefreshResponse)

      // Advance past the scheduled refresh point (expiresIn - 5min = 3300s)
      await vi.runAllTimersAsync()

      const fetchMock = vi.mocked(global.fetch)
      const callUrls = fetchMock.mock.calls.map(([url]) => url as string)
      expect(callUrls.some((u) => u.includes('/auth/refresh'))).toBe(true)
    })
  })

  // ── register ─────────────────────────────────────────────────────────────

  describe('register', () => {
    it('stores tokens and user on success', async () => {
      stubFetchOk(mockAuthResponse)
      const authStore = useAuthStore()

      await authService.register('Test User', 'test@example.com', 'password')

      expect(authStore.token).toBe('access_abc')
      expect(authStore.refreshToken).toBe('refresh_xyz')
      expect(authStore.user).toEqual(mockUser)
    })

    it('redirects to dashboard on success', async () => {
      stubFetchOk(mockAuthResponse)

      await authService.register('Test User', 'test@example.com', 'password')

      expect(router.push).toHaveBeenCalledWith({ name: 'dashboard' })
    })

    it('posts name, email, and password to the register endpoint', async () => {
      const fetchMock = vi
        .fn()
        .mockResolvedValue(new Response(JSON.stringify(mockAuthResponse), { status: 200 }))
      vi.stubGlobal('fetch', fetchMock)

      await authService.register('Alice', 'alice@example.com', 'pass123')

      const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
      expect(url).toContain('/auth/register')
      expect(JSON.parse(init.body as string)).toEqual({
        name: 'Alice',
        email: 'alice@example.com',
        password: 'pass123',
      })
    })

    it('throws with server detail message on API failure', async () => {
      stubFetchError(422, { detail: 'Email already taken' })

      await expect(authService.register('Test', 'existing@example.com', 'pass')).rejects.toThrow(
        'Email already taken'
      )
    })
  })

  // ── logout ───────────────────────────────────────────────────────────────

  describe('logout', () => {
    it('clears auth store and navigates to login', async () => {
      const authStore = useAuthStore()
      authStore.login(mockUser, 'tok', 'refresh')
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 200 })))

      await authService.logout()

      expect(authStore.isAuthenticated).toBe(false)
      expect(authStore.token).toBeNull()
      expect(authStore.refreshToken).toBeNull()
      expect(router.push).toHaveBeenCalledWith({ name: 'login' })
    })

    it('clears localStorage on logout', async () => {
      const authStore = useAuthStore()
      authStore.login(mockUser, 'tok', 'refresh')
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 200 })))

      await authService.logout()

      expect(localStorage.getItem('auth_token')).toBeNull()
      expect(localStorage.getItem('refresh_token')).toBeNull()
    })

    it('still navigates to login when server logout call fails', async () => {
      const authStore = useAuthStore()
      authStore.login(mockUser, 'tok', 'refresh')
      vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('network error')))

      await authService.logout()

      expect(authStore.isAuthenticated).toBe(false)
      expect(router.push).toHaveBeenCalledWith({ name: 'login' })
    })
  })

  // ── refreshAccessToken ───────────────────────────────────────────────────

  describe('refreshAccessToken', () => {
    it('updates both tokens on success', async () => {
      const authStore = useAuthStore()
      authStore.login(mockUser, 'old_access', 'old_refresh')
      stubFetchOk(mockRefreshResponse)

      await authService.refreshAccessToken()

      expect(authStore.token).toBe('access_new')
      expect(authStore.refreshToken).toBe('refresh_new')
    })

    it('sends stored refresh token to the refresh endpoint', async () => {
      const authStore = useAuthStore()
      authStore.login(mockUser, 'old_access', 'stored_refresh')
      const fetchMock = vi
        .fn()
        .mockResolvedValue(new Response(JSON.stringify(mockRefreshResponse), { status: 200 }))
      vi.stubGlobal('fetch', fetchMock)

      await authService.refreshAccessToken()

      const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
      expect(url).toContain('/auth/refresh')
      expect(JSON.parse(init.body as string)).toEqual({ refreshToken: 'stored_refresh' })
    })

    it('logs out and redirects to login when API returns non-OK', async () => {
      const authStore = useAuthStore()
      authStore.login(mockUser, 'old_access', 'old_refresh')
      stubFetchError(401)

      await authService.refreshAccessToken()

      expect(authStore.isAuthenticated).toBe(false)
      expect(router.push).toHaveBeenCalledWith({ name: 'login' })
    })

    it('logs out and redirects to login when no refresh token is stored', async () => {
      // No login call — refreshToken is null
      await authService.refreshAccessToken()

      const authStore = useAuthStore()
      expect(authStore.isAuthenticated).toBe(false)
      expect(router.push).toHaveBeenCalledWith({ name: 'login' })
    })
  })

  // ── getCurrentUser ───────────────────────────────────────────────────────

  describe('getCurrentUser', () => {
    it('fetches user profile and updates the store', async () => {
      const authStore = useAuthStore()
      authStore.login(mockUser, 'tok_abc')
      stubFetchOk(mockUser)

      const user = await authService.getCurrentUser()

      expect(user).toEqual(mockUser)
      expect(authStore.user).toEqual(mockUser)
    })

    it('includes the access token in the Authorization header', async () => {
      const authStore = useAuthStore()
      authStore.login(mockUser, 'tok_abc')
      const fetchMock = vi
        .fn()
        .mockResolvedValue(new Response(JSON.stringify(mockUser), { status: 200 }))
      vi.stubGlobal('fetch', fetchMock)

      await authService.getCurrentUser()

      const [, init] = fetchMock.mock.calls[0] as [string, RequestInit]
      expect((init.headers as Record<string, string>)['Authorization']).toBe('Bearer tok_abc')
    })

    it('throws when not authenticated', async () => {
      await expect(authService.getCurrentUser()).rejects.toThrow('Not authenticated')
    })

    it('throws on API failure', async () => {
      const authStore = useAuthStore()
      authStore.login(mockUser, 'tok_abc')
      stubFetchError(403)

      await expect(authService.getCurrentUser()).rejects.toThrow('Failed to fetch user profile')
    })
  })
})
