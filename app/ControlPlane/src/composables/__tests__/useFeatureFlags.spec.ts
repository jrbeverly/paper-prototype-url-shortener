import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { defineComponent, ref } from 'vue'
import { createPinia } from 'pinia'
import { VueQueryPlugin, QueryClient } from '@tanstack/vue-query'
import { useIsFeatureEnabled, useTenantFeatureFlags } from '../useFeatureFlags'
import type { FeatureFlagEvaluationsResponse } from '@/types/featureFlags'

vi.mock('@/services/featureFlagService', () => ({
  featureFlagService: {
    list: vi.fn().mockResolvedValue({ flags: [], totalCount: 0 }),
    get: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    delete: vi.fn(),
    evaluateAll: vi.fn(),
    evaluate: vi.fn(),
  },
}))

vi.mock('@/stores/workspace', () => ({
  useWorkspaceStore: vi.fn(() => ({ currentWorkspaceId: 'test-tenant-id' })),
}))

import { featureFlagService } from '@/services/featureFlagService'

function makeClient() {
  return new QueryClient({ defaultOptions: { queries: { retry: false } } })
}

function mountWithQuery<T>(composable: () => T) {
  let result!: T
  const TestComponent = defineComponent({
    setup() {
      result = composable()
      return {}
    },
    template: '<div/>',
  })
  const client = makeClient()
  mount(TestComponent, {
    global: {
      plugins: [[VueQueryPlugin, { queryClient: client }], createPinia()],
    },
  })
  return { result, client }
}

describe('useTenantFeatureFlags', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('calls evaluateAll with the current tenant id', async () => {
    const mockResponse: FeatureFlagEvaluationsResponse = {
      flags: [{ key: 'feature.links.csv', enabled: true, reason: 'boolean' }],
      totalCount: 1,
    }
    vi.mocked(featureFlagService.evaluateAll).mockResolvedValue(mockResponse)

    const { result } = mountWithQuery(() => useTenantFeatureFlags())
    await flushPromises()

    expect(featureFlagService.evaluateAll).toHaveBeenCalledWith('test-tenant-id')
    expect(result.data.value?.flags).toHaveLength(1)
  })

  it('returns empty flags when there are no flags', async () => {
    vi.mocked(featureFlagService.evaluateAll).mockResolvedValue({ flags: [], totalCount: 0 })

    const { result } = mountWithQuery(() => useTenantFeatureFlags())
    await flushPromises()

    expect(result.data.value?.flags).toHaveLength(0)
  })
})

describe('useIsFeatureEnabled', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('returns true for an enabled flag', async () => {
    vi.mocked(featureFlagService.evaluateAll).mockResolvedValue({
      flags: [
        { key: 'feature.links.csv', enabled: true, reason: 'boolean' },
        { key: 'feature.analytics.advanced', enabled: false, reason: 'kill_switch' },
      ],
      totalCount: 2,
    })

    const { result } = mountWithQuery(() => useIsFeatureEnabled('feature.links.csv'))
    await flushPromises()

    expect(result.value).toBe(true)
  })

  it('returns false for a disabled flag', async () => {
    vi.mocked(featureFlagService.evaluateAll).mockResolvedValue({
      flags: [{ key: 'feature.analytics.advanced', enabled: false, reason: 'kill_switch' }],
      totalCount: 1,
    })

    const { result } = mountWithQuery(() => useIsFeatureEnabled('feature.analytics.advanced'))
    await flushPromises()

    expect(result.value).toBe(false)
  })

  it('returns false when flag is not in the evaluations list', async () => {
    vi.mocked(featureFlagService.evaluateAll).mockResolvedValue({ flags: [], totalCount: 0 })

    const { result } = mountWithQuery(() => useIsFeatureEnabled('feature.unknown.flag'))
    await flushPromises()

    expect(result.value).toBe(false)
  })

  it('returns false while data is still loading', () => {
    // Never resolves — simulates in-flight request.
    vi.mocked(featureFlagService.evaluateAll).mockReturnValue(new Promise(() => {}))

    const { result } = mountWithQuery(() => useIsFeatureEnabled('feature.links.csv'))

    // Data is undefined during loading → should default to false.
    expect(result.value).toBe(false)
  })

  it('accepts a reactive key and re-evaluates on key change', async () => {
    vi.mocked(featureFlagService.evaluateAll).mockResolvedValue({
      flags: [
        { key: 'feature.a', enabled: true, reason: 'boolean' },
        { key: 'feature.b', enabled: false, reason: 'kill_switch' },
      ],
      totalCount: 2,
    })

    const key = ref('feature.a')
    const { result } = mountWithQuery(() => useIsFeatureEnabled(key))
    await flushPromises()

    expect(result.value).toBe(true)

    key.value = 'feature.b'
    await flushPromises()

    expect(result.value).toBe(false)
  })
})
