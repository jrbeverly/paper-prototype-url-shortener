import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { featureFlagService } from '@/services/featureFlagService'
import { useWorkspaceStore } from '@/stores/workspace'
import type { CreateFeatureFlagParams, UpdateFeatureFlagParams } from '@/types/featureFlags'

function getTenantId(): string {
  const workspaceStore = useWorkspaceStore()
  return workspaceStore.currentWorkspaceId ?? 'mock-tenant-id'
}

// ── Admin composables (flag management) ──────────────────────────────────────

/** List all feature flags. Admin use. */
export function useFeatureFlags() {
  return useQuery({
    queryKey: ['feature-flags'],
    queryFn: () => featureFlagService.list(),
    staleTime: 5 * 60 * 1000,
    retry: false,
  })
}

/** Get a single feature flag by key. Admin use. */
export function useFeatureFlag(key: MaybeRefOrGetter<string>) {
  return useQuery({
    queryKey: ['feature-flags', key],
    queryFn: () => featureFlagService.get(toValue(key)),
    staleTime: 5 * 60 * 1000,
    retry: false,
  })
}

/** Create a new feature flag. */
export function useCreateFeatureFlag() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (params: CreateFeatureFlagParams) => featureFlagService.create(params),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['feature-flags'] })
    },
  })
}

/** Partially update an existing feature flag. Set enabled=false to trigger the kill switch. */
export function useUpdateFeatureFlag(key: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (params: UpdateFeatureFlagParams) =>
      featureFlagService.update(toValue(key), params),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['feature-flags'] })
    },
  })
}

/** Permanently delete a feature flag (use after full rollout or cleanup). */
export function useDeleteFeatureFlag() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (key: string) => featureFlagService.delete(key),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['feature-flags'] })
    },
  })
}

// ── Tenant evaluation composables (conditional rendering) ────────────────────

/** Evaluate all feature flags for the current tenant. */
export function useTenantFeatureFlags() {
  const tenantId = getTenantId()
  return useQuery({
    queryKey: ['feature-flags', 'evaluations', tenantId],
    queryFn: () => featureFlagService.evaluateAll(tenantId),
    staleTime: 60 * 1000, // 1-minute TTL matches default server-side cache
    retry: false,
  })
}

/**
 * Returns a reactive boolean indicating whether a specific flag is enabled for the current tenant.
 * Intended for conditional rendering in components: `v-if="isCsvExportEnabled"`.
 */
export function useIsFeatureEnabled(flagKey: MaybeRefOrGetter<string>) {
  const { data } = useTenantFeatureFlags()
  return computed(() => {
    const key = toValue(flagKey)
    return data.value?.flags.find((f) => f.key === key)?.enabled ?? false
  })
}

/** Evaluate a single feature flag for the current tenant. */
export function useTenantFeatureFlag(flagKey: MaybeRefOrGetter<string>) {
  const tenantId = getTenantId()
  return useQuery({
    queryKey: ['feature-flags', 'evaluation', tenantId, flagKey],
    queryFn: () => featureFlagService.evaluate(tenantId, toValue(flagKey)),
    staleTime: 60 * 1000,
    retry: false,
  })
}
