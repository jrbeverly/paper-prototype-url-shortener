export type DomainStatus = 'pending_verification' | 'verifying' | 'verification_failed' | 'active'
export type CertificateStatus = 'pending' | 'issued' | 'failed'
export type NotFoundBehavior = '404' | 'redirect' | 'passthrough'

export interface DnsVerificationInstructions {
  txtName: string
  txtValue: string
  cnameName: string
  cnameValue: string
}

export interface Domain {
  id: string
  hostname: string
  status: DomainStatus
  certificateStatus: CertificateStatus
  linkCount: number
  createdAt: string
}

export interface DomainSettings {
  defaultRedirectUrl: string | null
  errorPageBranding: string | null
  notFoundBehavior: NotFoundBehavior
}

export interface DomainDetail extends Domain {
  tenantId: string
  settings: DomainSettings
  verificationInstructions?: DnsVerificationInstructions
  updatedAt: string | null
  deletedAt: string | null
}

export interface DomainList {
  items: Domain[]
  page: number
  pageSize: number
  totalCount: number
}

export interface DomainCreated {
  id: string
  hostname: string
  status: DomainStatus
  verificationInstructions: DnsVerificationInstructions
  createdAt: string
}

export interface DnsCheckDetail {
  passed: boolean
  expected: string
  actual: string | null
  error: string | null
}

export interface DomainVerifyResult {
  id: string
  hostname: string
  status: DomainStatus
  txtCheck: DnsCheckDetail
  cnameCheck: DnsCheckDetail
  message: string | null
  checkedAt: string
}

export interface AddDomainParams {
  hostname: string
}

export interface UpdateDomainParams {
  defaultRedirectUrl?: string | null
  errorPageBranding?: string | null
  notFoundBehavior?: NotFoundBehavior
}
