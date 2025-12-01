using ControlPlane.Api.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ControlPlane.UnitTests.Services;

public sealed class FeatureFlagServiceTests
{
    private readonly InMemoryFeatureFlagRepository _repository = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    private IFeatureFlagService CreateService(int cacheTtlSeconds = 60) =>
        new FeatureFlagService(
            _repository,
            _cache,
            Options.Create(new FeatureFlagOptions { CacheTtlSeconds = cacheTtlSeconds }));

    private async Task<FeatureFlagEntity> SeedAsync(
        string key = "feature.test.flag",
        string flagType = FlagTypes.Boolean,
        bool enabled = true,
        int? rolloutPercentage = null,
        IReadOnlyList<Guid>? enabledTenantIds = null,
        IReadOnlyList<string>? enabledPlanIds = null)
    {
        var entity = new FeatureFlagEntity
        {
            Key = key,
            Name = "Test Flag",
            FlagType = flagType,
            Enabled = enabled,
            RolloutPercentage = rolloutPercentage,
            EnabledTenantIds = enabledTenantIds,
            EnabledPlanIds = enabledPlanIds,
            CreatedBy = "test",
            CreatedAt = DateTime.UtcNow
        };
        return await _repository.CreateAsync(entity);
    }

    // ── Unknown flag ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task IsEnabled_UnknownFlag_ReturnsFalse()
    {
        var svc = CreateService();
        var result = await svc.IsEnabledAsync("feature.does.not.exist");
        result.Should().BeFalse();
    }

    // ── Boolean flag ───────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task IsEnabled_BooleanFlag_Enabled_ReturnsTrue()
    {
        await SeedAsync(flagType: FlagTypes.Boolean, enabled: true);
        var svc = CreateService();

        var result = await svc.IsEnabledAsync("feature.test.flag");

        result.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task IsEnabled_BooleanFlag_KillSwitch_ReturnsFalse()
    {
        await SeedAsync(flagType: FlagTypes.Boolean, enabled: false);
        var svc = CreateService();

        var result = await svc.IsEnabledAsync("feature.test.flag");

        result.Should().BeFalse();
    }

    // ── Percentage flag ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task IsEnabled_PercentageFlag_Zero_AlwaysFalse()
    {
        await SeedAsync(flagType: FlagTypes.Percentage, rolloutPercentage: 0);
        var svc = CreateService();

        // Try many tenants — should all be false
        foreach (var _ in Enumerable.Range(0, 20))
        {
            var result = await svc.IsEnabledAsync("feature.test.flag", tenantId: Guid.NewGuid());
            result.Should().BeFalse();
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task IsEnabled_PercentageFlag_OneHundred_AlwaysTrue()
    {
        await SeedAsync(flagType: FlagTypes.Percentage, rolloutPercentage: 100);
        var svc = CreateService();

        foreach (var _ in Enumerable.Range(0, 20))
        {
            var result = await svc.IsEnabledAsync("feature.test.flag", tenantId: Guid.NewGuid());
            result.Should().BeTrue();
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task IsEnabled_PercentageFlag_IsDeterministic()
    {
        await SeedAsync(flagType: FlagTypes.Percentage, rolloutPercentage: 50);
        var svc = CreateService();
        var tenantId = Guid.NewGuid();

        var first = await svc.IsEnabledAsync("feature.test.flag", tenantId: tenantId);
        var second = await svc.IsEnabledAsync("feature.test.flag", tenantId: tenantId);

        first.Should().Be(second);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task IsEnabled_PercentageFlag_NoTenantId_ReturnsFalse()
    {
        await SeedAsync(flagType: FlagTypes.Percentage, rolloutPercentage: 100);
        var svc = CreateService();

        var result = await svc.IsEnabledAsync("feature.test.flag", tenantId: null);

        result.Should().BeFalse();
    }

    // ── Per-tenant flag ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task IsEnabled_PerTenantFlag_TenantInList_ReturnsTrue()
    {
        var tenantId = Guid.NewGuid();
        await SeedAsync(flagType: FlagTypes.PerTenant, enabledTenantIds: [tenantId]);
        var svc = CreateService();

        var result = await svc.IsEnabledAsync("feature.test.flag", tenantId: tenantId);

        result.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task IsEnabled_PerTenantFlag_TenantNotInList_ReturnsFalse()
    {
        var tenantId = Guid.NewGuid();
        await SeedAsync(flagType: FlagTypes.PerTenant, enabledTenantIds: [Guid.NewGuid()]);
        var svc = CreateService();

        var result = await svc.IsEnabledAsync("feature.test.flag", tenantId: tenantId);

        result.Should().BeFalse();
    }

    // ── Per-plan flag ──────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task IsEnabled_PerPlanFlag_MatchingPlan_ReturnsTrue()
    {
        await SeedAsync(flagType: FlagTypes.PerPlan, enabledPlanIds: ["pro", "team"]);
        var svc = CreateService();

        var result = await svc.IsEnabledAsync("feature.test.flag", tenantId: Guid.NewGuid(), planId: "pro");

        result.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task IsEnabled_PerPlanFlag_CaseInsensitiveMatch()
    {
        await SeedAsync(flagType: FlagTypes.PerPlan, enabledPlanIds: ["Pro"]);
        var svc = CreateService();

        var result = await svc.IsEnabledAsync("feature.test.flag", tenantId: Guid.NewGuid(), planId: "pro");

        result.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task IsEnabled_PerPlanFlag_NonMatchingPlan_ReturnsFalse()
    {
        await SeedAsync(flagType: FlagTypes.PerPlan, enabledPlanIds: ["pro", "team"]);
        var svc = CreateService();

        var result = await svc.IsEnabledAsync("feature.test.flag", tenantId: Guid.NewGuid(), planId: "free");

        result.Should().BeFalse();
    }

    // ── Kill switch ────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task IsEnabled_KillSwitch_OverridesPercentageFlag()
    {
        await SeedAsync(flagType: FlagTypes.Percentage, rolloutPercentage: 100, enabled: false);
        var svc = CreateService();

        var result = await svc.IsEnabledAsync("feature.test.flag", tenantId: Guid.NewGuid());

        result.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task IsEnabled_KillSwitch_OverridesPerTenantFlag()
    {
        var tenantId = Guid.NewGuid();
        await SeedAsync(flagType: FlagTypes.PerTenant, enabledTenantIds: [tenantId], enabled: false);
        var svc = CreateService();

        var result = await svc.IsEnabledAsync("feature.test.flag", tenantId: tenantId);

        result.Should().BeFalse();
    }

    // ── Caching ────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task IsEnabled_CachesResult_SubsequentCallDoesNotHitRepository()
    {
        await SeedAsync(flagType: FlagTypes.Boolean, enabled: true);
        var svc = CreateService();

        await svc.IsEnabledAsync("feature.test.flag"); // populates cache

        // Mutate the repository directly (bypassing service) to simulate a change
        await _repository.UpdateAsync(
            "feature.test.flag",
            new FeatureFlagUpdate { Enabled = false },
            new FlagAuditEntry { Action = "disabled", PerformedBy = "test", Timestamp = DateTime.UtcNow });

        // Cache is still warm → should return old (true) value
        var result = await svc.IsEnabledAsync("feature.test.flag");
        result.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task InvalidateCache_ForcesRepositoryReread()
    {
        await SeedAsync(flagType: FlagTypes.Boolean, enabled: true);
        var svc = CreateService();

        await svc.IsEnabledAsync("feature.test.flag"); // populates cache

        await _repository.UpdateAsync(
            "feature.test.flag",
            new FeatureFlagUpdate { Enabled = false },
            new FlagAuditEntry { Action = "disabled", PerformedBy = "test", Timestamp = DateTime.UtcNow });

        svc.InvalidateCache("feature.test.flag");

        var result = await svc.IsEnabledAsync("feature.test.flag");
        result.Should().BeFalse();
    }

    // ── EvaluateAll ────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EvaluateAll_ReturnsEvaluationForEveryFlag()
    {
        var tenantId = Guid.NewGuid();
        await SeedAsync("feature.area.one", FlagTypes.Boolean, enabled: true);
        await _repository.CreateAsync(new FeatureFlagEntity
        {
            Key = "feature.area.two",
            Name = "Two",
            FlagType = FlagTypes.Boolean,
            Enabled = false,
            CreatedBy = "test",
            CreatedAt = DateTime.UtcNow
        });

        var svc = CreateService();
        var results = await svc.EvaluateAllAsync(tenantId, planId: null);

        results.Should().HaveCount(2);
        results.Should().Contain(e => e.Key == "feature.area.one" && e.Enabled);
        results.Should().Contain(e => e.Key == "feature.area.two" && !e.Enabled);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EvaluateAll_KillSwitchedFlag_HasKillSwitchReason()
    {
        var tenantId = Guid.NewGuid();
        await SeedAsync(enabled: false);
        var svc = CreateService();

        var results = await svc.EvaluateAllAsync(tenantId, planId: null);

        results.Should().ContainSingle(e => e.Reason == "kill_switch");
    }
}
