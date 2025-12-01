import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useWorkspaceStore } from '../workspace'

describe('useWorkspaceStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
  })

  it('starts with no current workspace or workspace list', () => {
    const store = useWorkspaceStore()
    expect(store.currentWorkspaceId).toBeNull()
    expect(store.workspaces).toHaveLength(0)
    expect(store.currentWorkspace).toBeNull()
  })

  it('stores the workspace list', () => {
    const store = useWorkspaceStore()
    store.setWorkspaces([
      { id: 'ws_1', name: 'Acme Corp', slug: 'acme' },
      { id: 'ws_2', name: 'Beta Inc', slug: 'beta' },
    ])
    expect(store.workspaces).toHaveLength(2)
  })

  it('sets the current workspace id and persists to localStorage', () => {
    const store = useWorkspaceStore()
    store.setCurrentWorkspace('ws_1')
    expect(store.currentWorkspaceId).toBe('ws_1')
    expect(localStorage.getItem('current_workspace_id')).toBe('ws_1')
  })

  it('resolves currentWorkspace from the workspace list', () => {
    const store = useWorkspaceStore()
    store.setWorkspaces([{ id: 'ws_1', name: 'Acme Corp', slug: 'acme' }])
    store.setCurrentWorkspace('ws_1')
    expect(store.currentWorkspace?.name).toBe('Acme Corp')
  })

  it('returns null for currentWorkspace when id does not match any workspace', () => {
    const store = useWorkspaceStore()
    store.setWorkspaces([{ id: 'ws_1', name: 'Acme Corp', slug: 'acme' }])
    store.setCurrentWorkspace('ws_unknown')
    expect(store.currentWorkspace).toBeNull()
  })

  it('restores current workspace id from localStorage on initialization', () => {
    localStorage.setItem('current_workspace_id', 'ws_1')
    const store = useWorkspaceStore()
    expect(store.currentWorkspaceId).toBe('ws_1')
  })

  it('resets all state and clears localStorage', () => {
    const store = useWorkspaceStore()
    store.setWorkspaces([{ id: 'ws_1', name: 'Acme Corp', slug: 'acme' }])
    store.setCurrentWorkspace('ws_1')
    store.reset()
    expect(store.currentWorkspaceId).toBeNull()
    expect(store.workspaces).toHaveLength(0)
    expect(store.currentWorkspace).toBeNull()
    expect(localStorage.getItem('current_workspace_id')).toBeNull()
  })
})
