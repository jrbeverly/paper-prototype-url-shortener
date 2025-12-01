import { test, expect } from '@playwright/test'

test.describe('Authentication', () => {
  test.describe('Login page', () => {
    test('renders the login form', async ({ page }) => {
      await page.goto('/login')

      await expect(page.getByTestId('email-input')).toBeVisible()
      await expect(page.getByTestId('password-input')).toBeVisible()
      await expect(page.getByTestId('remember-me-checkbox')).toBeVisible()
      await expect(page.getByTestId('login-submit-btn')).toBeVisible()
      await expect(page.getByTestId('login-submit-btn')).toContainText('Sign in')
    })

    test('shows a link to the registration page', async ({ page }) => {
      await page.goto('/login')

      const registerLink = page.getByTestId('register-link')
      await expect(registerLink).toBeVisible()
      await expect(registerLink).toContainText('Create account')

      await registerLink.click()
      await page.waitForURL('/register')
      await expect(page.getByTestId('register-submit-btn')).toBeVisible()
    })

    test('shows validation errors when submitting empty form', async ({ page }) => {
      await page.goto('/login')

      // Submit the empty form to trigger validation
      await page.getByTestId('login-submit-btn').click()

      // Vuetify shows validation messages below inputs
      await expect(page.getByText('Email is required')).toBeVisible()
      await expect(page.getByText('Password is required')).toBeVisible()
    })

    test('redirects unauthenticated users to login', async ({ page }) => {
      await page.goto('/links')
      await expect(page).toHaveURL(/\/login/)
      await expect(page.getByTestId('login-submit-btn')).toBeVisible()
    })
  })

  test.describe('Register page', () => {
    test('renders the registration form', async ({ page }) => {
      await page.goto('/register')

      await expect(page.getByTestId('register-name-input')).toBeVisible()
      await expect(page.getByTestId('register-email-input')).toBeVisible()
      await expect(page.getByTestId('register-password-input')).toBeVisible()
      await expect(page.getByTestId('register-confirm-password-input')).toBeVisible()
      await expect(page.getByTestId('register-terms-checkbox')).toBeVisible()
      await expect(page.getByTestId('register-submit-btn')).toBeVisible()
      await expect(page.getByTestId('register-submit-btn')).toContainText('Create account')
    })

    test('shows a link to the sign in page', async ({ page }) => {
      await page.goto('/register')

      const loginLink = page.getByTestId('login-link')
      await expect(loginLink).toBeVisible()
      await expect(loginLink).toContainText('Sign in')

      await loginLink.click()
      await page.waitForURL('/login')
      await expect(page.getByTestId('login-submit-btn')).toBeVisible()
    })

    test('shows validation errors on empty submit', async ({ page }) => {
      await page.goto('/register')

      await page.getByTestId('register-submit-btn').click()

      await expect(page.getByText('Name is required')).toBeVisible()
      await expect(page.getByText('Email is required')).toBeVisible()
      await expect(page.getByText('Password is required')).toBeVisible()
      await expect(page.getByText('Please confirm your password')).toBeVisible()
      await expect(page.getByText('You must accept the terms of service to continue')).toBeVisible()
    })

    test('shows error for mismatched passwords', async ({ page }) => {
      await page.goto('/register')

      await page.getByTestId('register-password-input').locator('input').fill('Password1!')
      await page.getByTestId('register-confirm-password-input').locator('input').fill('Different1!')
      await page.getByTestId('register-submit-btn').click()

      await expect(page.getByText('Passwords do not match')).toBeVisible()
    })

    test('shows password strength indicator when typing', async ({ page }) => {
      await page.goto('/register')

      await expect(page.getByTestId('password-strength-bar')).not.toBeVisible()

      await page.getByTestId('register-password-input').locator('input').fill('abcdefgh')
      await expect(page.getByTestId('password-strength-bar')).toBeVisible()
      await expect(page.getByTestId('password-strength-label')).toContainText('Weak')

      await page.getByTestId('register-password-input').locator('input').fill('Abc123!@#')
      await expect(page.getByTestId('password-strength-label')).toContainText('Strong')
    })
  })
})
