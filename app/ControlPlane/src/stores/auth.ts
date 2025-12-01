import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { User } from '@/types/auth'
import { useUIStore } from './ui'
import { useWorkspaceStore } from './workspace'

const TOKEN_KEY = 'auth_token'
const REFRESH_TOKEN_KEY = 'refresh_token'

export const useAuthStore = defineStore('auth', () => {
  const token = ref<string | null>(localStorage.getItem(TOKEN_KEY))
  const refreshToken = ref<string | null>(localStorage.getItem(REFRESH_TOKEN_KEY))
  const user = ref<User | null>(null)

  const isAuthenticated = computed(() => token.value !== null)

  function login(newUser: User, newToken: string, newRefreshToken?: string) {
    user.value = newUser
    token.value = newToken
    localStorage.setItem(TOKEN_KEY, newToken)
    if (newRefreshToken !== undefined) {
      refreshToken.value = newRefreshToken
      localStorage.setItem(REFRESH_TOKEN_KEY, newRefreshToken)
    }
  }

  function setTokens(newToken: string, newRefreshToken: string) {
    token.value = newToken
    refreshToken.value = newRefreshToken
    localStorage.setItem(TOKEN_KEY, newToken)
    localStorage.setItem(REFRESH_TOKEN_KEY, newRefreshToken)
  }

  function setUser(newUser: User) {
    user.value = newUser
  }

  function logout() {
    user.value = null
    token.value = null
    refreshToken.value = null
    localStorage.removeItem(TOKEN_KEY)
    localStorage.removeItem(REFRESH_TOKEN_KEY)

    useUIStore().reset()
    useWorkspaceStore().reset()
  }

  return { token, refreshToken, user, isAuthenticated, login, setTokens, setUser, logout }
})
