// TODO: Replace mock implementations with apiClient calls once link API endpoints are wired up.
// All functions are shaped to match the expected API contract so the switch-over is a one-line change per function.

import type {
  CreateLinkParams,
  LinkDetail,
  LinkListItem,
  LinkListQuery,
  LinkListResponse,
  UpdateLinkParams,
} from '@/types/links'

const mockLinks: LinkListItem[] = [
  {
    id: '550e8400-e29b-41d4-a716-446655550001',
    domainId: '550e8400-e29b-41d4-a716-446655440001',
    domainHostname: 'go.acme.com',
    slug: 'launch',
    destinationUrl: 'https://acme.com/product-launch-2025',
    shortUrl: 'https://go.acme.com/launch',
    redirectType: '302',
    status: 'active',
    clickCount: 1284,
    maxClicks: null,
    expiresAt: null,
    createdAt: '2025-01-10T09:00:00Z',
    updatedAt: '2025-01-10T09:00:00Z',
    version: 1,
    expiryWarning: null,
  },
  {
    id: '550e8400-e29b-41d4-a716-446655550002',
    domainId: '550e8400-e29b-41d4-a716-446655440001',
    domainHostname: 'go.acme.com',
    slug: 'pricing',
    destinationUrl: 'https://acme.com/pricing',
    shortUrl: 'https://go.acme.com/pricing',
    redirectType: '301',
    status: 'active',
    clickCount: 537,
    maxClicks: null,
    expiresAt: null,
    createdAt: '2025-02-15T11:30:00Z',
    updatedAt: '2025-02-15T11:30:00Z',
    version: 1,
    expiryWarning: null,
  },
  {
    id: '550e8400-e29b-41d4-a716-446655550003',
    domainId: '550e8400-e29b-41d4-a716-446655440001',
    domainHostname: 'go.acme.com',
    slug: 'summer25',
    destinationUrl: 'https://acme.com/campaigns/summer-2025',
    shortUrl: 'https://go.acme.com/summer25',
    redirectType: '302',
    status: 'active',
    clickCount: 92,
    maxClicks: 100,
    expiresAt: '2025-09-01T00:00:00Z',
    createdAt: '2025-06-01T08:00:00Z',
    updatedAt: '2025-06-01T08:00:00Z',
    version: 1,
    expiryWarning: {
      isNearingExpiry: true,
      type: 'clicks',
      message: '8 clicks remaining before this link expires',
      remainingClicks: 8,
      remainingDays: null,
    },
  },
  {
    id: '550e8400-e29b-41d4-a716-446655550004',
    domainId: '550e8400-e29b-41d4-a716-446655440001',
    domainHostname: 'go.acme.com',
    slug: 'docs',
    destinationUrl: 'https://docs.acme.com',
    shortUrl: 'https://go.acme.com/docs',
    redirectType: '302',
    status: 'paused',
    clickCount: 321,
    maxClicks: null,
    expiresAt: null,
    createdAt: '2025-03-20T14:00:00Z',
    updatedAt: '2025-05-01T10:00:00Z',
    version: 2,
    expiryWarning: null,
  },
  {
    id: '550e8400-e29b-41d4-a716-446655550005',
    domainId: '550e8400-e29b-41d4-a716-446655440001',
    domainHostname: 'go.acme.com',
    slug: 'blog',
    destinationUrl: 'https://acme.com/blog',
    shortUrl: 'https://go.acme.com/blog',
    redirectType: '302',
    status: 'active',
    clickCount: 2047,
    maxClicks: null,
    expiresAt: null,
    createdAt: '2025-01-05T07:00:00Z',
    updatedAt: '2025-01-05T07:00:00Z',
    version: 1,
    expiryWarning: null,
  },
  {
    id: '550e8400-e29b-41d4-a716-446655550006',
    domainId: '550e8400-e29b-41d4-a716-446655440001',
    domainHostname: 'go.acme.com',
    slug: 'winter24',
    destinationUrl: 'https://acme.com/campaigns/winter-2024',
    shortUrl: 'https://go.acme.com/winter24',
    redirectType: '302',
    status: 'expired',
    clickCount: 5830,
    maxClicks: null,
    expiresAt: '2025-01-31T00:00:00Z',
    createdAt: '2024-12-01T00:00:00Z',
    updatedAt: '2024-12-01T00:00:00Z',
    version: 1,
    expiryWarning: null,
  },
]

let localLinks = [...mockLinks]

function applyQuery(links: LinkListItem[], query: LinkListQuery): LinkListItem[] {
  let result = [...links]

  if (query.search) {
    const term = query.search.toLowerCase()
    result = result.filter(
      (l) => l.slug.toLowerCase().includes(term) || l.destinationUrl.toLowerCase().includes(term)
    )
  }

  if (query.domainId) {
    result = result.filter((l) => l.domainId === query.domainId)
  }

  if (query.status) {
    result = result.filter((l) => l.status === query.status)
  }

  return result
}

const EXISTING_SLUGS = new Set(mockLinks.map((l) => `${l.domainId}:${l.slug}`))

export const linkService = {
  async list(tenantId: string, query: LinkListQuery = {}): Promise<LinkListResponse> {
    // TODO: const { data, error } = await apiClient.GET('/tenants/{tenantId}/links', { params: { path: { tenantId }, query } })
    void tenantId
    const pageSize = query.pageSize ?? 20
    const filtered = applyQuery(localLinks, query)
    const startIndex = query.cursor
      ? filtered.findIndex((l) => l.id === atob(query.cursor!)) + 1
      : 0
    const page = filtered.slice(startIndex, startIndex + pageSize)
    const lastItem = page[page.length - 1]
    const hasMore = startIndex + pageSize < filtered.length
    return {
      items: page,
      nextCursor: hasMore && lastItem ? btoa(lastItem.id) : null,
      totalCount: filtered.length,
    }
  },

  async get(tenantId: string, linkId: string): Promise<LinkDetail> {
    // TODO: const { data, error } = await apiClient.GET('/tenants/{tenantId}/links/{linkId}', { params: { path: { tenantId, linkId } } })
    void tenantId
    const link = localLinks.find((l) => l.id === linkId)
    if (!link) throw new Error(`Link ${linkId} not found`)

    const totalClicks = link.clickCount
    const uniqueClicks = Math.floor(totalClicks * 0.72)
    const clicksToday = Math.max(1, Math.floor(totalClicks * 0.04))

    const days = Math.min(30, Math.max(1, daysSince(link.createdAt)))
    const trend = generateTrend(days, totalClicks, linkId)

    return {
      ...link,
      analytics: {
        totalClicks,
        uniqueClicks,
        clicksToday,
        clickTrend: trend,
        topCountries: [
          {
            countryCode: 'US',
            countryName: 'United States',
            count: Math.floor(totalClicks * 0.35),
          },
          {
            countryCode: 'GB',
            countryName: 'United Kingdom',
            count: Math.floor(totalClicks * 0.15),
          },
          { countryCode: 'DE', countryName: 'Germany', count: Math.floor(totalClicks * 0.1) },
          { countryCode: 'JP', countryName: 'Japan', count: Math.floor(totalClicks * 0.07) },
          { countryCode: 'BR', countryName: 'Brazil', count: Math.floor(totalClicks * 0.05) },
        ].filter((c) => c.count > 0),
        topReferrers: [
          { domain: 'google.com', count: Math.floor(totalClicks * 0.25) },
          { domain: 'twitter.com', count: Math.floor(totalClicks * 0.18) },
          { domain: 'linkedin.com', count: Math.floor(totalClicks * 0.12) },
          { domain: 'github.com', count: Math.floor(totalClicks * 0.08) },
          { domain: 'facebook.com', count: Math.floor(totalClicks * 0.05) },
        ].filter((r) => r.count > 0),
        devices: {
          desktop: Math.floor(uniqueClicks * 0.55),
          mobile: Math.floor(uniqueClicks * 0.35),
          tablet: Math.floor(uniqueClicks * 0.1),
        },
      },
      auditTrail: [
        {
          version: 1,
          action: 'created',
          description: 'Link created',
          timestamp: link.createdAt,
        },
        ...(link.version > 1
          ? [
              {
                version: link.version,
                action: 'updated',
                description: 'Link configuration updated',
                timestamp: link.updatedAt,
              },
            ]
          : []),
      ],
    }
  },

  async create(tenantId: string, params: CreateLinkParams): Promise<LinkListItem> {
    // TODO: const { data, error } = await apiClient.POST('/tenants/{tenantId}/links', { params: { path: { tenantId } }, body: params })
    void tenantId
    const slug = params.slug?.trim() || Math.random().toString(36).slice(2, 8)
    const domain = localLinks.find((l) => l.domainId === params.domainId)
    const domainHostname = domain?.domainHostname ?? 'short.io'
    const link: LinkListItem = {
      id: crypto.randomUUID(),
      domainId: params.domainId,
      domainHostname,
      slug,
      destinationUrl: params.destinationUrl,
      shortUrl: `https://${domainHostname}/${slug}`,
      redirectType: params.redirectType ?? '302',
      status: 'active',
      clickCount: 0,
      maxClicks: params.maxClicks ?? null,
      expiresAt: params.expiresAt ?? null,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      version: 1,
      expiryWarning: null,
    }
    localLinks = [link, ...localLinks]
    EXISTING_SLUGS.add(`${params.domainId}:${slug}`)
    return link
  },

  async update(tenantId: string, linkId: string, params: UpdateLinkParams): Promise<LinkListItem> {
    // TODO: const { data, error } = await apiClient.PATCH('/tenants/{tenantId}/links/{linkId}', { params: { path: { tenantId, linkId } }, body: params })
    void tenantId
    const idx = localLinks.findIndex((l) => l.id === linkId)
    if (idx === -1) throw new Error(`Link ${linkId} not found`)
    const current = localLinks[idx]
    const updated: LinkListItem = {
      ...current,
      ...(params.destinationUrl !== undefined && { destinationUrl: params.destinationUrl }),
      ...(params.redirectType !== undefined && { redirectType: params.redirectType }),
      ...(params.status !== undefined && { status: params.status }),
      expiresAt: params.clearExpiresAt ? null : (params.expiresAt ?? current.expiresAt),
      maxClicks: params.clearMaxClicks ? null : (params.maxClicks ?? current.maxClicks),
      updatedAt: new Date().toISOString(),
      version: current.version + 1,
    }
    localLinks = localLinks.map((l) => (l.id === linkId ? updated : l))
    return updated
  },

  async delete(tenantId: string, linkId: string): Promise<void> {
    // TODO: await apiClient.DELETE('/tenants/{tenantId}/links/{linkId}', { params: { path: { tenantId, linkId } } })
    void tenantId
    localLinks = localLinks.filter((l) => l.id !== linkId)
  },

  async checkSlugAvailability(
    tenantId: string,
    domainId: string,
    slug: string
  ): Promise<{ available: boolean }> {
    // TODO: const { data, error } = await apiClient.GET('/tenants/{tenantId}/links/check-slug', { params: { path: { tenantId }, query: { domainId, slug } } })
    void tenantId
    return { available: !EXISTING_SLUGS.has(`${domainId}:${slug}`) }
  },
}

function daysSince(iso: string): number {
  const created = new Date(iso)
  const now = new Date()
  return Math.max(1, Math.floor((now.getTime() - created.getTime()) / (1000 * 60 * 60 * 24)))
}

function generateTrend(
  days: number,
  total: number,
  seed: string
): { date: string; count: number }[] {
  const results: { date: string; count: number }[] = []
  let remaining = total
  const seedNum = seed.split('').reduce((acc, c) => acc + c.charCodeAt(0), 0)
  const prng = mulberry32(seedNum)
  const baseDate = new Date()
  baseDate.setDate(baseDate.getDate() - days + 1)

  for (let i = 0; i < days; i++) {
    const d = new Date(baseDate)
    d.setDate(d.getDate() + i)
    const dateStr = d.toISOString().slice(0, 10)

    if (i === days - 1) {
      results.push({ date: dateStr, count: Math.max(0, remaining) })
    } else {
      const share = remaining / (days - i)
      const jitter = share * (prng() * 0.5 + 0.25)
      const val = Math.min(remaining, Math.max(0, Math.round(jitter)))
      results.push({ date: dateStr, count: val })
      remaining -= val
    }
  }

  return results
}

function mulberry32(a: number): () => number {
  return () => {
    a |= 0
    a = (a + 0x6d2b79f5) | 0
    let t = Math.imul(a ^ (a >>> 15), 1 | a)
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}
