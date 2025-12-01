import { type Page } from '@playwright/test'

interface FakeUser {
  id: string
  email: string
  name: string
}

const DEFAULT_USER: FakeUser = {
  id: '00000000-0000-0000-0000-000000000001',
  email: 'e2e-test@short.io',
  name: 'E2E Test User',
}

function fakeToken(): string {
  return `eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.${btoa(
    JSON.stringify({ sub: DEFAULT_USER.id, email: DEFAULT_USER.email })
  )}.fake-signature`
}

function fakeRefreshToken(): string {
  return `refresh_${crypto.randomUUID()}`
}

/**
 * Set up an authenticated session by writing tokens to localStorage and
 * priming the Pinia auth store. The Pinia store reads `auth_token` and
 * `refresh_token` from localStorage on startup, so this tricks the app
 * into thinking the user is already logged in.
 */
export async function setupAuth(
  page: Page,
  user: Partial<FakeUser> = {}
): Promise<FakeUser> {
  const merged = { ...DEFAULT_USER, ...user }

  await page.goto('/')
  await page.evaluate(
    ([token, refreshToken, userData]) => {
      localStorage.setItem('auth_token', token)
      localStorage.setItem('refresh_token', refreshToken)
      // The Pinia auth store initializes token from localStorage.
      // We also stash the user object so the auth store can hydrate it.
      localStorage.setItem('auth_user', JSON.stringify(userData))
    },
    [fakeToken(), fakeRefreshToken(), merged] as const
  )

  return merged
}
