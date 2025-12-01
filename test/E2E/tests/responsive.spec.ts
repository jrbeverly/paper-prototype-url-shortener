import { test, expect } from '@playwright/test'
import { setupAuth } from '../fixtures/auth'

test.describe('Responsive layout', () => {
  test.describe('Mobile viewport', () => {
    test.use({ viewport: { width: 375, height: 667 } })

    test('login page is usable on mobile', async ({ page }) => {
      await page.goto('/login')

      await expect(page.getByTestId('email-input')).toBeVisible()
      await expect(page.getByTestId('password-input')).toBeVisible()
      await expect(page.getByTestId('login-submit-btn')).toBeVisible()
    })

    test('register page is usable on mobile', async ({ page }) => {
      await page.goto('/register')

      await expect(page.getByTestId('register-name-input')).toBeVisible()
      await expect(page.getByTestId('register-email-input')).toBeVisible()
      await expect(page.getByTestId('register-password-input')).toBeVisible()
      await expect(page.getByTestId('register-submit-btn')).toBeVisible()
    })

    test('links page is usable on mobile', async ({ page }) => {
      await setupAuth(page)
      await page.goto('/links')

      await expect(page.getByTestId('links-table')).toBeVisible()
      await expect(page.getByTestId('create-link-btn')).toBeVisible()
    })

    test('link detail page is usable on mobile', async ({ page }) => {
      await setupAuth(page)
      const linkId = '550e8400-e29b-41d4-a716-446655550001'
      await page.goto(`/links/${linkId}`)

      await expect(page.getByTestId('link-short-url')).toBeVisible()
      await expect(page.getByTestId('back-btn')).toBeVisible()
    })

    test('domains page is usable on mobile', async ({ page }) => {
      await setupAuth(page)
      await page.goto('/domains')

      await expect(page.getByTestId('domains-table')).toBeVisible()
      await expect(page.getByTestId('add-domain-btn')).toBeVisible()
    })

    test('API keys page is usable on mobile', async ({ page }) => {
      await setupAuth(page)
      await page.goto('/api-keys')

      await expect(page.getByTestId('keys-table')).toBeVisible()
      await expect(page.getByTestId('create-key-btn')).toBeVisible()
    })
  })

  test.describe('Tablet viewport', () => {
    test.use({ viewport: { width: 768, height: 1024 } })

    test('login page renders on tablet', async ({ page }) => {
      await page.goto('/login')
      await expect(page.getByTestId('login-submit-btn')).toBeVisible()
    })

    test('links page renders on tablet', async ({ page }) => {
      await setupAuth(page)
      await page.goto('/links')
      await expect(page.getByTestId('links-table')).toBeVisible()
    })
  })
})
