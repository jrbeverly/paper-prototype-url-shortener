export type FlagType = 'boolean' | 'percentage' | 'per_tenant' | 'per_plan'

export interface FlagAuditEntry {
  action: string
  performedBy: string
  details: string | null
  timestamp: string
}

export interface FeatureFlag {
  key: string
  name: string
  description: string | null
  flagType: FlagType
  /** Global kill switch. When false the flag always evaluates to disabled. */
  enabled: boolean
  rolloutPercentage: number | null
  enabledTenantIds: string[] | null
  enabledPlanIds: string[] | null
  createdBy: string
  updatedBy: string | null
  createdAt: string
  updatedAt: string | null
  auditTrail: FlagAuditEntry[]
}

export interface FeatureFlagListResponse {
  flags: FeatureFlag[]
  totalCount: number
}

/** Evaluated state of a flag for a specific tenant context. */
export interface FeatureFlagEvaluation {
  key: string
  enabled: boolean
  /**
   * Why the flag evaluated as it did:
   * "kill_switch" | "boolean" | "percentage" | "per_tenant" | "per_plan" | "disabled" | "enabled" | "not_found"
   */
  reason: string
}

export interface FeatureFlagEvaluationsResponse {
  flags: FeatureFlagEvaluation[]
  totalCount: number
}

export interface CreateFeatureFlagParams {
  key: string
  name: string
  description?: string
  flagType: FlagType
  enabled?: boolean
  rolloutPercentage?: number
  enabledTenantIds?: string[]
  enabledPlanIds?: string[]
}

export interface UpdateFeatureFlagParams {
  name?: string
  description?: string
  /** Set to false to trigger the kill switch. */
  enabled?: boolean
  flagType?: FlagType
  rolloutPercentage?: number
  enabledTenantIds?: string[]
  enabledPlanIds?: string[]
}
