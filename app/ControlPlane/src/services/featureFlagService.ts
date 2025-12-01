import type {
  CreateFeatureFlagParams,
  FeatureFlag,
  FeatureFlagEvaluation,
  FeatureFlagEvaluationsResponse,
  FeatureFlagListResponse,
  UpdateFeatureFlagParams,
} from '@/types/featureFlags'

// TODO: replace with apiClient.GET / POST / PATCH / DELETE once OpenAPI spec is wired up.

const MOCK_FLAGS: FeatureFlag[] = [
  {
    key: 'feature.links.csv_export',
    name: 'CSV Export',
    description: 'Enables CSV export for the links table.',
    flagType: 'boolean',
    enabled: true,
    rolloutPercentage: null,
    enabledTenantIds: null,
    enabledPlanIds: null,
    createdBy: 'system',
    updatedBy: null,
    createdAt: new Date(Date.now() - 7 * 86_400_000).toISOString(),
    updatedAt: null,
    auditTrail: [
      {
        action: 'created',
        performedBy: 'system',
        details: "Flag created with type 'boolean', enabled=true.",
        timestamp: new Date(Date.now() - 7 * 86_400_000).toISOString(),
      },
    ],
  },
  {
    key: 'feature.analytics.advanced',
    name: 'Advanced Analytics',
    description: 'Enables the advanced analytics dashboard with cohort analysis.',
    flagType: 'per_plan',
    enabled: true,
    rolloutPercentage: null,
    enabledTenantIds: null,
    enabledPlanIds: ['pro', 'team', 'business', 'enterprise'],
    createdBy: 'system',
    updatedBy: null,
    createdAt: new Date(Date.now() - 3 * 86_400_000).toISOString(),
    updatedAt: null,
    auditTrail: [
      {
        action: 'created',
        performedBy: 'system',
        details: "Flag created with type 'per_plan', enabled=true.",
        timestamp: new Date(Date.now() - 3 * 86_400_000).toISOString(),
      },
    ],
  },
]

/** In-memory store for mock mutations. */
const _store: Map<string, FeatureFlag> = new Map(MOCK_FLAGS.map((f) => [f.key, f]))

export const featureFlagService = {
  /** List all feature flags (admin). */
  async list(): Promise<FeatureFlagListResponse> {
    const flags = Array.from(_store.values()).sort((a, b) => a.key.localeCompare(b.key))
    return { flags, totalCount: flags.length }
  },

  /** Get a single feature flag by key (admin). */
  async get(key: string): Promise<FeatureFlag> {
    const flag = _store.get(key)
    if (!flag) throw new Error(`Feature flag '${key}' not found.`)
    return { ...flag }
  },

  /** Create a new feature flag (admin). */
  async create(params: CreateFeatureFlagParams): Promise<FeatureFlag> {
    if (_store.has(params.key))
      throw new Error(`A feature flag with key '${params.key}' already exists.`)
    const now = new Date().toISOString()
    const flag: FeatureFlag = {
      key: params.key,
      name: params.name,
      description: params.description ?? null,
      flagType: params.flagType,
      enabled: params.enabled ?? true,
      rolloutPercentage: params.rolloutPercentage ?? null,
      enabledTenantIds: params.enabledTenantIds ?? null,
      enabledPlanIds: params.enabledPlanIds ?? null,
      createdBy: 'current-user',
      updatedBy: null,
      createdAt: now,
      updatedAt: null,
      auditTrail: [
        {
          action: 'created',
          performedBy: 'current-user',
          details: `Flag created with type '${params.flagType}', enabled=${params.enabled ?? true}.`,
          timestamp: now,
        },
      ],
    }
    _store.set(flag.key, flag)
    return { ...flag }
  },

  /** Partially update an existing feature flag (admin). Set enabled=false for a kill switch. */
  async update(key: string, params: UpdateFeatureFlagParams): Promise<FeatureFlag> {
    const existing = _store.get(key)
    if (!existing) throw new Error(`Feature flag '${key}' not found.`)
    const now = new Date().toISOString()
    const action =
      params.enabled === false ? 'disabled' : params.enabled === true ? 'enabled' : 'updated'
    const updated: FeatureFlag = {
      ...existing,
      name: params.name ?? existing.name,
      description:
        params.description !== undefined ? (params.description ?? null) : existing.description,
      enabled: params.enabled ?? existing.enabled,
      flagType: params.flagType ?? existing.flagType,
      rolloutPercentage: params.rolloutPercentage ?? existing.rolloutPercentage,
      enabledTenantIds: params.enabledTenantIds ?? existing.enabledTenantIds,
      enabledPlanIds: params.enabledPlanIds ?? existing.enabledPlanIds,
      updatedBy: 'current-user',
      updatedAt: now,
      auditTrail: [
        ...existing.auditTrail,
        {
          action,
          performedBy: 'current-user',
          details: null,
          timestamp: now,
        },
      ],
    }
    _store.set(key, updated)
    return { ...updated }
  },

  /** Permanently delete a feature flag (admin). */
  async delete(key: string): Promise<void> {
    if (!_store.has(key)) throw new Error(`Feature flag '${key}' not found.`)
    _store.delete(key)
  },

  /** Evaluate all flags for a tenant. */
  async evaluateAll(tenantId: string, planId?: string): Promise<FeatureFlagEvaluationsResponse> {
    const flags = Array.from(_store.values())
    const evaluations: FeatureFlagEvaluation[] = flags.map((flag) =>
      evaluateFlag(flag, tenantId, planId)
    )
    return { flags: evaluations, totalCount: evaluations.length }
  },

  /** Evaluate a single flag for a tenant. */
  async evaluate(tenantId: string, key: string, planId?: string): Promise<FeatureFlagEvaluation> {
    const flag = _store.get(key)
    if (!flag) return { key, enabled: false, reason: 'not_found' }
    return evaluateFlag(flag, tenantId, planId)
  },
}

function evaluateFlag(flag: FeatureFlag, tenantId: string, planId?: string): FeatureFlagEvaluation {
  if (!flag.enabled) return { key: flag.key, enabled: false, reason: 'kill_switch' }

  switch (flag.flagType) {
    case 'boolean':
      return { key: flag.key, enabled: true, reason: 'boolean' }

    case 'percentage': {
      const bucket = deterministicBucket(tenantId, flag.key)
      const enabled = bucket < (flag.rolloutPercentage ?? 0)
      return { key: flag.key, enabled, reason: 'percentage' }
    }

    case 'per_tenant': {
      const enabled = flag.enabledTenantIds?.includes(tenantId) ?? false
      return { key: flag.key, enabled, reason: 'per_tenant' }
    }

    case 'per_plan': {
      const enabled =
        planId !== undefined &&
        (flag.enabledPlanIds?.some((p) => p.toLowerCase() === planId.toLowerCase()) ?? false)
      return { key: flag.key, enabled, reason: 'per_plan' }
    }

    default:
      return { key: flag.key, enabled: false, reason: 'per_plan' }
  }
}

/** Stable bucket (0–99) for a tenantId + flagKey pair. Matches server-side SHA-256 bucketing semantics. */
function deterministicBucket(tenantId: string, flagKey: string): number {
  const str = `${tenantId}:${flagKey}`
  let hash = 0
  for (let i = 0; i < str.length; i++) {
    hash = (Math.imul(31, hash) + str.charCodeAt(i)) | 0
  }
  return Math.abs(hash) % 100
}
