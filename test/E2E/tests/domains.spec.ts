import { test, expect } from '@playwright/test'
import { setupAuth } from '../fixtures/auth'

test.describe('Domains', () => {
  test.beforeEach(async ({ page }) => {
    await setupAuth(page)
  })

  test('renders the domains table with mock data', async ({ page }) => {
    await page.goto('/domains')

    await expect(page.getByTestId('domains-table')).toBeVisible()
    await expect(page.getByTestId('domains-card')).toBeVisible()

    // Should show the mock domain items
    const rows = page.getByTestId(/^domain-row-/)
    await expect(rows.first()).toBeVisible()
  })

  test('opens the add domain dialog', async ({ page }) => {
    await page.goto('/domains')

    await page.getByTestId('add-domain-btn').click()
    await expect(page.getByTestId('add-submit-btn')).toBeVisible()
    await expect(page.getByTestId('hostname-input')).toBeVisible()
    await expect(page.getByTestId('add-cancel-btn')).toBeVisible()
  })

  test('validates hostname in add domain dialog', async ({ page }) => {
    await page.goto('/domains')

    await page.getByTestId('add-domain-btn').click()

    // Enter invalid hostname with protocol
    const input = page.getByTestId('hostname-input').locator('input')
    await input.fill('https://example.com')
    await input.blur()

    // Should show validation error about https://
    await expect(page.getByText(/without https/)).toBeVisible()
  })

  test('closes add domain dialog with cancel', async ({ page }) => {
    await page.goto('/domains')

    await page.getByTestId('add-domain-btn').click()
    await expect(page.getByTestId('add-submit-btn')).toBeVisible()
    await page.getByTestId('add-cancel-btn').click()

    await expect(page.getByTestId('add-submit-btn')).not.toBeVisible()
  })

  test('navigates to domain detail page on row click', async ({ page }) => {
    await page.goto('/domains')

    const domainId = '550e8400-e29b-41d4-a716-446655440001'
    const link = page.getByTestId(`domain-hostname-${domainId}`)
    await link.click()

    await expect(page.getByTestId('domain-hostname')).toBeVisible({ timeout: 10_000 })
    await expect(page).toHaveURL(/\/domains\//)
  })

  test('domain detail shows DNS instructions', async ({ page }) => {
    const domainId = '550e8400-e29b-41d4-a716-446655440001'
    await page.goto(`/domains/${domainId}`)

    // The mock domain has status 'pending_verification', so DNS instructions should show
    await expect(page.getByTestId('dns-instructions-card')).toBeVisible()
    await expect(page.getByTestId('txt-name')).toBeVisible()
    await expect(page.getByTestId('txt-value')).toBeVisible()
    await expect(page.getByTestId('verify-btn')).toBeVisible()
  })

  test('domain detail shows settings', async ({ page }) => {
    const domainId = '550e8400-e29b-41d4-a716-446655440001'
    await page.goto(`/domains/${domainId}`)

    await expect(page.getByTestId('settings-card')).toBeVisible()
    await expect(page.getByTestId('default-redirect-input')).toBeVisible()
    await expect(page.getByTestId('not-found-select')).toBeVisible()
    await expect(page.getByTestId('save-settings-btn')).toBeVisible()
  })
})
