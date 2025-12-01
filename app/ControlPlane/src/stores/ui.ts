import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { Notification } from '@/types/ui'

type Theme = 'light' | 'dark'

const SIDEBAR_KEY = 'ui_sidebar_collapsed'
const THEME_KEY = 'ui_theme'

export const useUIStore = defineStore('ui', () => {
  const sidebarCollapsed = ref(localStorage.getItem(SIDEBAR_KEY) === 'true')
  const theme = ref<Theme>((localStorage.getItem(THEME_KEY) as Theme) ?? 'light')
  const notifications = ref<Notification[]>([])

  function toggleSidebar() {
    sidebarCollapsed.value = !sidebarCollapsed.value
    localStorage.setItem(SIDEBAR_KEY, String(sidebarCollapsed.value))
  }

  function setTheme(newTheme: Theme) {
    theme.value = newTheme
    localStorage.setItem(THEME_KEY, newTheme)
  }

  function addNotification(notification: Omit<Notification, 'id'>) {
    notifications.value.push({ ...notification, id: crypto.randomUUID() })
  }

  function dismissNotification(id: string) {
    notifications.value = notifications.value.filter((n) => n.id !== id)
  }

  function reset() {
    sidebarCollapsed.value = false
    theme.value = 'light'
    notifications.value = []
    localStorage.removeItem(SIDEBAR_KEY)
    localStorage.removeItem(THEME_KEY)
  }

  return {
    sidebarCollapsed,
    theme,
    notifications,
    toggleSidebar,
    setTheme,
    addNotification,
    dismissNotification,
    reset,
  }
})
