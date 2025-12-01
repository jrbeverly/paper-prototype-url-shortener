import { test, expect } from '@playwright/test'
import { setupAuth } from '../fixtures/auth'

test.describe('API Keys', () => {
  test.beforeEach(async ({ page }) => {
    await setupAuth(page)
  })

  test('renders the API keys table with mock data', async ({ page }) => {
    await page.goto('/api-keys')

    await expect(page.getByTestId('keys-table')).toBeVisible()
    await expect(page.getByTestId('keys-card')).toBeVisible()

    // Should show the mock key items
    const rows = page.getByTestId(/^key-row-/)
    await expect(rows.first()).toBeVisible()
  })

  test('shows active and revoked key statuses', async ({ page }) => {
    await page.goto('/api-keys')

    // Production key (active)
    await expect(
      page.getByTestId('key-status-chip-550e8400-e29b-41d4-a716-446655440001')
    ).toContainText('Active')

    // Legacy CI key (revoked)
    await expect(
      page.getByTestId('key-status-chip-550e8400-e29b-41d4-a716-446655440003')
    ).toContainText('Revoked')
  })

  test('opens the create API key dialog', async ({ page }) => {
    await page.goto('/api-keys')

    await page.getByTestId('create-key-btn').click()
    await expect(page.getByTestId('create-submit-btn')).toBeVisible()
    await expect(page.getByTestId('key-name-input')).toBeVisible()
    await expect(page.getByTestId('key-role-select')).toBeVisible()
    await expect(page.getByTestId('create-cancel-btn')).toBeVisible()
  })

  test('closes create API key dialog with cancel', async ({ page }) => {
    await page.goto('/api-keys')

    await page.getByTestId('create-key-btn').click()
    await expect(page.getByTestId('create-submit-btn')).toBeVisible()
    await page.getByTestId('create-cancel-btn').click()

    await expect(page.getByTestId('create-submit-btn')).not.toBeVisible()
  })

  test('creates an API key and shows the reveal dialog', async ({ page }) => {
    await page.goto('/api-keys')

    await page.getByTestId('create-key-btn').click()

    await page.getByTestId('key-name-input').locator('input').fill('E2E Test Key')
    await page.getByTestId('key-role-select').click()
    await page.getByRole('option', { name: /member/i }).click()

    await page.getByTestId('create-submit-btn').click()

    // Key reveal dialog should appear
    await expect(page.getByTestId('key-value')).toBeVisible()
    await expect(page.getByTestId('reveal-warning')).toBeVisible()
    await expect(page.getByTestId('reveal-done-btn')).toBeVisible()
  })

  test('closes key reveal dialog', async ({ page }) => {
    await page.goto('/api-keys')

    await page.getByTestId('create-key-btn').click()

    await page.getByTestId('key-name-input').locator('input').fill('E2E Test Key')
    await page.getByTestId('create-submit-btn').click()

    await expect(page.getByTestId('reveal-done-btn')).toBeVisible()
    await page.getByTestId('reveal-done-btn').click()

    // Should be back on the list view
    await expect(page.getByTestId('keys-table')).toBeVisible()
  })

  test('opens revoke dialog for an active key', async ({ page }) => {
    await page.goto('/api-keys')

    const keyId = '550e8400-e29b-41d4-a716-446655440001'
    await page.getByTestId(`revoke-btn-${keyId}`).click()

    await expect(page.getByTestId('revoke-confirm-btn')).toBeVisible()
    await expect(page.getByTestId('revoke-cancel-btn')).toBeVisible()
  })
})
