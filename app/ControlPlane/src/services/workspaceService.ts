// TODO: Replace mock implementations with apiClient calls once workspace API endpoints are defined.

import { useWorkspaceStore } from '@/stores/workspace'
import type { WorkspaceSettings, UpdateWorkspaceSettingsParams } from '@/types/workspace'

const mockSettings: WorkspaceSettings = {
  id: 'ws_default',
  name: 'My Workspace',
  slug: 'my-workspace',
  branding: {
    primaryColor: '#1867C0',
    logoUrl: null,
  },
  defaults: {
    fallback404Url: null,
    defaultRedirectType: '302',
  },
}

const localSettings = {
  ...mockSettings,
  branding: { ...mockSettings.branding },
  defaults: { ...mockSettings.defaults },
}

export const workspaceService = {
  getSettings(workspaceId: string): Promise<WorkspaceSettings> {
    // TODO: const { data, error } = await apiClient.GET('/workspaces/{workspaceId}/settings', { params: { path: { workspaceId } } })
    void workspaceId
    return Promise.resolve({
      ...localSettings,
      branding: { ...localSettings.branding },
      defaults: { ...localSettings.defaults },
    })
  },

  updateSettings(
    workspaceId: string,
    params: UpdateWorkspaceSettingsParams
  ): Promise<WorkspaceSettings> {
    // TODO: const { data, error } = await apiClient.PATCH('/workspaces/{workspaceId}/settings', { params: { path: { workspaceId } }, body: params })
    void workspaceId
    if (params.name !== undefined) localSettings.name = params.name
    if (params.slug !== undefined) localSettings.slug = params.slug
    if (params.branding) {
      if (params.branding.primaryColor !== undefined)
        localSettings.branding.primaryColor = params.branding.primaryColor
      if (params.branding.logoUrl !== undefined)
        localSettings.branding.logoUrl = params.branding.logoUrl
    }
    if (params.defaults) {
      if (params.defaults.fallback404Url !== undefined)
        localSettings.defaults.fallback404Url = params.defaults.fallback404Url
      if (params.defaults.defaultRedirectType !== undefined)
        localSettings.defaults.defaultRedirectType = params.defaults.defaultRedirectType
    }

    const workspaceStore = useWorkspaceStore()
    const updated = workspaceStore.workspaces.find((w) => w.id === workspaceId)
    if (updated && params.name) {
      workspaceStore.setWorkspaces(
        workspaceStore.workspaces.map((w) =>
          w.id === workspaceId ? { ...w, name: params.name! } : w
        )
      )
    }

    return Promise.resolve({
      ...localSettings,
      branding: { ...localSettings.branding },
      defaults: { ...localSettings.defaults },
    })
  },

  deleteWorkspace(workspaceId: string): Promise<void> {
    // TODO: await apiClient.DELETE('/workspaces/{workspaceId}', { params: { path: { workspaceId } } })
    void workspaceId
    const workspaceStore = useWorkspaceStore()
    workspaceStore.setWorkspaces(workspaceStore.workspaces.filter((w) => w.id !== workspaceId))
    if (workspaceStore.currentWorkspaceId === workspaceId) {
      workspaceStore.reset()
    }
    return Promise.resolve()
  },
}
