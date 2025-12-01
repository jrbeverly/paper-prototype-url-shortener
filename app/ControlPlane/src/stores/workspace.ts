import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { Workspace } from '@/types/workspace'

const WORKSPACE_KEY = 'current_workspace_id'

export const useWorkspaceStore = defineStore('workspace', () => {
  const currentWorkspaceId = ref<string | null>(localStorage.getItem(WORKSPACE_KEY))
  const workspaces = ref<Workspace[]>([])

  const currentWorkspace = computed(
    () => workspaces.value.find((w) => w.id === currentWorkspaceId.value) ?? null
  )

  function setCurrentWorkspace(id: string) {
    currentWorkspaceId.value = id
    localStorage.setItem(WORKSPACE_KEY, id)
  }

  function setWorkspaces(list: Workspace[]) {
    workspaces.value = list
  }

  function reset() {
    currentWorkspaceId.value = null
    workspaces.value = []
    localStorage.removeItem(WORKSPACE_KEY)
  }

  return {
    currentWorkspaceId,
    workspaces,
    currentWorkspace,
    setCurrentWorkspace,
    setWorkspaces,
    reset,
  }
})
