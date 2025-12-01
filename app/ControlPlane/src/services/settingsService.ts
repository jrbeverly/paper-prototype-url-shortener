// TODO: Replace mock implementations with apiClient calls once settings API endpoints are defined.

import { getCurrentUser } from '@/services/authService'
import { useAuthStore } from '@/stores/auth'
import type {
  ActiveSession,
  ChangePasswordParams,
  NotificationPreferences,
  UpdateProfileParams,
} from '@/types/settings'
import type { User } from '@/types/auth'

const notifPrefs: NotificationPreferences = {
  linkEvents: true,
  domainEvents: true,
  billingEvents: true,
  weeklyDigest: false,
}

const sessions: ActiveSession[] = [
  {
    id: 'session-001',
    device: 'MacBook Pro',
    browser: 'Chrome 125',
    location: 'San Francisco, US',
    ipAddress: '12.34.56.78',
    lastActive: new Date().toISOString(),
    isCurrent: true,
  },
  {
    id: 'session-002',
    device: 'iPhone 15',
    browser: 'Safari 17',
    location: 'New York, US',
    ipAddress: '98.76.54.32',
    lastActive: new Date(Date.now() - 3 * 60 * 60 * 1000).toISOString(),
    isCurrent: false,
  },
]

export const settingsService = {
  getProfile(): Promise<User> {
    // TODO: apiClient.GET('/api/v1/auth/me')
    return getCurrentUser()
  },

  updateProfile(params: UpdateProfileParams): Promise<User> {
    const authStore = useAuthStore()
    const updated: User = { ...authStore.user!, ...params }
    authStore.setUser(updated)
    // TODO: apiClient.PATCH('/api/v1/auth/me', { body: params })
    return Promise.resolve(updated)
  },

  changePassword(params: ChangePasswordParams): Promise<void> {
    void params // TODO: apiClient.POST('/api/v1/auth/change-password', { body: params })
    return Promise.resolve()
  },

  getNotificationPreferences(): Promise<NotificationPreferences> {
    // TODO: apiClient.GET('/api/v1/settings/notifications')
    return Promise.resolve({ ...notifPrefs })
  },

  updateNotificationPreferences(prefs: NotificationPreferences): Promise<NotificationPreferences> {
    // TODO: apiClient.PUT('/api/v1/settings/notifications', { body: prefs })
    Object.assign(notifPrefs, prefs)
    return Promise.resolve({ ...notifPrefs })
  },

  getSessions(): Promise<ActiveSession[]> {
    // TODO: apiClient.GET('/api/v1/auth/sessions')
    return Promise.resolve(sessions.map((s) => ({ ...s })))
  },

  revokeSession(sessionId: string): Promise<void> {
    // TODO: apiClient.DELETE(`/api/v1/auth/sessions/${sessionId}`)
    const idx = sessions.findIndex((s) => s.id === sessionId)
    if (idx !== -1) sessions.splice(idx, 1)
    return Promise.resolve()
  },

  revokeAllOtherSessions(): Promise<void> {
    // TODO: apiClient.POST('/api/v1/auth/sessions/revoke-others')
    const current = sessions.find((s) => s.isCurrent)
    sessions.length = 0
    if (current) sessions.push(current)
    return Promise.resolve()
  },

  deleteAccount(): Promise<void> {
    // TODO: apiClient.DELETE('/api/v1/account')
    return Promise.resolve()
  },
}
