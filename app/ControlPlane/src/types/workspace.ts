export interface Workspace {
  id: string
  name: string
  slug: string
}

export interface WorkspaceSettings {
  id: string
  name: string
  slug: string
  branding: {
    primaryColor: string
    logoUrl: string | null
  }
  defaults: {
    fallback404Url: string | null
    defaultRedirectType: '301' | '302'
  }
}

export interface UpdateWorkspaceSettingsParams {
  name?: string
  slug?: string
  branding?: {
    primaryColor?: string
    logoUrl?: string | null
  }
  defaults?: {
    fallback404Url?: string | null
    defaultRedirectType?: '301' | '302'
  }
}
