import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

declare module 'vue-router' {
  interface RouteMeta {
    requiresAuth?: boolean
    title?: string
    breadcrumbs?: Array<{ text: string; to?: string }>
  }
}

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      redirect: '/dashboard',
    },

    // Public routes — authenticated users are redirected away
    {
      path: '/login',
      name: 'login',
      component: () => import('@/views/LoginView.vue'),
      meta: { requiresAuth: false, title: 'Sign in' },
    },
    {
      path: '/register',
      name: 'register',
      component: () => import('@/views/RegisterView.vue'),
      meta: { requiresAuth: false, title: 'Create account' },
    },

    // Protected routes
    {
      path: '/dashboard',
      name: 'dashboard',
      component: () => import('@/views/DashboardView.vue'),
      meta: {
        requiresAuth: true,
        title: 'Dashboard',
        breadcrumbs: [{ text: 'Dashboard' }],
      },
    },
    {
      path: '/domains',
      name: 'domains',
      component: () => import('@/views/DomainsView.vue'),
      meta: {
        requiresAuth: true,
        title: 'Domains',
        breadcrumbs: [{ text: 'Dashboard', to: '/dashboard' }, { text: 'Domains' }],
      },
    },
    {
      path: '/domains/:id',
      name: 'domain-detail',
      component: () => import('@/views/DomainDetailView.vue'),
      meta: {
        requiresAuth: true,
        title: 'Domain',
        breadcrumbs: [
          { text: 'Dashboard', to: '/dashboard' },
          { text: 'Domains', to: '/domains' },
          { text: 'Domain' },
        ],
      },
    },
    {
      path: '/links',
      name: 'links',
      component: () => import('@/views/LinksView.vue'),
      meta: {
        requiresAuth: true,
        title: 'Links',
        breadcrumbs: [{ text: 'Dashboard', to: '/dashboard' }, { text: 'Links' }],
      },
    },
    // /links/create must precede /links/:id so the literal segment matches first
    {
      path: '/links/create',
      name: 'link-create',
      component: () => import('@/views/LinkCreateView.vue'),
      meta: {
        requiresAuth: true,
        title: 'Create link',
        breadcrumbs: [
          { text: 'Dashboard', to: '/dashboard' },
          { text: 'Links', to: '/links' },
          { text: 'Create' },
        ],
      },
    },
    {
      path: '/links/:id',
      name: 'link-detail',
      component: () => import('@/views/LinkDetailView.vue'),
      meta: {
        requiresAuth: true,
        title: 'Link',
        breadcrumbs: [
          { text: 'Dashboard', to: '/dashboard' },
          { text: 'Links', to: '/links' },
          { text: 'Link' },
        ],
      },
    },
    {
      path: '/settings',
      name: 'settings',
      component: () => import('@/views/SettingsView.vue'),
      meta: {
        requiresAuth: true,
        title: 'Settings',
        breadcrumbs: [{ text: 'Dashboard', to: '/dashboard' }, { text: 'Settings' }],
      },
    },
    {
      path: '/billing',
      name: 'billing',
      component: () => import('@/views/BillingView.vue'),
      meta: {
        requiresAuth: true,
        title: 'Billing',
        breadcrumbs: [{ text: 'Dashboard', to: '/dashboard' }, { text: 'Billing' }],
      },
    },
    {
      path: '/analytics',
      name: 'analytics',
      component: () => import('@/views/AnalyticsView.vue'),
      meta: {
        requiresAuth: true,
        title: 'Analytics',
        breadcrumbs: [{ text: 'Dashboard', to: '/dashboard' }, { text: 'Analytics' }],
      },
    },
    {
      path: '/api-keys',
      name: 'api-keys',
      component: () => import('@/views/ApiKeysView.vue'),
      meta: {
        requiresAuth: true,
        title: 'API Keys',
        breadcrumbs: [{ text: 'Dashboard', to: '/dashboard' }, { text: 'API Keys' }],
      },
    },
    {
      path: '/workspace',
      name: 'workspace',
      component: () => import('@/views/WorkspacesView.vue'),
      meta: {
        requiresAuth: true,
        title: 'Workspace',
        breadcrumbs: [{ text: 'Dashboard', to: '/dashboard' }, { text: 'Workspace' }],
      },
    },

    // Generic error page — navigate to with { name: 'error', query: { code: '500' } }
    {
      path: '/error',
      name: 'error',
      component: () => import('@/views/ErrorView.vue'),
      meta: { title: 'Error' },
      props: (route) => ({
        code: Number(route.query.code) || 500,
        message: typeof route.query.message === 'string' ? route.query.message : undefined,
      }),
    },

    // 404 catch-all
    {
      path: '/:pathMatch(.*)*',
      name: 'not-found',
      component: () => import('@/views/NotFoundView.vue'),
      meta: { title: 'Page not found' },
    },
  ],
})

router.beforeEach((to) => {
  const authStore = useAuthStore()

  if (to.meta.requiresAuth && !authStore.isAuthenticated) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }

  if (to.meta.requiresAuth === false && authStore.isAuthenticated) {
    return { name: 'dashboard' }
  }
})

router.afterEach((to) => {
  document.title = to.meta.title ? `${to.meta.title} — Short.io` : 'Short.io'
})

export default router
