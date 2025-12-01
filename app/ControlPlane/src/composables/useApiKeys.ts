import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { apiKeyService } from '@/services/apiKeyService'
import { useWorkspaceStore } from '@/stores/workspace'
import type { CreateApiKeyParams } from '@/types/apiKeys'

function getTenantId(): string {
  const workspaceStore = useWorkspaceStore()
  return workspaceStore.currentWorkspaceId ?? 'mock-tenant-id'
}

export function useApiKeys() {
  const tenantId = getTenantId()
  return useQuery({
    queryKey: ['api-keys'],
    queryFn: () => apiKeyService.list(tenantId),
    staleTime: 5 * 60 * 1000,
    retry: false,
  })
}

export function useCreateApiKey() {
  const queryClient = useQueryClient()
  const tenantId = getTenantId()
  return useMutation({
    mutationFn: (params: CreateApiKeyParams) => apiKeyService.create(tenantId, params),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['api-keys'] })
    },
  })
}

export function useRevokeApiKey() {
  const queryClient = useQueryClient()
  const tenantId = getTenantId()
  return useMutation({
    mutationFn: (keyId: string) => apiKeyService.revoke(tenantId, keyId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['api-keys'] })
    },
  })
}
