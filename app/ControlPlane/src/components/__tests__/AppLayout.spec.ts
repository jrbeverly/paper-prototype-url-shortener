import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { createRouter, createWebHistory } from 'vue-router'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import AppLayout from '../AppLayout.vue'
import { useAuthStore } from '@/stores/auth'
import { useUIStore } from '@/stores/ui'

// VNavigationDrawer, VAppBar, and VMain require the layout context provided
// by <v-app>. Tests mount AppLayout inside a <v-app> wrapper to satisfy this.

vi.mock('@/services/authService', () => ({
  logout: vi.fn().mockResolvedValue(undefined),
}))

// VApp uses ResizeObserver for layout sizing; JSDOM does not implement it.
vi.stubGlobal(
  'ResizeObserver',
  class {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
)

const vuetify = createVuetify({ components, directives })

function createTestRouter() {
  return createRouter({
    history: createWebHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      {
        path: '/dashboard',
        name: 'dashboard',
        component: { template: '<div />' },
        meta: { breadcrumbs: [{ text: 'Dashboard' }] },
      },
      {
        path: '/links',
        name: 'links',
        component: { template: '<div />' },
        meta: { breadcrumbs: [{ text: 'Dashboard', to: '/dashboard' }, { text: 'Links' }] },
      },
      {
        path: '/domains',
        name: 'domains',
        component: { template: '<div />' },
        meta: { breadcrumbs: [{ text: 'Dashboard', to: '/dashboard' }, { text: 'Domains' }] },
      },
      { path: '/settings', name: 'settings', component: { template: '<div />' } },
      { path: '/billing', name: 'billing', component: { template: '<div />' } },
      { path: '/api-keys', name: 'api-keys', component: { template: '<div />' } },
      { path: '/workspace', name: 'workspace', component: { template: '<div />' } },
      {
        path: '/analytics',
        name: 'analytics',
        component: { template: '<div />' },
        meta: { breadcrumbs: [{ text: 'Dashboard', to: '/dashboard' }, { text: 'Analytics' }] },
      },
    ],
  })
}

function mountComponent() {
  return {
    wrapper: mount(
      { components: { AppLayout }, template: '<v-app><AppLayout /></v-app>' },
      { global: { plugins: [vuetify, createTestRouter()] } }
    ),
  }
}

async function mountAtRoute(path: string) {
  const router = createTestRouter()
  await router.push(path)
  await router.isReady()
  return {
    wrapper: mount(
      { components: { AppLayout }, template: '<v-app><AppLayout /></v-app>' },
      { global: { plugins: [vuetify, router] } }
    ),
  }
}

describe('AppLayout', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  describe('navigation drawer', () => {
    it('renders all navigation links', () => {
      const { wrapper } = mountComponent()
      expect(wrapper.text()).toContain('Dashboard')
      expect(wrapper.text()).toContain('Links')
      expect(wrapper.text()).toContain('Domains')
      expect(wrapper.text()).toContain('Analytics')
      expect(wrapper.text()).toContain('API Keys')
      expect(wrapper.text()).toContain('Workspace')
      expect(wrapper.text()).toContain('Billing')
      expect(wrapper.text()).toContain('Settings')
    })

    it('renders the brand name', () => {
      const { wrapper } = mountComponent()
      expect(wrapper.text()).toContain('Short.io')
    })

    it('renders a sign out option', () => {
      const { wrapper } = mountComponent()
      expect(wrapper.text()).toContain('Sign out')
    })
  })

  describe('workspace selector', () => {
    it('renders the workspace selector button', () => {
      const { wrapper } = mountComponent()
      expect(wrapper.find('[data-testid="workspace-selector"]').exists()).toBe(true)
    })

    it('shows "Personal workspace" when no workspaces are loaded', () => {
      const { wrapper } = mountComponent()
      expect(wrapper.find('[data-testid="workspace-selector"]').text()).toContain(
        'Personal workspace'
      )
    })
  })

  describe('notification bell', () => {
    it('renders the notification bell button', () => {
      const { wrapper } = mountComponent()
      expect(wrapper.find('[data-testid="notification-bell"]').exists()).toBe(true)
    })

    it('shows no badge when there are no notifications', () => {
      const { wrapper } = mountComponent()
      expect(wrapper.find('[data-testid="notification-bell"] .v-badge').exists()).toBe(false)
    })

    it('shows a badge when there are notifications', () => {
      const uiStore = useUIStore()
      uiStore.addNotification({ message: 'Test notification', type: 'info' })
      const { wrapper } = mountComponent()
      expect(wrapper.find('[data-testid="notification-bell"] .v-badge').exists()).toBe(true)
    })
  })

  describe('user avatar initials', () => {
    it('shows a placeholder when no user is authenticated', () => {
      const { wrapper } = mountComponent()
      expect(wrapper.text()).toContain('?')
    })

    it('shows two-letter initials for a two-word name', () => {
      const authStore = useAuthStore()
      authStore.login({ id: '1', name: 'John Doe', email: 'john@example.com' }, 'tok')
      const { wrapper } = mountComponent()
      expect(wrapper.text()).toContain('JD')
    })

    it('shows one-letter initial for a single-word name', () => {
      const authStore = useAuthStore()
      authStore.login({ id: '2', name: 'Alice', email: 'alice@example.com' }, 'tok')
      const { wrapper } = mountComponent()
      expect(wrapper.text()).toContain('A')
    })

    it('shows at most two initials for names with more than two words', () => {
      const authStore = useAuthStore()
      authStore.login({ id: '3', name: 'Mary Jane Watson', email: 'mj@example.com' }, 'tok')
      const { wrapper } = mountComponent()
      expect(wrapper.text()).toContain('MJ')
      expect(wrapper.text()).not.toContain('MJW')
    })
  })

  describe('user menu', () => {
    it('renders the user avatar trigger', () => {
      const { wrapper } = mountComponent()
      // Vuetify renders v-menu overlay content into a teleport portal outside the
      // wrapper DOM — opening it in JSDOM requires stubbing visualViewport and other
      // browser layout APIs. Testing the activator presence is sufficient here;
      // profile/settings items are verified by e2e/visual tests.
      expect(wrapper.find('[data-testid="user-menu-trigger"]').exists()).toBe(true)
    })
  })

  describe('theme toggle', () => {
    it('renders a theme toggle button', () => {
      const { wrapper } = mountComponent()
      expect(wrapper.find('[data-testid="theme-toggle"]').exists()).toBe(true)
    })

    it('switches from light to dark on click', async () => {
      const uiStore = useUIStore()
      expect(uiStore.theme).toBe('light')
      const { wrapper } = mountComponent()
      await wrapper.find('[data-testid="theme-toggle"]').trigger('click')
      expect(uiStore.theme).toBe('dark')
    })

    it('switches from dark to light on click', async () => {
      const uiStore = useUIStore()
      uiStore.setTheme('dark')
      const { wrapper } = mountComponent()
      await wrapper.find('[data-testid="theme-toggle"]').trigger('click')
      expect(uiStore.theme).toBe('light')
    })

    it('shows the moon icon in light mode', () => {
      const { wrapper } = mountComponent()
      expect(wrapper.find('.mdi-weather-night').exists()).toBe(true)
    })

    it('shows the sun icon in dark mode', () => {
      const uiStore = useUIStore()
      uiStore.setTheme('dark')
      const { wrapper } = mountComponent()
      expect(wrapper.find('.mdi-weather-sunny').exists()).toBe(true)
    })
  })

  describe('breadcrumbs', () => {
    it('does not render breadcrumbs on a route with no breadcrumb meta', () => {
      const { wrapper } = mountComponent()
      // Default route '/' has no breadcrumbs meta
      expect(wrapper.find('[data-testid="breadcrumbs"]').exists()).toBe(false)
    })

    it('renders breadcrumbs when the route provides them', async () => {
      const { wrapper } = await mountAtRoute('/links')
      await wrapper.vm.$nextTick()
      const bc = wrapper.find('[data-testid="breadcrumbs"]')
      expect(bc.exists()).toBe(true)
      expect(bc.text()).toContain('Dashboard')
      expect(bc.text()).toContain('Links')
    })

    it('renders a single breadcrumb for a top-level route', async () => {
      const { wrapper } = await mountAtRoute('/dashboard')
      await wrapper.vm.$nextTick()
      const bc = wrapper.find('[data-testid="breadcrumbs"]')
      expect(bc.exists()).toBe(true)
      expect(bc.text()).toContain('Dashboard')
    })
  })

  describe('logout', () => {
    it('calls logout when the sign out list item is clicked', async () => {
      const { logout } = await import('@/services/authService')
      const { wrapper } = mountComponent()

      const signOutItems = wrapper.findAll('.v-list-item')
      const signOutItem = signOutItems.find((item) => item.text().includes('Sign out'))
      expect(signOutItem).toBeDefined()
      await signOutItem!.trigger('click')

      expect(logout).toHaveBeenCalled()
    })
  })
})
