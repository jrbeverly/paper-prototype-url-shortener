import { computed } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { settingsService } from '@/services/settingsService'
import { useUIStore } from '@/stores/ui'
import type {
  ChangePasswordParams,
  NotificationPreferences,
  UpdateProfileParams,
} from '@/types/settings'

export function useProfile() {
  return useQuery({
    queryKey: ['settings', 'profile'],
    queryFn: settingsService.getProfile,
    staleTime: 5 * 60 * 1000,
    retry: false,
  })
}

export function useUpdateProfile() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (params: UpdateProfileParams) => settingsService.updateProfile(params),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['settings', 'profile'] })
    },
  })
}

export function useChangePassword() {
  return useMutation({
    mutationFn: (params: ChangePasswordParams) => settingsService.changePassword(params),
  })
}

export function useNotificationPreferences() {
  return useQuery({
    queryKey: ['settings', 'notifications'],
    queryFn: settingsService.getNotificationPreferences,
    staleTime: 5 * 60 * 1000,
    retry: false,
  })
}

export function useUpdateNotificationPreferences() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (prefs: NotificationPreferences) =>
      settingsService.updateNotificationPreferences(prefs),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['settings', 'notifications'] })
    },
  })
}

export function useSessions() {
  return useQuery({
    queryKey: ['settings', 'sessions'],
    queryFn: settingsService.getSessions,
    staleTime: 60 * 1000,
    retry: false,
  })
}

export function useRevokeSession() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (sessionId: string) => settingsService.revokeSession(sessionId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['settings', 'sessions'] })
    },
  })
}

export function useRevokeAllOtherSessions() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: settingsService.revokeAllOtherSessions,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['settings', 'sessions'] })
    },
  })
}

export function useDeleteAccount() {
  return useMutation({
    mutationFn: settingsService.deleteAccount,
  })
}

export function useAppearance() {
  const uiStore = useUIStore()
  const theme = computed(() => uiStore.theme)
  function toggleTheme() {
    uiStore.setTheme(uiStore.theme === 'light' ? 'dark' : 'light')
  }
  return { theme, toggleTheme }
}
