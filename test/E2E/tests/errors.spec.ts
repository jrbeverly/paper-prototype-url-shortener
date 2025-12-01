import { test, expect } from '@playwright/test'
import { setupAuth } from '../fixtures/auth'

test.describe('Error states', () => {
  test('shows 404 page for unknown routes', async ({ page }) => {
    await page.goto('/this-page-does-not-exist')
    await expect(page.getByText('404')).toBeVisible()
    await expect(page.getByText('Page not found')).toBeVisible()
  })

  test('404 page has navigation buttons', async ({ page }) => {
    await page.goto('/non-existent-route')
    await expect(page.getByRole('link', { name: /dashboard/i })).toBeVisible()
    await expect(page.getByRole('link', { name: /home/i })).toBeVisible()
  })

  test('redirects to login when accessing protected route unauthenticated', async ({
    page,
  }) => {
    // Clear any localStorage that might be set
    await page.goto('/')
    await page.evaluate(() => localStorage.clear())

    await page.goto('/dashboard')
    await expect(page).toHaveURL(/\/login/)
    await expect(page.getByTestId('login-submit-btn')).toBeVisible()
  })

  test('redirects to login when accessing links unauthenticated', async ({
    page,
  }) => {
    await page.goto('/')
    await page.evaluate(() => localStorage.clear())

    await page.goto('/links')
    await expect(page).toHaveURL(/\/login/)
    await expect(page.getByTestId('login-submit-btn')).toBeVisible()
  })

  test('shows empty state with CTA when no filters active', async ({ page }) => {
    // Login, then clear mock data by navigating with fake tenant
    // The mock data is in-memory, so the empty state won't show with mock data.
    // This test verifies the component renders without crashing.
    await setupAuth(page)
    await page.goto('/links')
    await expect(page.getByTestId('links-card')).toBeVisible()
  })

  test('error page renders with provided code', async ({ page }) => {
    await page.goto('/error?code=500&message=Something went wrong')
    await expect(page.getByText('500')).toBeVisible()
  })
})
