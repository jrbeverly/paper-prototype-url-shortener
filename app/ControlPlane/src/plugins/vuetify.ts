import { createVuetify } from 'vuetify'
import { aliases, mdi } from 'vuetify/iconsets/mdi'
import '@mdi/font/css/materialdesignicons.css'
import 'vuetify/styles'

export default createVuetify({
  icons: {
    defaultSet: 'mdi',
    aliases,
    sets: { mdi },
  },
  theme: {
    defaultTheme: 'light',
    themes: {
      light: {
        dark: false,
        colors: {
          primary: '#2563EB',
          'primary-darken-1': '#1D4ED8',
          secondary: '#64748B',
          'secondary-darken-1': '#475569',
          accent: '#0EA5E9',
          error: '#EF4444',
          warning: '#F59E0B',
          info: '#3B82F6',
          success: '#10B981',
          background: '#F8FAFC',
          surface: '#FFFFFF',
        },
      },
      dark: {
        dark: true,
        colors: {
          primary: '#3B82F6',
          'primary-darken-1': '#2563EB',
          secondary: '#94A3B8',
          'secondary-darken-1': '#64748B',
          accent: '#38BDF8',
          error: '#F87171',
          warning: '#FBBF24',
          info: '#60A5FA',
          success: '#34D399',
          background: '#0F172A',
          surface: '#1E293B',
        },
      },
    },
  },
  defaults: {
    VBtn: { rounded: 'lg' },
    VCard: { rounded: 'lg' },
    VTextField: { variant: 'outlined', density: 'comfortable' },
    VSelect: { variant: 'outlined', density: 'comfortable' },
    VTextarea: { variant: 'outlined', density: 'comfortable' },
  },
  display: {
    mobileBreakpoint: 'sm',
    thresholds: {
      xs: 0,
      sm: 600,
      md: 960,
      lg: 1280,
      xl: 1920,
    },
  },
})
