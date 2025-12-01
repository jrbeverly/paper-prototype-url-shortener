export type LinkStatus = 'active' | 'paused' | 'expired'
export type RedirectType = '301' | '302' | '307' | '308'

export interface LinkExpiryWarning {
  isNearingExpiry: boolean
  type: 'time' | 'clicks'
  message: string
  remainingClicks: number | null
  remainingDays: number | null
}

export interface LinkListItem {
  id: string
  domainId: string
  domainHostname: string
  slug: string
  destinationUrl: string
  shortUrl: string
  redirectType: RedirectType
  status: LinkStatus
  clickCount: number
  maxClicks: number | null
  expiresAt: string | null
  createdAt: string
  updatedAt: string
  version: number
  expiryWarning: LinkExpiryWarning | null
}

export interface LinkListResponse {
  items: LinkListItem[]
  nextCursor: string | null
  totalCount: number
}

export interface DailyClickCount {
  date: string
  count: number
}

export interface CountryBreakdown {
  countryCode: string
  countryName: string
  count: number
}

export interface ReferrerBreakdown {
  domain: string
  count: number
}

export interface DeviceBreakdown {
  desktop: number
  mobile: number
  tablet: number
}

export interface LinkAnalyticsSummary {
  totalClicks: number
  uniqueClicks: number
  clicksToday: number
  clickTrend: DailyClickCount[]
  topCountries: CountryBreakdown[]
  topReferrers: ReferrerBreakdown[]
  devices: DeviceBreakdown
}

export interface LinkAuditEntry {
  version: number
  action: string
  description: string
  timestamp: string
}

export interface LinkDetail extends LinkListItem {
  analytics: LinkAnalyticsSummary
  auditTrail: LinkAuditEntry[]
}

export interface CreateLinkParams {
  domainId: string
  destinationUrl: string
  slug?: string
  redirectType?: RedirectType
  expiresAt?: string
  maxClicks?: number
}

export interface UpdateLinkParams {
  destinationUrl?: string
  redirectType?: RedirectType
  expiresAt?: string
  clearExpiresAt?: boolean
  maxClicks?: number
  clearMaxClicks?: boolean
  status?: LinkStatus
}

export interface LinkListQuery {
  search?: string
  domainId?: string
  status?: LinkStatus
  cursor?: string
  pageSize?: number
}
