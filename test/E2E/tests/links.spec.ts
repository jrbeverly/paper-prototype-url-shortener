import { test, expect } from '@playwright/test'
import { setupAuth } from '../fixtures/auth'

test.describe('Links', () => {
  test.beforeEach(async ({ page }) => {
    await setupAuth(page)
  })

  test('renders the links table with mock data', async ({ page }) => {
    await page.goto('/links')

    await expect(page.getByTestId('links-table')).toBeVisible()
    await expect(page.getByTestId('links-card')).toBeVisible()

    // Should show mock link items
    const rows = page.getByTestId(/^link-row-/)
    await expect(rows.first()).toBeVisible()
  })

  test('opens the create link dialog', async ({ page }) => {
    await page.goto('/links')

    await page.getByTestId('create-link-btn').click()
    await expect(page.getByTestId('create-submit-btn')).toBeVisible()
    await expect(page.getByTestId('domain-select')).toBeVisible()
    await expect(page.getByTestId('destination-input')).toBeVisible()
    await expect(page.getByTestId('slug-input')).toBeVisible()
  })

  test('closes the create link dialog with cancel', async ({ page }) => {
    await page.goto('/links')

    await page.getByTestId('create-link-btn').click()
    await expect(page.getByTestId('create-submit-btn')).toBeVisible()
    await page.getByTestId('create-cancel-btn').click()

    await expect(page.getByTestId('create-submit-btn')).not.toBeVisible()
  })

  test('searches links by text', async ({ page }) => {
    await page.goto('/links')

    const input = page.getByTestId('search-input').locator('input')
    await input.fill('launch')
    await page.waitForTimeout(500)

    // Only matching links should be visible
    await expect(page.getByTestId('link-slug-550e8400-e29b-41d4-a716-446655550001')).toBeVisible()
  })

  test('filters links by status', async ({ page }) => {
    await page.goto('/links')

    const statusFilter = page.getByTestId('status-filter')
    await statusFilter.click()

    // Select "Active" from the dropdown
    await page.getByRole('option', { name: 'Active' }).click()

    // Should only show active links
    await expect(page.getByTestId('link-slug-550e8400-e29b-41d4-a716-446655550001')).toBeVisible()
  })

  test('navigates to link detail page', async ({ page }) => {
    await page.goto('/links')

    const linkId = '550e8400-e29b-41d4-a716-446655550001'
    await page.getByTestId(`link-slug-${linkId}`).click()

    await page.waitForURL(`/links/${linkId}`)
    await expect(page.getByTestId('link-destination')).toBeVisible()
    await expect(page.getByTestId('link-properties-card')).toBeVisible()
    await expect(page.getByTestId('back-btn')).toBeVisible()
  })

  test('navigates back from detail to list', async ({ page }) => {
    const linkId = '550e8400-e29b-41d4-a716-446655550001'
    await page.goto(`/links/${linkId}`)

    await page.getByTestId('back-btn').click()
    await page.waitForURL('/links')
    await expect(page.getByTestId('links-table')).toBeVisible()
  })

  test('opens delete dialog from list', async ({ page }) => {
    await page.goto('/links')

    const linkId = '550e8400-e29b-41d4-a716-446655550001'
    await page.getByTestId(`delete-btn-${linkId}`).click()

    await expect(page.getByTestId('delete-confirm-btn')).toBeVisible()
    await expect(page.getByTestId('delete-cancel-btn')).toBeVisible()
  })

  test('closes delete dialog with cancel', async ({ page }) => {
    await page.goto('/links')

    const linkId = '550e8400-e29b-41d4-a716-446655550001'
    await page.getByTestId(`delete-btn-${linkId}`).click()
    await expect(page.getByTestId('delete-confirm-btn')).toBeVisible()

    await page.getByTestId('delete-cancel-btn').click()
    await expect(page.getByTestId('delete-confirm-btn')).not.toBeVisible()
  })

  test('opens edit dialog from detail page', async ({ page }) => {
    const linkId = '550e8400-e29b-41d4-a716-446655550001'
    await page.goto(`/links/${linkId}`)

    await page.getByTestId('edit-btn').click()
    await expect(page.getByTestId('edit-save-btn')).toBeVisible()
    await expect(page.getByTestId('edit-destination-input')).toBeVisible()
    await expect(page.getByTestId('edit-cancel-btn')).toBeVisible()
  })

  test('closes edit dialog with cancel', async ({ page }) => {
    const linkId = '550e8400-e29b-41d4-a716-446655550001'
    await page.goto(`/links/${linkId}`)

    await page.getByTestId('edit-btn').click()
    await expect(page.getByTestId('edit-save-btn')).toBeVisible()
    await page.getByTestId('edit-cancel-btn').click()

    await expect(page.getByTestId('edit-save-btn')).not.toBeVisible()
  })

  test('link detail shows analytics cards', async ({ page }) => {
    const linkId = '550e8400-e29b-41d4-a716-446655550001'
    await page.goto(`/links/${linkId}`)

    await expect(page.getByTestId('clicks-card')).toBeVisible()
    await expect(page.getByTestId('unique-clicks-card')).toBeVisible()
    await expect(page.getByTestId('clicks-today-card')).toBeVisible()
    await expect(page.getByTestId('link-properties-card')).toBeVisible()
    await expect(page.getByTestId('audit-trail-card')).toBeVisible()
  })
})
