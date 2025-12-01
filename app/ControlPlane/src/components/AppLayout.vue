<script setup lang="ts">
import { ref, computed } from 'vue'
import { useDisplay } from 'vuetify'
import { useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useUIStore } from '@/stores/ui'
import { useWorkspaceStore } from '@/stores/workspace'
import { logout } from '@/services/authService'

const authStore = useAuthStore()
const uiStore = useUIStore()
const workspaceStore = useWorkspaceStore()
const route = useRoute()
const { lgAndUp } = useDisplay()

// Controls the navigation drawer's open state. On mobile/tablet the drawer is
// temporary and this toggles it. On desktop it is permanent (:permanent="lgAndUp"),
// but Vuetify only auto-opens a permanent drawer when its v-model is null — an
// explicit `false` leaves the drawer closed and `inert`, so its nav links can't
// be reached. Initialize from the current breakpoint so the desktop sidebar
// renders open on first load.
const mobileDrawerOpen = ref(lgAndUp.value)

// Rail (icon-only) mode is only meaningful on desktop.
const isRail = computed(() => lgAndUp.value && uiStore.sidebarCollapsed)

const breadcrumbs = computed(() =>
  (route.meta.breadcrumbs ?? []).map((crumb) => ({
    title: crumb.text,
    disabled: !crumb.to,
    to: crumb.to,
  }))
)

const notificationCount = computed(() => uiStore.notifications.length)

function notificationIcon(type: string): string {
  const icons: Record<string, string> = {
    error: 'mdi-alert-circle-outline',
    success: 'mdi-check-circle-outline',
    warning: 'mdi-alert-outline',
    info: 'mdi-information-outline',
  }
  return icons[type] ?? 'mdi-information-outline'
}

function toggleDrawer() {
  if (lgAndUp.value) {
    uiStore.toggleSidebar()
  } else {
    mobileDrawerOpen.value = !mobileDrawerOpen.value
  }
}

const navItems = [
  { title: 'Dashboard', icon: 'mdi-view-dashboard-outline', to: { name: 'dashboard' } },
  { title: 'Links', icon: 'mdi-link-variant', to: { name: 'links' } },
  { title: 'Domains', icon: 'mdi-web', to: { name: 'domains' } },
  { title: 'Analytics', icon: 'mdi-chart-line', to: { name: 'analytics' } },
  { title: 'API Keys', icon: 'mdi-key-outline', to: { name: 'api-keys' } },
  { title: 'Workspace', icon: 'mdi-office-building-outline', to: { name: 'workspace' } },
  { title: 'Settings', icon: 'mdi-cog-outline', to: { name: 'settings' } },
  { title: 'Billing', icon: 'mdi-credit-card-outline', to: { name: 'billing' } },
]

const userInitials = computed(() => {
  const name = authStore.user?.name
  if (!name) return '?'
  return name
    .split(' ')
    .map((word) => word[0])
    .join('')
    .toUpperCase()
    .slice(0, 2)
})

function toggleTheme() {
  uiStore.setTheme(uiStore.theme === 'light' ? 'dark' : 'light')
}

async function handleLogout() {
  await logout()
}
</script>

<template>
  <v-navigation-drawer v-model="mobileDrawerOpen" :rail="isRail" :permanent="lgAndUp">
    <v-list-item prepend-icon="mdi-link-variant" title="Short.io" nav />

    <v-divider />

    <v-list density="compact" nav>
      <v-list-item
        v-for="item in navItems"
        :key="item.title"
        :prepend-icon="item.icon"
        :title="item.title"
        :to="item.to"
        rounded="lg"
        color="primary"
      />
    </v-list>

    <template #append>
      <v-divider />
      <v-list density="compact" nav>
        <v-list-item
          prepend-icon="mdi-logout"
          title="Sign out"
          rounded="lg"
          @click="handleLogout"
        />
      </v-list>
    </template>
  </v-navigation-drawer>

  <v-app-bar elevation="0" border="b">
    <v-app-bar-nav-icon @click="toggleDrawer" />
    <v-app-bar-title v-if="!lgAndUp">Short.io</v-app-bar-title>

    <!-- Workspace selector -->
    <v-menu location="bottom start">
      <template #activator="{ props: menuProps }">
        <v-btn
          v-bind="menuProps"
          variant="tonal"
          append-icon="mdi-chevron-down"
          class="ml-2 text-body-2 text-truncate"
          max-width="200"
          data-testid="workspace-selector"
        >
          {{ workspaceStore.currentWorkspace?.name ?? 'Personal workspace' }}
        </v-btn>
      </template>
      <v-list min-width="220">
        <v-list-subheader>Workspaces</v-list-subheader>
        <template v-if="workspaceStore.workspaces.length === 0">
          <v-list-item title="Personal workspace" :active="true" color="primary" />
        </template>
        <v-list-item
          v-for="ws in workspaceStore.workspaces"
          :key="ws.id"
          :title="ws.name"
          :active="ws.id === workspaceStore.currentWorkspaceId"
          color="primary"
          @click="workspaceStore.setCurrentWorkspace(ws.id)"
        />
        <v-divider />
        <v-list-item
          prepend-icon="mdi-cog-outline"
          title="Workspace settings"
          :to="{ name: 'workspace' }"
          data-testid="workspace-settings-link"
        />
      </v-list>
    </v-menu>

    <v-spacer />

    <!-- Notification bell -->
    <v-menu location="bottom end" :close-on-content-click="false">
      <template #activator="{ props: menuProps }">
        <v-btn v-bind="menuProps" icon variant="text" data-testid="notification-bell">
          <v-badge v-if="notificationCount > 0" :content="notificationCount" color="error">
            <v-icon>mdi-bell-outline</v-icon>
          </v-badge>
          <v-icon v-else>mdi-bell-outline</v-icon>
        </v-btn>
      </template>
      <v-card min-width="300" max-width="380">
        <v-card-title class="text-body-1 font-weight-medium pa-4 pb-2">Notifications</v-card-title>
        <v-divider />
        <v-list
          v-if="uiStore.notifications.length > 0"
          lines="one"
          max-height="360"
          class="overflow-y-auto"
        >
          <v-list-item
            v-for="n in uiStore.notifications"
            :key="n.id"
            :title="n.message"
            :prepend-icon="notificationIcon(n.type)"
          >
            <template #append>
              <v-btn icon size="x-small" variant="text" @click="uiStore.dismissNotification(n.id)">
                <v-icon>mdi-close</v-icon>
              </v-btn>
            </template>
          </v-list-item>
        </v-list>
        <v-card-text v-else class="text-center text-medium-emphasis py-8">
          <v-icon size="40" class="mb-2">mdi-bell-off-outline</v-icon>
          <div>No notifications</div>
        </v-card-text>
      </v-card>
    </v-menu>

    <!-- Theme toggle -->
    <v-btn
      :icon="uiStore.theme === 'dark' ? 'mdi-weather-sunny' : 'mdi-weather-night'"
      variant="text"
      data-testid="theme-toggle"
      @click="toggleTheme"
    />

    <!-- User avatar menu -->
    <v-menu location="bottom end">
      <template #activator="{ props: menuProps }">
        <v-avatar
          v-bind="menuProps"
          color="primary"
          size="36"
          class="mr-2"
          style="cursor: pointer"
          data-testid="user-menu-trigger"
        >
          <span class="text-body-2 font-weight-medium">{{ userInitials }}</span>
        </v-avatar>
      </template>
      <v-list min-width="200">
        <template v-if="authStore.user">
          <v-list-item :title="authStore.user.name" :subtitle="authStore.user.email" lines="two" />
          <v-divider />
        </template>
        <v-list-item
          prepend-icon="mdi-account-outline"
          title="Profile"
          :to="{ name: 'settings' }"
        />
        <v-list-item prepend-icon="mdi-cog-outline" title="Settings" :to="{ name: 'settings' }" />
        <v-divider />
        <v-list-item prepend-icon="mdi-logout" title="Sign out" @click="handleLogout" />
      </v-list>
    </v-menu>
  </v-app-bar>

  <v-main>
    <v-breadcrumbs
      v-if="breadcrumbs.length > 0"
      :items="breadcrumbs"
      class="px-4 py-2"
      data-testid="breadcrumbs"
    >
      <template #divider>
        <v-icon size="small">mdi-chevron-right</v-icon>
      </template>
    </v-breadcrumbs>

    <RouterView v-slot="{ Component }">
      <Transition name="fade" mode="out-in">
        <component :is="Component" />
      </Transition>
    </RouterView>
  </v-main>
</template>

<style scoped>
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.15s ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
</style>
