import { describe, it, expect, beforeEach } from 'vitest'
import { featureFlagService } from '../featureFlagService'

// Reset the module's internal store before each test by deleting flags added during tests.
// Flags seeded in the module are reset by re-importing; use a fresh set for create/delete tests.
beforeEach(async () => {
  // Remove any flags added by previous tests (keys not in the original seed).
  const { flags } = await featureFlagService.list()
  const seedKeys = new Set(['feature.links.csv_export', 'feature.analytics.advanced'])
  for (const f of flags) {
    if (!seedKeys.has(f.key)) {
      try {
        await featureFlagService.delete(f.key)
      } catch {
        // ignore
      }
    }
  }
  // Restore seeded flags to their original enabled state if they were modified.
  try {
    await featureFlagService.update('feature.links.csv_export', { enabled: true })
    await featureFlagService.update('feature.analytics.advanced', { enabled: true })
  } catch {
    // ignore
  }
})

describe('featureFlagService.list', () => {
  it('returns flags array and totalCount', async () => {
    const result = await featureFlagService.list()
    expect(Array.isArray(result.flags)).toBe(true)
    expect(typeof result.totalCount).toBe('number')
    expect(result.totalCount).toBe(result.flags.length)
  })

  it('returns flags sorted by key', async () => {
    const { flags } = await featureFlagService.list()
    const keys = flags.map((f) => f.key)
    expect(keys).toEqual([...keys].sort())
  })
})

describe('featureFlagService.get', () => {
  it('returns a known flag', async () => {
    const flag = await featureFlagService.get('feature.links.csv_export')
    expect(flag.key).toBe('feature.links.csv_export')
    expect(flag.flagType).toBe('boolean')
    expect(typeof flag.enabled).toBe('boolean')
    expect(Array.isArray(flag.auditTrail)).toBe(true)
  })

  it('throws for unknown key', async () => {
    await expect(featureFlagService.get('feature.does.not.exist')).rejects.toThrow()
  })
})

describe('featureFlagService.create', () => {
  it('creates a new boolean flag', async () => {
    const created = await featureFlagService.create({
      key: 'feature.test.new_flag',
      name: 'New Flag',
      flagType: 'boolean',
      enabled: true,
    })
    expect(created.key).toBe('feature.test.new_flag')
    expect(created.flagType).toBe('boolean')
    expect(created.enabled).toBe(true)
    expect(created.auditTrail).toHaveLength(1)
    expect(created.auditTrail[0].action).toBe('created')
  })

  it('creates a percentage flag with rollout', async () => {
    const created = await featureFlagService.create({
      key: 'feature.test.pct_flag',
      name: 'PCT Flag',
      flagType: 'percentage',
      rolloutPercentage: 30,
    })
    expect(created.rolloutPercentage).toBe(30)
  })

  it('creates a per_plan flag with plan list', async () => {
    const created = await featureFlagService.create({
      key: 'feature.test.plan_flag',
      name: 'Plan Flag',
      flagType: 'per_plan',
      enabledPlanIds: ['pro', 'team'],
    })
    expect(created.enabledPlanIds).toEqual(['pro', 'team'])
  })

  it('throws when creating duplicate key', async () => {
    await featureFlagService.create({
      key: 'feature.test.dup',
      name: 'First',
      flagType: 'boolean',
    })
    await expect(
      featureFlagService.create({ key: 'feature.test.dup', name: 'Second', flagType: 'boolean' })
    ).rejects.toThrow()
  })
})

describe('featureFlagService.update', () => {
  it('updates a flag name and appends audit entry', async () => {
    const updated = await featureFlagService.update('feature.links.csv_export', {
      name: 'CSV Export v2',
    })
    expect(updated.name).toBe('CSV Export v2')
    expect(updated.updatedAt).not.toBeNull()
    const lastEntry = updated.auditTrail[updated.auditTrail.length - 1]
    expect(lastEntry.action).toBe('updated')
  })

  it('kill switch sets enabled=false with action=disabled', async () => {
    const updated = await featureFlagService.update('feature.links.csv_export', { enabled: false })
    expect(updated.enabled).toBe(false)
    const lastEntry = updated.auditTrail[updated.auditTrail.length - 1]
    expect(lastEntry.action).toBe('disabled')
  })

  it('re-enabling sets enabled=true with action=enabled', async () => {
    await featureFlagService.update('feature.links.csv_export', { enabled: false })
    const updated = await featureFlagService.update('feature.links.csv_export', { enabled: true })
    expect(updated.enabled).toBe(true)
    const lastEntry = updated.auditTrail[updated.auditTrail.length - 1]
    expect(lastEntry.action).toBe('enabled')
  })

  it('throws when updating unknown key', async () => {
    await expect(
      featureFlagService.update('feature.does.not.exist', { name: 'Ghost' })
    ).rejects.toThrow()
  })
})

describe('featureFlagService.delete', () => {
  it('deletes an existing flag', async () => {
    await featureFlagService.create({
      key: 'feature.test.to_delete',
      name: 'To Delete',
      flagType: 'boolean',
    })
    await featureFlagService.delete('feature.test.to_delete')
    await expect(featureFlagService.get('feature.test.to_delete')).rejects.toThrow()
  })

  it('throws when deleting unknown key', async () => {
    await expect(featureFlagService.delete('feature.does.not.exist')).rejects.toThrow()
  })
})

describe('featureFlagService.evaluate', () => {
  const tenantId = 'tenant-abc-123'

  it('returns enabled=false for unknown flag with reason=not_found', async () => {
    const result = await featureFlagService.evaluate(tenantId, 'feature.unknown.flag')
    expect(result.enabled).toBe(false)
    expect(result.reason).toBe('not_found')
  })

  it('evaluates boolean flag as enabled=true', async () => {
    const result = await featureFlagService.evaluate(tenantId, 'feature.links.csv_export')
    expect(result.enabled).toBe(true)
    expect(result.reason).toBe('boolean')
  })

  it('evaluates kill-switched flag as disabled with reason=kill_switch', async () => {
    await featureFlagService.update('feature.links.csv_export', { enabled: false })
    const result = await featureFlagService.evaluate(tenantId, 'feature.links.csv_export')
    expect(result.enabled).toBe(false)
    expect(result.reason).toBe('kill_switch')
  })

  it('evaluates per_plan flag with matching plan as enabled', async () => {
    const result = await featureFlagService.evaluate(tenantId, 'feature.analytics.advanced', 'pro')
    expect(result.enabled).toBe(true)
    expect(result.reason).toBe('per_plan')
  })

  it('evaluates per_plan flag with non-matching plan as disabled', async () => {
    const result = await featureFlagService.evaluate(tenantId, 'feature.analytics.advanced', 'free')
    expect(result.enabled).toBe(false)
  })

  it('per_plan evaluation is case-insensitive', async () => {
    const result = await featureFlagService.evaluate(tenantId, 'feature.analytics.advanced', 'PRO')
    expect(result.enabled).toBe(true)
  })
})

describe('featureFlagService.evaluateAll', () => {
  it('returns evaluations for all flags', async () => {
    const result = await featureFlagService.evaluateAll('any-tenant')
    expect(Array.isArray(result.flags)).toBe(true)
    expect(result.totalCount).toBe(result.flags.length)
    expect(result.totalCount).toBeGreaterThan(0)
  })

  it('each evaluation has key, enabled (boolean), and reason (string)', async () => {
    const { flags } = await featureFlagService.evaluateAll('any-tenant')
    for (const f of flags) {
      expect(typeof f.key).toBe('string')
      expect(typeof f.enabled).toBe('boolean')
      expect(typeof f.reason).toBe('string')
    }
  })
})
