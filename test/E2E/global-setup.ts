import { chromium, type Page } from '@playwright/test'

const BASE_URL = 'http://localhost:5173'
const NAV_TIMEOUT = 30_000

/**
 * Pre-warm Vite's on-demand compiler before any test worker starts.
 *
 * Vite compiles each module the first time it is requested. Without warmup,
 * the first test to navigate to each route pays the full compilation cost.
 * On constrained CI hardware this can exceed the expect.timeout, causing
 * spurious test failures.
 *
 * Public routes are warmed first. Authenticated routes (workspace) are warmed
 * after setting a minimal auth_token in localStorage — all services in this
 * codebase are mocked and perform no real API token validation, so a plain
 * string token is sufficient to pass the router guard without triggering a
 * logout redirect.
 *
 * This function is deliberately non-throwing: if any route fails to warm
 * (e.g. the runner is too slow or the browser can't launch), the error is
 * logged and tests continue — they rely on the extended expect.timeout.
 */
export default async function globalSetup(): Promise<void> {
  let browser
  try {
    browser = await chromium.launch({
      // Required in Docker/rootless CI environments that restrict the sandbox
      args: ['--no-sandbox', '--disable-setuid-sandbox'],
    })
    const context = await browser.newContext()
    const page = await context.newPage()

    await warmRoute(page, `${BASE_URL}/login`, '[data-testid="login-submit-btn"]')
    await warmRoute(page, `${BASE_URL}/register`, '[data-testid="register-submit-btn"]')

    // Warm the workspace settings page. Setting a token in localStorage before
    // the navigation satisfies the router's auth guard without a real backend.
    await page.evaluate(() => {
      localStorage.setItem('auth_token', 'warmup-token')
    })
    await warmRoute(page, `${BASE_URL}/workspace`, '[data-testid="general-card"]')
    await warmRoute(page, `${BASE_URL}/links`, '[data-testid="create-link-btn"]')
  } catch (err) {
    // Browser failed to launch or another non-recoverable error occurred.
    // Log and continue — tests will still run, compiling on demand.
    console.warn('[global-setup] warmup skipped:', (err as Error).message)
  } finally {
    await browser?.close()
  }
}

async function warmRoute(page: Page, url: string, sentinel?: string): Promise<void> {
  try {
    await page.goto(url, { timeout: NAV_TIMEOUT })
    if (sentinel) {
      await page.waitForSelector(sentinel, { timeout: NAV_TIMEOUT })
    }
  } catch (err) {
    console.warn(`[global-setup] ${url} warmup failed: ${(err as Error).message}`)
  }
}
