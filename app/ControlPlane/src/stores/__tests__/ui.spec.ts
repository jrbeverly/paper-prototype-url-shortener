import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useUIStore } from '../ui'

describe('useUIStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
  })

  it('starts with default state', () => {
    const store = useUIStore()
    expect(store.sidebarCollapsed).toBe(false)
    expect(store.theme).toBe('light')
    expect(store.notifications).toHaveLength(0)
  })

  it('toggles sidebar collapsed state', () => {
    const store = useUIStore()
    store.toggleSidebar()
    expect(store.sidebarCollapsed).toBe(true)
    store.toggleSidebar()
    expect(store.sidebarCollapsed).toBe(false)
  })

  it('persists sidebar state to localStorage', () => {
    const store = useUIStore()
    store.toggleSidebar()
    expect(localStorage.getItem('ui_sidebar_collapsed')).toBe('true')
  })

  it('restores sidebar state from localStorage on initialization', () => {
    localStorage.setItem('ui_sidebar_collapsed', 'true')
    const store = useUIStore()
    expect(store.sidebarCollapsed).toBe(true)
  })

  it('sets theme and persists to localStorage', () => {
    const store = useUIStore()
    store.setTheme('dark')
    expect(store.theme).toBe('dark')
    expect(localStorage.getItem('ui_theme')).toBe('dark')
  })

  it('restores theme from localStorage on initialization', () => {
    localStorage.setItem('ui_theme', 'dark')
    const store = useUIStore()
    expect(store.theme).toBe('dark')
  })

  it('adds a notification with a generated id', () => {
    const store = useUIStore()
    store.addNotification({ message: 'Saved', type: 'success' })
    expect(store.notifications).toHaveLength(1)
    expect(store.notifications[0].message).toBe('Saved')
    expect(store.notifications[0].type).toBe('success')
    expect(store.notifications[0].id).toBeTruthy()
  })

  it('dismisses a notification by id', () => {
    const store = useUIStore()
    store.addNotification({ message: 'Hello', type: 'info' })
    const id = store.notifications[0].id
    store.dismissNotification(id)
    expect(store.notifications).toHaveLength(0)
  })

  it('only dismisses the targeted notification', () => {
    const store = useUIStore()
    store.addNotification({ message: 'First', type: 'info' })
    store.addNotification({ message: 'Second', type: 'warning' })
    const firstId = store.notifications[0].id
    store.dismissNotification(firstId)
    expect(store.notifications).toHaveLength(1)
    expect(store.notifications[0].message).toBe('Second')
  })

  it('resets all state and clears localStorage', () => {
    const store = useUIStore()
    store.toggleSidebar()
    store.setTheme('dark')
    store.addNotification({ message: 'Hello', type: 'info' })
    store.reset()
    expect(store.sidebarCollapsed).toBe(false)
    expect(store.theme).toBe('light')
    expect(store.notifications).toHaveLength(0)
    expect(localStorage.getItem('ui_sidebar_collapsed')).toBeNull()
    expect(localStorage.getItem('ui_theme')).toBeNull()
  })
})
