import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useAuthStore } from '../auth'
import { useUIStore } from '../ui'
import { useWorkspaceStore } from '../workspace'

describe('useAuthStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
  })

  it('starts unauthenticated with no user or token', () => {
    const store = useAuthStore()
    expect(store.isAuthenticated).toBe(false)
    expect(store.user).toBeNull()
    expect(store.token).toBeNull()
    expect(store.refreshToken).toBeNull()
  })

  it('sets user and token on login', () => {
    const store = useAuthStore()
    store.login({ id: '1', email: 'test@example.com', name: 'Test User' }, 'tok_abc123')
    expect(store.isAuthenticated).toBe(true)
    expect(store.user).toEqual({ id: '1', email: 'test@example.com', name: 'Test User' })
    expect(store.token).toBe('tok_abc123')
  })

  it('sets refresh token when provided to login', () => {
    const store = useAuthStore()
    store.login({ id: '1', email: 'test@example.com', name: 'Test' }, 'tok_abc123', 'ref_xyz')
    expect(store.refreshToken).toBe('ref_xyz')
  })

  it('leaves refresh token unchanged when not provided to login', () => {
    const store = useAuthStore()
    store.login({ id: '1', email: 'test@example.com', name: 'Test' }, 'tok_abc123')
    expect(store.refreshToken).toBeNull()
  })

  it('persists token to localStorage on login', () => {
    const store = useAuthStore()
    store.login({ id: '1', email: 'test@example.com', name: 'Test' }, 'tok_abc123')
    expect(localStorage.getItem('auth_token')).toBe('tok_abc123')
  })

  it('persists refresh token to localStorage when provided', () => {
    const store = useAuthStore()
    store.login({ id: '1', email: 'test@example.com', name: 'Test' }, 'tok_abc123', 'ref_xyz')
    expect(localStorage.getItem('refresh_token')).toBe('ref_xyz')
  })

  it('restores token from localStorage on initialization', () => {
    localStorage.setItem('auth_token', 'tok_existing')
    const store = useAuthStore()
    expect(store.token).toBe('tok_existing')
    expect(store.isAuthenticated).toBe(true)
  })

  it('restores refresh token from localStorage on initialization', () => {
    localStorage.setItem('refresh_token', 'ref_existing')
    const store = useAuthStore()
    expect(store.refreshToken).toBe('ref_existing')
  })

  it('clears user, token, refresh token, and localStorage on logout', () => {
    const store = useAuthStore()
    store.login({ id: '1', email: 'test@example.com', name: 'Test' }, 'tok_abc123', 'ref_xyz')
    store.logout()
    expect(store.isAuthenticated).toBe(false)
    expect(store.user).toBeNull()
    expect(store.token).toBeNull()
    expect(store.refreshToken).toBeNull()
    expect(localStorage.getItem('auth_token')).toBeNull()
    expect(localStorage.getItem('refresh_token')).toBeNull()
  })

  it('resets UI and workspace stores on logout', () => {
    const authStore = useAuthStore()
    const uiStore = useUIStore()
    const workspaceStore = useWorkspaceStore()

    authStore.login({ id: '1', email: 'test@example.com', name: 'Test' }, 'tok_abc123')
    uiStore.toggleSidebar()
    workspaceStore.setCurrentWorkspace('ws_123')

    expect(uiStore.sidebarCollapsed).toBe(true)
    expect(workspaceStore.currentWorkspaceId).toBe('ws_123')

    authStore.logout()

    expect(uiStore.sidebarCollapsed).toBe(false)
    expect(workspaceStore.currentWorkspaceId).toBeNull()
  })

  describe('setTokens', () => {
    it('updates both tokens and persists to localStorage', () => {
      const store = useAuthStore()
      store.setTokens('new_access', 'new_refresh')
      expect(store.token).toBe('new_access')
      expect(store.refreshToken).toBe('new_refresh')
      expect(localStorage.getItem('auth_token')).toBe('new_access')
      expect(localStorage.getItem('refresh_token')).toBe('new_refresh')
    })
  })

  describe('setUser', () => {
    it('updates the user without affecting tokens', () => {
      const store = useAuthStore()
      store.login({ id: '1', email: 'old@example.com', name: 'Old' }, 'tok_abc')
      store.setUser({ id: '1', email: 'new@example.com', name: 'New' })
      expect(store.user).toEqual({ id: '1', email: 'new@example.com', name: 'New' })
      expect(store.token).toBe('tok_abc')
    })
  })
})
