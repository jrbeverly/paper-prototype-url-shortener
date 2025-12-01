import { test, expect, type Page } from '@playwright/test'
import { setupAuth } from '../fixtures/auth'


// Waits until the workspace settings have been fetched and the name field is
// populated. The general form's name input stays empty until the TanStack
// Query resolves, so this is a reliable signal that settings data is available
// (which also means v-if="settings" in the template evaluates to true).
async function waitForSettings(page: Page) {
  await expect(page.getByTestId('name-input').locator('input')).not.toHaveValue('')
}

test.describe('Workspace settings', () => {
  test.beforeEach(async ({ page }) => {
    await setupAuth(page)
  })

  test('renders all settings sections', async ({ page }) => {
    await page.goto('/workspace')
    await waitForSettings(page)

    await expect(page.getByTestId('general-card')).toBeVisible()
    await expect(page.getByTestId('branding-card')).toBeVisible()
    await expect(page.getByTestId('defaults-card')).toBeVisible()
    await expect(page.getByTestId('team-card')).toBeVisible()
    await expect(page.getByTestId('danger-zone-card')).toBeVisible()
  })

  test('pre-fills name and slug from loaded settings', async ({ page }) => {
    await page.goto('/workspace')

    const nameInput = page.getByTestId('name-input').locator('input')
    const slugInput = page.getByTestId('slug-input').locator('input')

    await expect(nameInput).toHaveValue('My Workspace')
    await expect(slugInput).toHaveValue('my-workspace')
  })

  test('saves general settings and shows success alert', async ({ page }) => {
    await page.goto('/workspace')
    await waitForSettings(page)

    await page.getByTestId('general-save-btn').click()

    await expect(page.getByTestId('general-success')).toBeVisible()
  })

  test('saves branding settings and shows success alert', async ({ page }) => {
    await page.goto('/workspace')
    await waitForSettings(page)

    await page.getByTestId('branding-save-btn').click()

    await expect(page.getByTestId('branding-success')).toBeVisible()
  })

  test('saves defaults settings and shows success alert', async ({ page }) => {
    await page.goto('/workspace')
    await waitForSettings(page)

    await page.getByTestId('defaults-save-btn').click()

    await expect(page.getByTestId('defaults-success')).toBeVisible()
  })

  test('color picker button shows the current primary color', async ({ page }) => {
    await page.goto('/workspace')
    await waitForSettings(page)

    await expect(page.getByTestId('color-picker-btn')).toBeVisible()
    await expect(page.getByTestId('color-picker-btn')).toContainText('#1867C0')
  })

  test('team section shows coming soon placeholder', async ({ page }) => {
    await page.goto('/workspace')
    await waitForSettings(page)

    await expect(page.getByTestId('team-coming-soon')).toBeVisible()
    await expect(page.getByTestId('team-coming-soon')).toContainText('coming soon')
  })

  test('danger zone has delete workspace button', async ({ page }) => {
    await page.goto('/workspace')
    await waitForSettings(page)

    await expect(page.getByTestId('delete-workspace-btn')).toBeVisible()
  })

  test('opens delete workspace dialog when delete button is clicked', async ({ page }) => {
    await page.goto('/workspace')
    await waitForSettings(page)

    await page.getByTestId('delete-workspace-btn').click()

    await expect(page.getByTestId('confirm-name-input')).toBeVisible()
    await expect(page.getByTestId('delete-confirm-btn')).toBeVisible()
    await expect(page.getByTestId('delete-cancel-btn')).toBeVisible()
  })

  test('delete confirm button is disabled until workspace name is typed', async ({ page }) => {
    await page.goto('/workspace')
    await waitForSettings(page)

    await page.getByTestId('delete-workspace-btn').click()

    const confirmBtn = page.getByTestId('delete-confirm-btn')
    await expect(confirmBtn).toBeDisabled()

    await page.getByTestId('confirm-name-input').locator('input').fill('My Workspace')
    await expect(confirmBtn).toBeEnabled()
  })

  test('closes delete dialog when cancel is clicked', async ({ page }) => {
    await page.goto('/workspace')
    await waitForSettings(page)

    await page.getByTestId('delete-workspace-btn').click()
    await expect(page.getByTestId('confirm-name-input')).toBeVisible()

    await page.getByTestId('delete-cancel-btn').click()
    await expect(page.getByTestId('confirm-name-input')).not.toBeVisible()
  })

  test('workspace is accessible via the sidebar nav link', async ({ page }) => {
    await page.goto('/links')

    await page.getByRole('link', { name: 'Workspace' }).click()
    await expect(page).toHaveURL('/workspace')
    await waitForSettings(page)
  })
})
