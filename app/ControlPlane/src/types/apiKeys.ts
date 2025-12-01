export interface ApiKey {
  id: string
  name: string
  keyPreview: string
  keyPrefix: string
  createdAt: string
  lastUsedAt: string | null
  permissions: string[]
  isRevoked: boolean
}

export interface ApiKeyCreated {
  id: string
  name: string
  key: string
  keyPrefix: string
  createdAt: string
  permissions: string[]
}

export interface CreateApiKeyParams {
  name: string
  permissions: string[]
}
