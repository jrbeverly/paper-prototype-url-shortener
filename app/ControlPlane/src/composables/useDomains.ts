import { toValue, type MaybeRefOrGetter } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { domainService } from '@/services/domainService'
import { useWorkspaceStore } from '@/stores/workspace'
import type { AddDomainParams, UpdateDomainParams } from '@/types/domains'

function getTenantId(): string {
  const workspaceStore = useWorkspaceStore()
  return workspaceStore.currentWorkspaceId ?? 'mock-tenant-id'
}

export function useDomains() {
  const tenantId = getTenantId()
  return useQuery({
    queryKey: ['domains'],
    queryFn: () => domainService.list(tenantId),
    staleTime: 5 * 60 * 1000,
    retry: false,
  })
}

export function useDomain(id: MaybeRefOrGetter<string>) {
  const tenantId = getTenantId()
  return useQuery({
    queryKey: ['domains', id],
    queryFn: () => domainService.get(tenantId, toValue(id)),
    staleTime: 5 * 60 * 1000,
    retry: false,
  })
}

export function useAddDomain() {
  const queryClient = useQueryClient()
  const tenantId = getTenantId()
  return useMutation({
    mutationFn: (params: AddDomainParams) => domainService.create(tenantId, params),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['domains'] })
    },
  })
}

export function useUpdateDomain(id: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient()
  const tenantId = getTenantId()
  return useMutation({
    mutationFn: (params: UpdateDomainParams) => domainService.update(tenantId, toValue(id), params),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['domains', toValue(id)] })
    },
  })
}

export function useDeleteDomain() {
  const queryClient = useQueryClient()
  const tenantId = getTenantId()
  return useMutation({
    mutationFn: (domainId: string) => domainService.delete(tenantId, domainId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['domains'] })
    },
  })
}

export function useVerifyDomain() {
  const queryClient = useQueryClient()
  const tenantId = getTenantId()
  return useMutation({
    mutationFn: (domainId: string) => domainService.verify(tenantId, domainId),
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: ['domains', result.id] })
      queryClient.invalidateQueries({ queryKey: ['domains'] })
    },
  })
}
