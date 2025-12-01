import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import { defineComponent, ref } from 'vue'
import SettingsView from '../SettingsView.vue'

vi.mock('@/composables/useSettings', () => ({
  useProfile: vi.fn(),
  useUpdateProfile: vi.fn(),
  useChangePassword: vi.fn(),
  useNotificationPreferences: vi.fn(),
  useUpdateNotificationPreferences: vi.fn(),
  useSessions: vi.fn(),
  useRevokeSession: vi.fn(),
  useRevokeAllOtherSessions: vi.fn(),
  useAppearance: vi.fn(),
}))

const vuetify = createVuetify({ components, directives })

vi.stubGlobal(
  'ResizeObserver',
  class {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
)

const mockProfile = { id: 'user-001', name: 'Jane Doe', email: 'jane@example.com' }

const mockNotifPrefs = {
  linkEvents: true,
  domainEvents: false,
  billingEvents: true,
  weeklyDigest: false,
}

const mockSessions = [
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

function makeQueryResult<T>(data: T) {
  return {
    data: ref(data),
    isLoading: ref(false),
    isError: ref(false),
    isFetching: ref(false),
    error: ref(null),
  }
}

function makeMutation() {
  return {
    mutate: vi.fn(),
    mutateAsync: vi.fn().mockResolvedValue(undefined),
    isPending: ref(false),
    isError: ref(false),
    error: ref(null),
    reset: vi.fn(),
  }
}

const stubDialog = (name: string) =>
  defineComponent({
    name,
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: `<div :data-testid="'${name.toLowerCase()}-stub'" :data-visible="String(modelValue ?? false)"></div>`,
  })

function mountComponent() {
  return mount(SettingsView, {
    global: {
      plugins: [vuetify],
      stubs: { DeleteAccountDialog: stubDialog('DeleteAccountDialog') },
    },
  })
}

describe('SettingsView', () => {
  beforeEach(async () => {
    vi.clearAllMocks()

    const {
      useProfile,
      useUpdateProfile,
      useChangePassword,
      useNotificationPreferences,
      useUpdateNotificationPreferences,
      useSessions,
      useRevokeSession,
      useRevokeAllOtherSessions,
      useAppearance,
    } = await import('@/composables/useSettings')

    vi.mocked(useProfile).mockReturnValue(
      makeQueryResult(mockProfile) as ReturnType<typeof useProfile>
    )
    vi.mocked(useUpdateProfile).mockReturnValue(
      makeMutation() as unknown as ReturnType<typeof useUpdateProfile>
    )
    vi.mocked(useChangePassword).mockReturnValue(
      makeMutation() as unknown as ReturnType<typeof useChangePassword>
    )
    vi.mocked(useNotificationPreferences).mockReturnValue(
      makeQueryResult(mockNotifPrefs) as ReturnType<typeof useNotificationPreferences>
    )
    vi.mocked(useUpdateNotificationPreferences).mockReturnValue(
      makeMutation() as unknown as ReturnType<typeof useUpdateNotificationPreferences>
    )
    vi.mocked(useSessions).mockReturnValue(
      makeQueryResult(mockSessions) as ReturnType<typeof useSessions>
    )
    vi.mocked(useRevokeSession).mockReturnValue(
      makeMutation() as unknown as ReturnType<typeof useRevokeSession>
    )
    vi.mocked(useRevokeAllOtherSessions).mockReturnValue(
      makeMutation() as unknown as ReturnType<typeof useRevokeAllOtherSessions>
    )
    vi.mocked(useAppearance).mockReturnValue({
      theme: ref<'light' | 'dark'>('light'),
      toggleTheme: vi.fn(),
    })
  })

  it('renders the page title', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('Settings')
  })

  it('renders the subtitle', () => {
    const wrapper = mountComponent()
    expect(wrapper.text()).toContain('Manage your account settings')
  })

  // ── Profile ─────────────────────────────────────────────────────────────────
  describe('profile section', () => {
    it('renders the profile card', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="profile-card"]').exists()).toBe(true)
    })

    it('renders the name input', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="profile-name-input"]').exists()).toBe(true)
    })

    it('renders the email input', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="profile-email-input"]').exists()).toBe(true)
    })

    it('populates the name field from profile data', () => {
      const wrapper = mountComponent()
      const input = wrapper.find('[data-testid="profile-name-input"] input')
      expect((input.element as HTMLInputElement).value).toBe('Jane Doe')
    })

    it('populates the email field from profile data', () => {
      const wrapper = mountComponent()
      const input = wrapper.find('[data-testid="profile-email-input"] input')
      expect((input.element as HTMLInputElement).value).toBe('jane@example.com')
    })

    it('renders the avatar with initials derived from the name', () => {
      const wrapper = mountComponent()
      const avatar = wrapper.find('[data-testid="profile-avatar"]')
      expect(avatar.exists()).toBe(true)
      expect(avatar.text()).toContain('JD')
    })

    it('renders the save changes button', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="profile-save-btn"]').exists()).toBe(true)
    })
  })

  // ── Password ─────────────────────────────────────────────────────────────────
  describe('password section', () => {
    it('renders the password card', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="password-card"]').exists()).toBe(true)
    })

    it('renders the current password input', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="current-password-input"]').exists()).toBe(true)
    })

    it('renders the new password input', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="new-password-input"]').exists()).toBe(true)
    })

    it('renders the confirm password input', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="confirm-password-input"]').exists()).toBe(true)
    })

    it('renders the update password button', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="password-save-btn"]').exists()).toBe(true)
    })

    it('does not show the strength bar when new password is empty', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="password-strength-bar"]').exists()).toBe(false)
    })
  })

  // ── Notifications ─────────────────────────────────────────────────────────────
  describe('notifications section', () => {
    it('renders the notifications card', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="notifications-card"]').exists()).toBe(true)
    })

    it('renders the link events switch', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="notif-link-events"]').exists()).toBe(true)
    })

    it('renders the domain events switch', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="notif-domain-events"]').exists()).toBe(true)
    })

    it('renders the billing events switch', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="notif-billing-events"]').exists()).toBe(true)
    })

    it('renders the weekly digest switch', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="notif-weekly-digest"]').exists()).toBe(true)
    })

    it('renders the save preferences button', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="notifications-save-btn"]').exists()).toBe(true)
    })

    it('calls updateNotifPrefs.mutateAsync when save is clicked', async () => {
      const { useUpdateNotificationPreferences } = await import('@/composables/useSettings')
      const mutateAsync = vi.fn().mockResolvedValue(undefined)
      vi.mocked(useUpdateNotificationPreferences).mockReturnValue({
        ...makeMutation(),
        mutateAsync,
      } as unknown as ReturnType<typeof useUpdateNotificationPreferences>)

      const wrapper = mountComponent()
      await wrapper.find('[data-testid="notifications-save-btn"]').trigger('click')
      await wrapper.vm.$nextTick()

      expect(mutateAsync).toHaveBeenCalledWith(expect.objectContaining({ linkEvents: true }))
    })
  })

  // ── Appearance ────────────────────────────────────────────────────────────────
  describe('appearance section', () => {
    it('renders the appearance card', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="appearance-card"]').exists()).toBe(true)
    })

    it('renders the theme toggle button', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="theme-toggle"]').exists()).toBe(true)
    })

    it('shows "Light mode" label when theme is light', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="appearance-card"]').text()).toContain('Light mode')
    })

    it('shows "Dark mode" label when theme is dark', async () => {
      const { useAppearance } = await import('@/composables/useSettings')
      vi.mocked(useAppearance).mockReturnValue({
        theme: ref<'light' | 'dark'>('dark'),
        toggleTheme: vi.fn(),
      })

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="appearance-card"]').text()).toContain('Dark mode')
    })

    it('calls toggleTheme when the toggle button is clicked', async () => {
      const { useAppearance } = await import('@/composables/useSettings')
      const toggleTheme = vi.fn()
      vi.mocked(useAppearance).mockReturnValue({
        theme: ref<'light' | 'dark'>('light'),
        toggleTheme,
      })

      const wrapper = mountComponent()
      await wrapper.find('[data-testid="theme-toggle"]').trigger('click')
      expect(toggleTheme).toHaveBeenCalled()
    })
  })

  // ── Sessions ──────────────────────────────────────────────────────────────────
  describe('sessions section', () => {
    it('renders the sessions card', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="sessions-card"]').exists()).toBe(true)
    })

    it('renders a row for each session', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="session-row-session-001"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="session-row-session-002"]').exists()).toBe(true)
    })

    it('shows a "Current" chip on the active session', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="current-session-chip"]').exists()).toBe(true)
    })

    it('shows a revoke button for non-current sessions', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="revoke-session-btn-session-002"]').exists()).toBe(true)
    })

    it('does not show a revoke button for the current session', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="revoke-session-btn-session-001"]').exists()).toBe(false)
    })

    it('shows "Revoke all other sessions" when other sessions exist', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="revoke-all-sessions-btn"]').exists()).toBe(true)
    })

    it('hides "Revoke all other sessions" when only the current session exists', async () => {
      const { useSessions } = await import('@/composables/useSettings')
      vi.mocked(useSessions).mockReturnValue(
        makeQueryResult([mockSessions[0]]) as ReturnType<typeof useSessions>
      )

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="revoke-all-sessions-btn"]').exists()).toBe(false)
    })

    it('shows an error alert when sessions fail to load', async () => {
      const { useSessions } = await import('@/composables/useSettings')
      vi.mocked(useSessions).mockReturnValue({
        data: ref(undefined),
        isLoading: ref(false),
        isError: ref(true),
        isFetching: ref(false),
        error: ref(new Error('Failed')),
      } as ReturnType<typeof useSessions>)

      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="sessions-error"]').exists()).toBe(true)
    })
  })

  // ── Danger Zone ───────────────────────────────────────────────────────────────
  describe('danger zone', () => {
    it('renders the danger zone card', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="danger-zone-card"]').exists()).toBe(true)
    })

    it('renders the delete account button', () => {
      const wrapper = mountComponent()
      expect(wrapper.find('[data-testid="delete-account-btn"]').exists()).toBe(true)
    })

    it('the delete dialog is initially closed', () => {
      const wrapper = mountComponent()
      const dialog = wrapper.findComponent({ name: 'DeleteAccountDialog' })
      expect(dialog.exists()).toBe(true)
      expect(dialog.props('modelValue')).toBe(false)
    })

    it('opens the delete dialog when the delete button is clicked', async () => {
      const wrapper = mountComponent()
      await wrapper.find('[data-testid="delete-account-btn"]').trigger('click')
      await wrapper.vm.$nextTick()

      const dialog = wrapper.findComponent({ name: 'DeleteAccountDialog' })
      expect(dialog.props('modelValue')).toBe(true)
    })
  })
})
