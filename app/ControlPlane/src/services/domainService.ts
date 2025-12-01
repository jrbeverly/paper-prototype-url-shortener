// TODO: Replace mock implementations with apiClient calls once domain API endpoints are wired up.
// All functions are shaped to match the expected API contract so the switch-over is a one-line change per function.

import type {
  AddDomainParams,
  Domain,
  DomainCreated,
  DomainDetail,
  DomainList,
  DomainVerifyResult,
  UpdateDomainParams,
} from '@/types/domains'

export const domainService = {
  async list(tenantId: string, page = 1, pageSize = 20): Promise<DomainList> {
    // TODO: const { data, error } = await apiClient.GET('/tenants/{tenantId}/domains', { params: { path: { tenantId }, query: { page, pageSize } } })
    void tenantId
    const items: Domain[] = [
      {
        id: '550e8400-e29b-41d4-a716-446655440001',
        hostname: 'go.acme.com',
        status: 'active',
        certificateStatus: 'issued',
        linkCount: 42,
        createdAt: '2025-03-01T10:00:00Z',
      },
      {
        id: '550e8400-e29b-41d4-a716-446655440002',
        hostname: 'links.example.org',
        status: 'pending_verification',
        certificateStatus: 'pending',
        linkCount: 0,
        createdAt: '2025-05-20T14:30:00Z',
      },
      {
        id: '550e8400-e29b-41d4-a716-446655440003',
        hostname: 'short.mybrand.io',
        status: 'verification_failed',
        certificateStatus: 'pending',
        linkCount: 0,
        createdAt: '2025-05-25T09:15:00Z',
      },
    ]
    return { items, page, pageSize, totalCount: items.length }
  },

  async get(tenantId: string, domainId: string): Promise<DomainDetail> {
    // TODO: const { data, error } = await apiClient.GET('/tenants/{tenantId}/domains/{domainId}', { params: { path: { tenantId, domainId } } })
    void tenantId
    return {
      id: domainId,
      tenantId,
      hostname: 'go.acme.com',
      status: 'pending_verification',
      certificateStatus: 'pending',
      linkCount: 0,
      settings: {
        defaultRedirectUrl: null,
        errorPageBranding: null,
        notFoundBehavior: '404',
      },
      verificationInstructions: {
        txtName: 'go.acme.com',
        txtValue: 'short-io-verify=mock1a2b3c4d5e6f7g8h9i0j',
        cnameName: 'go.acme.com',
        cnameValue: 'cname.short.io',
      },
      createdAt: '2025-03-01T10:00:00Z',
      updatedAt: null,
      deletedAt: null,
    }
  },

  async create(tenantId: string, params: AddDomainParams): Promise<DomainCreated> {
    // TODO: const { data, error } = await apiClient.POST('/tenants/{tenantId}/domains', { params: { path: { tenantId } }, body: params })
    void tenantId
    const hostname = params.hostname.trim().toLowerCase()
    return {
      id: crypto.randomUUID(),
      hostname,
      status: 'pending_verification',
      verificationInstructions: {
        txtName: hostname,
        txtValue: `short-io-verify=mock${Date.now().toString(16)}`,
        cnameName: hostname,
        cnameValue: 'cname.short.io',
      },
      createdAt: new Date().toISOString(),
    }
  },

  async update(
    tenantId: string,
    domainId: string,
    params: UpdateDomainParams
  ): Promise<DomainDetail> {
    // TODO: const { data, error } = await apiClient.PATCH('/tenants/{tenantId}/domains/{domainId}', { params: { path: { tenantId, domainId } }, body: params })
    void params
    const current = await domainService.get(tenantId, domainId)
    return {
      ...current,
      settings: {
        ...current.settings,
        ...(params.defaultRedirectUrl !== undefined && {
          defaultRedirectUrl: params.defaultRedirectUrl,
        }),
        ...(params.errorPageBranding !== undefined && {
          errorPageBranding: params.errorPageBranding,
        }),
        ...(params.notFoundBehavior !== undefined && { notFoundBehavior: params.notFoundBehavior }),
      },
      updatedAt: new Date().toISOString(),
    }
  },

  async delete(tenantId: string, domainId: string): Promise<void> {
    // TODO: await apiClient.DELETE('/tenants/{tenantId}/domains/{domainId}', { params: { path: { tenantId, domainId } } })
    void tenantId
    void domainId
  },

  async verify(tenantId: string, domainId: string): Promise<DomainVerifyResult> {
    // TODO: const { data, error } = await apiClient.POST('/tenants/{tenantId}/domains/{domainId}/verify', { params: { path: { tenantId, domainId } } })
    void tenantId
    return {
      id: domainId,
      hostname: 'go.acme.com',
      status: 'verification_failed',
      txtCheck: {
        passed: false,
        expected: 'short-io-verify=mock1a2b3c4d5e6f7g8h9i0j',
        actual: null,
        error: 'TXT record not found',
      },
      cnameCheck: {
        passed: false,
        expected: 'cname.short.io',
        actual: null,
        error: 'CNAME record not found',
      },
      message: 'Verification failed. Check the DNS record details below.',
      checkedAt: new Date().toISOString(),
    }
  },
}
