import { toValue, type MaybeRefOrGetter } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { linkService } from '@/services/linkService'
import { useWorkspaceStore } from '@/stores/workspace'
import type { CreateLinkParams, LinkListQuery, UpdateLinkParams } from '@/types/links'

function getTenantId(): string {
  const workspaceStore = useWorkspaceStore()
  return workspaceStore.currentWorkspaceId ?? 'mock-tenant-id'
}

export function useLinks(query: MaybeRefOrGetter<LinkListQuery> = {}) {
  const tenantId = getTenantId()
  return useQuery({
    queryKey: ['links', query],
    queryFn: () => linkService.list(tenantId, toValue(query)),
    staleTime: 5 * 60 * 1000,
    retry: false,
  })
}

export function useLink(id: MaybeRefOrGetter<string>) {
  const tenantId = getTenantId()
  return useQuery({
    queryKey: ['links', id],
    queryFn: () => linkService.get(tenantId, toValue(id)),
    staleTime: 5 * 60 * 1000,
    retry: false,
  })
}

export function useCreateLink() {
  const queryClient = useQueryClient()
  const tenantId = getTenantId()
  return useMutation({
    mutationFn: (params: CreateLinkParams) => linkService.create(tenantId, params),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['links'] })
    },
  })
}

export function useUpdateLink(id: MaybeRefOrGetter<string>) {
  const queryClient = useQueryClient()
  const tenantId = getTenantId()
  return useMutation({
    mutationFn: (params: UpdateLinkParams) => linkService.update(tenantId, toValue(id), params),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['links'] })
    },
  })
}

export function useDeleteLink() {
  const queryClient = useQueryClient()
  const tenantId = getTenantId()
  return useMutation({
    mutationFn: (linkId: string) => linkService.delete(tenantId, linkId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['links'] })
    },
  })
}
