// TODO: Replace mock implementations with apiClient calls once billing API endpoints are defined.
// All functions are shaped to match the expected API contract so the switch-over is a one-line change per function.

import type { ApiKey, ApiKeyCreated, CreateApiKeyParams } from '@/types/apiKeys'

export const apiKeyService = {
  async list(tenantId: string): Promise<ApiKey[]> {
    // TODO: const { data, error } = await apiClient.GET('/tenants/{tenantId}/api-keys', { params: { path: { tenantId } } })
    void tenantId
    return [
      {
        id: '550e8400-e29b-41d4-a716-446655440001',
        name: 'Production',
        keyPreview: 'sk_abc12345****...****5678',
        keyPrefix: 'sk_abc12345',
        createdAt: '2025-05-01T10:00:00Z',
        lastUsedAt: '2025-06-01T14:30:00Z',
        permissions: ['viewer'],
        isRevoked: false,
      },
      {
        id: '550e8400-e29b-41d4-a716-446655440002',
        name: 'Development',
        keyPreview: 'sk_xyz98765****...****4321',
        keyPrefix: 'sk_xyz98765',
        createdAt: '2025-04-15T09:00:00Z',
        lastUsedAt: null,
        permissions: ['member'],
        isRevoked: false,
      },
      {
        id: '550e8400-e29b-41d4-a716-446655440003',
        name: 'Legacy CI',
        keyPreview: 'sk_old11111****...****9999',
        keyPrefix: 'sk_old11111',
        createdAt: '2025-01-01T00:00:00Z',
        lastUsedAt: '2025-03-10T08:00:00Z',
        permissions: ['viewer'],
        isRevoked: true,
      },
    ]
  },

  async create(tenantId: string, params: CreateApiKeyParams): Promise<ApiKeyCreated> {
    // TODO: const { data, error } = await apiClient.POST('/tenants/{tenantId}/api-keys', { params: { path: { tenantId } }, body: params })
    void tenantId
    const randomBytes = crypto.getRandomValues(new Uint8Array(24))
    const encoded = Array.from(randomBytes)
      .map((b) => b.toString(16).padStart(2, '0'))
      .join('')
    const key = `sk_${encoded}`
    return {
      id: crypto.randomUUID(),
      name: params.name,
      key,
      keyPrefix: `sk_${encoded.slice(0, 8)}`,
      createdAt: new Date().toISOString(),
      permissions: params.permissions,
    }
  },

  async revoke(tenantId: string, keyId: string): Promise<void> {
    // TODO: await apiClient.DELETE('/tenants/{tenantId}/api-keys/{keyId}', { params: { path: { tenantId, keyId } } })
    void tenantId
    void keyId
  },
}
