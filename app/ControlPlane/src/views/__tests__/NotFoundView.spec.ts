import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createRouter, createWebHistory } from 'vue-router'
import { createVuetify } from 'vuetify'
import * as components from 'vuetify/components'
import * as directives from 'vuetify/directives'
import NotFoundView from '../NotFoundView.vue'

const vuetify = createVuetify({ components, directives })

function createTestRouter() {
  return createRouter({
    history: createWebHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/dashboard', name: 'dashboard', component: { template: '<div />' } },
      { path: '/links', name: 'links', component: { template: '<div />' } },
    ],
  })
}

function mountComponent() {
  const router = createTestRouter()
  return { wrapper: mount(NotFoundView, { global: { plugins: [vuetify, router] } }), router }
}

describe('NotFoundView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders the 404 error code', () => {
    const { wrapper } = mountComponent()
    expect(wrapper.text()).toContain('404')
  })

  it('renders the page not found heading', () => {
    const { wrapper } = mountComponent()
    expect(wrapper.text()).toContain('Page not found')
  })

  it('renders the description text', () => {
    const { wrapper } = mountComponent()
    expect(wrapper.text()).toContain("doesn't exist or has been moved")
  })

  it('renders a search field', () => {
    const { wrapper } = mountComponent()
    const input = wrapper.find('input')
    expect(input.exists()).toBe(true)
  })

  it('renders a Go to Dashboard button', () => {
    const { wrapper } = mountComponent()
    expect(wrapper.text()).toContain('Go to Dashboard')
  })

  it('renders a Go to Home button', () => {
    const { wrapper } = mountComponent()
    expect(wrapper.text()).toContain('Go to Home')
  })

  it('navigates to links with query on search', async () => {
    const { wrapper, router } = mountComponent()
    const pushSpy = vi.spyOn(router, 'push')

    const input = wrapper.find('input')
    await input.setValue('my-link')
    await input.trigger('keyup.enter')

    expect(pushSpy).toHaveBeenCalledWith({ name: 'links', query: { q: 'my-link' } })
  })

  it('does not navigate when search query is blank', async () => {
    const { wrapper, router } = mountComponent()
    const pushSpy = vi.spyOn(router, 'push')

    const input = wrapper.find('input')
    await input.setValue('   ')
    await input.trigger('keyup.enter')

    expect(pushSpy).not.toHaveBeenCalled()
  })

  it('does not navigate when search query is empty', async () => {
    const { wrapper, router } = mountComponent()
    const pushSpy = vi.spyOn(router, 'push')

    const input = wrapper.find('input')
    await input.trigger('keyup.enter')

    expect(pushSpy).not.toHaveBeenCalled()
  })

  it('trims whitespace from search query before navigating', async () => {
    const { wrapper, router } = mountComponent()
    const pushSpy = vi.spyOn(router, 'push')

    const input = wrapper.find('input')
    await input.setValue('  my-link  ')
    await input.trigger('keyup.enter')

    expect(pushSpy).toHaveBeenCalledWith({ name: 'links', query: { q: 'my-link' } })
  })

  it('renders a broken link icon', () => {
    const { wrapper } = mountComponent()
    const icon = wrapper.find('.mdi-link-off')
    expect(icon.exists()).toBe(true)
  })
})
