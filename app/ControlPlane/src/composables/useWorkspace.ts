import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { workspaceService } from '@/services/workspaceService'
import type { UpdateWorkspaceSettingsParams } from '@/types/workspace'

export function useWorkspaceSettings(workspaceId: string) {
  return useQuery({
    queryKey: ['workspace', workspaceId, 'settings'],
    queryFn: () => workspaceService.getSettings(workspaceId),
    staleTime: 5 * 60 * 1000,
    retry: false,
  })
}

export function useUpdateWorkspaceSettings(workspaceId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (params: UpdateWorkspaceSettingsParams) =>
      workspaceService.updateSettings(workspaceId, params),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['workspace', workspaceId, 'settings'] })
    },
  })
}

export function useDeleteWorkspace() {
  return useMutation({
    mutationFn: (workspaceId: string) => workspaceService.deleteWorkspace(workspaceId),
  })
}
