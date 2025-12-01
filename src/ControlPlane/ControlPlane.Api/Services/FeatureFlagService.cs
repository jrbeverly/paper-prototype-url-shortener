using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ControlPlane.Api.Services;

public sealed class FeatureFlagOptions
{
    public const string SectionName = "FeatureFlags";

    /// <summary>How long a flag is held in the in-process cache before being re-read from the repository.</summary>
    public int CacheTtlSeconds { get; init; } = 60;
}

public sealed class FeatureFlagService : IFeatureFlagService
{
    private readonly IFeatureFlagRepository _repository;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _cacheTtl;

    public FeatureFlagService(
        IFeatureFlagRepository repository,
        IMemoryCache cache,
        IOptions<FeatureFlagOptions> options)
    {
        _repository = repository;
        _cache = cache;
        _cacheTtl = TimeSpan.FromSeconds(options.Value.CacheTtlSeconds);
    }

    public async Task<bool> IsEnabledAsync(
        string key,
        Guid? tenantId = null,
        string? planId = null,
        CancellationToken ct = default)
    {
        var flag = await GetCachedFlagAsync(key, ct);
        if (flag is null) return false;
        return Evaluate(flag, tenantId, planId);
    }

    public async Task<IReadOnlyList<FeatureFlagEvaluation>> EvaluateAllAsync(
        Guid tenantId,
        string? planId,
        CancellationToken ct = default)
    {
        var flags = await _repository.ListAsync(ct);
        var results = new FeatureFlagEvaluation[flags.Count];
        for (var i = 0; i < flags.Count; i++)
        {
            var flag = flags[i];
            var enabled = Evaluate(flag, tenantId, planId);
            results[i] = new FeatureFlagEvaluation
            {
                Key = flag.Key,
                Enabled = enabled,
                Reason = EvaluationReason(flag, enabled)
            };
        }
        return results;
    }

    public void InvalidateCache(string key) => _cache.Remove(CacheKey(key));

    private async Task<FeatureFlagEntity?> GetCachedFlagAsync(string key, CancellationToken ct)
    {
        var cacheKey = CacheKey(key);
        if (_cache.TryGetValue(cacheKey, out FeatureFlagEntity? cached))
            return cached;

        var flag = await _repository.GetByKeyAsync(key, ct);
        if (flag is not null)
            _cache.Set(cacheKey, flag, _cacheTtl);
        return flag;
    }

    private static bool Evaluate(FeatureFlagEntity flag, Guid? tenantId, string? planId)
    {
        if (!flag.Enabled) return false; // Kill switch overrides everything

        return flag.FlagType switch
        {
            FlagTypes.Boolean => true,
            FlagTypes.Percentage => EvaluatePercentage(flag.Key, tenantId, flag.RolloutPercentage ?? 0),
            FlagTypes.PerTenant => tenantId.HasValue && (flag.EnabledTenantIds?.Contains(tenantId.Value) ?? false),
            FlagTypes.PerPlan => planId is not null &&
                (flag.EnabledPlanIds?.Contains(planId, StringComparer.OrdinalIgnoreCase) ?? false),
            _ => false
        };
    }

    /// <summary>
    /// Deterministic percentage rollout. Uses SHA-256 of "&lt;tenantId&gt;:&lt;flagKey&gt;" to produce
    /// a stable bucket (0–99), so the same tenant always gets the same result for a given flag.
    /// </summary>
    private static bool EvaluatePercentage(string flagKey, Guid? tenantId, int percentage)
    {
        if (!tenantId.HasValue) return false;
        if (percentage <= 0) return false;
        if (percentage >= 100) return true;

        var input = $"{tenantId:N}:{flagKey}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        // Use first 4 bytes as a uint and bucket into 0–99.
        var bucket = (int)(BitConverter.ToUInt32(hash, 0) % 100);
        return bucket < percentage;
    }

    private static string EvaluationReason(FeatureFlagEntity flag, bool enabled)
    {
        if (!flag.Enabled) return "kill_switch";
        if (!enabled) return flag.FlagType; // disabled by type logic
        return flag.FlagType;
    }

    private static string CacheKey(string flagKey) => $"ff:{flagKey}";
}
