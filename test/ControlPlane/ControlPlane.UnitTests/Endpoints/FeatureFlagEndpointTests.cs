using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;
using ControlPlane.Api.Services;

namespace ControlPlane.UnitTests.Endpoints;

public sealed class FeatureFlagEndpointTests : IAsyncDisposable
{
    private readonly UnitTestWebApplicationFactory _factory = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    private const string _adminUrl = "/api/v1/feature-flags";
    private string TenantFlagsUrl => $"/api/v1/tenants/{_tenantId}/feature-flags";

    private (HttpClient Client, TestClaimsProvider Claims) AsAdmin() =>
        _factory.CreateAuthenticatedClient(_tenantId.ToString(), Roles.Admin);

    private (HttpClient Client, TestClaimsProvider Claims) AsViewer() =>
        _factory.CreateAuthenticatedClient(_tenantId.ToString(), Roles.Viewer);

    private (HttpClient Client, TestClaimsProvider Claims) AsMember() =>
        _factory.CreateAuthenticatedClient(_tenantId.ToString(), Roles.Member);

    private async Task<FeatureFlagResponse> CreateFlagAsync(
        HttpClient client,
        string key = "feature.test.flag",
        string flagType = "boolean",
        bool enabled = true)
    {
        var request = new CreateFeatureFlagRequest
        {
            Key = key,
            Name = "Test Flag",
            Description = "A test feature flag.",
            FlagType = flagType,
            Enabled = enabled,
            RolloutPercentage = flagType == "percentage" ? 50 : null
        };
        var response = await client.PostAsJsonAsync(_adminUrl, request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<FeatureFlagResponse>())!;
    }

    // ── Create ─────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateFlag_ValidBoolean_Returns201WithBody()
    {
        var (client, _) = AsAdmin();
        var request = new CreateFeatureFlagRequest
        {
            Key = "feature.links.csv_export",
            Name = "CSV Export",
            Description = "Enable CSV export for links.",
            FlagType = "boolean",
            Enabled = true
        };

        var response = await client.PostAsJsonAsync(_adminUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<FeatureFlagResponse>();
        body.Should().NotBeNull();
        body!.Key.Should().Be("feature.links.csv_export");
        body.FlagType.Should().Be("boolean");
        body.Enabled.Should().BeTrue();
        body.CreatedBy.Should().NotBeNullOrEmpty();
        body.AuditTrail.Should().ContainSingle(e => e.Action == "created");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateFlag_Percentage_Returns201()
    {
        var (client, _) = AsAdmin();
        var request = new CreateFeatureFlagRequest
        {
            Key = "feature.links.bulk_delete",
            Name = "Bulk Delete",
            FlagType = "percentage",
            Enabled = true,
            RolloutPercentage = 25
        };

        var response = await client.PostAsJsonAsync(_adminUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<FeatureFlagResponse>();
        body!.RolloutPercentage.Should().Be(25);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateFlag_DuplicateKey_Returns409()
    {
        var (client, _) = AsAdmin();
        await CreateFlagAsync(client, "feature.test.dup");

        var duplicate = new CreateFeatureFlagRequest
        {
            Key = "feature.test.dup",
            Name = "Duplicate",
            FlagType = "boolean",
            Enabled = true
        };
        var response = await client.PostAsJsonAsync(_adminUrl, duplicate);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateFlag_InvalidKeyFormat_Returns400()
    {
        var (client, _) = AsAdmin();
        var request = new CreateFeatureFlagRequest
        {
            Key = "INVALID KEY!",
            Name = "Bad Key",
            FlagType = "boolean",
            Enabled = true
        };

        var response = await client.PostAsJsonAsync(_adminUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateFlag_PercentageMissingRollout_Returns400()
    {
        var (client, _) = AsAdmin();
        var request = new CreateFeatureFlagRequest
        {
            Key = "feature.test.no_rollout",
            Name = "No Rollout",
            FlagType = "percentage",
            Enabled = true
            // RolloutPercentage intentionally omitted
        };

        var response = await client.PostAsJsonAsync(_adminUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateFlag_RequiresFlagWritePermission_MemberReturns403()
    {
        var (client, _) = AsMember();
        var request = new CreateFeatureFlagRequest
        {
            Key = "feature.test.forbidden",
            Name = "Forbidden",
            FlagType = "boolean",
            Enabled = true
        };

        var response = await client.PostAsJsonAsync(_adminUrl, request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Get ─────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetFlag_ExistingKey_Returns200()
    {
        var (client, _) = AsAdmin();
        await CreateFlagAsync(client, "feature.test.get");

        var response = await client.GetAsync($"{_adminUrl}/feature.test.get");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<FeatureFlagResponse>();
        body!.Key.Should().Be("feature.test.get");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetFlag_NonExistentKey_Returns404()
    {
        var (client, _) = AsAdmin();

        var response = await client.GetAsync($"{_adminUrl}/feature.does.not.exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetFlag_ViewerCanRead()
    {
        var (adminClient, _) = AsAdmin();
        await CreateFlagAsync(adminClient, "feature.test.viewer_read");

        var (viewerClient, _) = AsViewer();
        var response = await viewerClient.GetAsync($"{_adminUrl}/feature.test.viewer_read");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── List ────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListFlags_NoFlags_ReturnsEmptyList()
    {
        var (client, _) = AsAdmin();

        var response = await client.GetAsync(_adminUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<FeatureFlagListResponse>();
        body!.TotalCount.Should().Be(0);
        body.Flags.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListFlags_MultipleFlags_ReturnsAll()
    {
        var (client, _) = AsAdmin();
        await CreateFlagAsync(client, "feature.test.a");
        await CreateFlagAsync(client, "feature.test.b");

        var response = await client.GetAsync(_adminUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<FeatureFlagListResponse>();
        body!.TotalCount.Should().Be(2);
    }

    // ── Update ───────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateFlag_ChangeName_Returns200WithUpdatedName()
    {
        var (client, _) = AsAdmin();
        await CreateFlagAsync(client, "feature.test.update_name");

        var update = new UpdateFeatureFlagRequest { Name = "New Name" };
        var response = await client.PatchAsJsonAsync($"{_adminUrl}/feature.test.update_name", update);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<FeatureFlagResponse>();
        body!.Name.Should().Be("New Name");
        body.UpdatedAt.Should().NotBeNull();
        body.AuditTrail.Should().HaveCount(2); // created + updated
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateFlag_KillSwitch_DisablesFlag()
    {
        var (client, _) = AsAdmin();
        await CreateFlagAsync(client, "feature.test.kill_switch", enabled: true);

        var update = new UpdateFeatureFlagRequest { Enabled = false };
        var response = await client.PatchAsJsonAsync($"{_adminUrl}/feature.test.kill_switch", update);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<FeatureFlagResponse>();
        body!.Enabled.Should().BeFalse();
        body.AuditTrail.Should().Contain(e => e.Action == "disabled");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateFlag_EnableKillSwitch_ReenablesFlag()
    {
        var (client, _) = AsAdmin();
        await CreateFlagAsync(client, "feature.test.reenable", enabled: false);

        var update = new UpdateFeatureFlagRequest { Enabled = true };
        var response = await client.PatchAsJsonAsync($"{_adminUrl}/feature.test.reenable", update);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<FeatureFlagResponse>();
        body!.Enabled.Should().BeTrue();
        body.AuditTrail.Should().Contain(e => e.Action == "enabled");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task UpdateFlag_NonExistentKey_Returns404()
    {
        var (client, _) = AsAdmin();

        var update = new UpdateFeatureFlagRequest { Name = "Ghost" };
        var response = await client.PatchAsJsonAsync($"{_adminUrl}/feature.does.not.exist", update);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Delete ───────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeleteFlag_ExistingKey_Returns204()
    {
        var (client, _) = AsAdmin();
        await CreateFlagAsync(client, "feature.test.delete");

        var response = await client.DeleteAsync($"{_adminUrl}/feature.test.delete");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeleteFlag_ThenGet_Returns404()
    {
        var (client, _) = AsAdmin();
        await CreateFlagAsync(client, "feature.test.delete_then_get");

        await client.DeleteAsync($"{_adminUrl}/feature.test.delete_then_get");
        var getResponse = await client.GetAsync($"{_adminUrl}/feature.test.delete_then_get");

        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DeleteFlag_NonExistentKey_Returns404()
    {
        var (client, _) = AsAdmin();

        var response = await client.DeleteAsync($"{_adminUrl}/feature.does.not.exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Tenant evaluation ─────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EvaluateAll_NoFlags_ReturnsEmptyList()
    {
        await _factory.SeedTenantAsync(_tenantId);
        var (client, _) = AsAdmin();

        var response = await client.GetAsync(TenantFlagsUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<FeatureFlagEvaluationsResponse>();
        body!.TotalCount.Should().Be(0);
        body.Flags.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EvaluateAll_BooleanFlag_ReturnsEnabledTrue()
    {
        await _factory.SeedTenantAsync(_tenantId);
        var (client, _) = AsAdmin();
        await CreateFlagAsync(client, "feature.test.eval_all");

        var response = await client.GetAsync(TenantFlagsUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<FeatureFlagEvaluationsResponse>();
        body!.Flags.Should().ContainSingle(e => e.Key == "feature.test.eval_all" && e.Enabled);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EvaluateAll_KillSwitchedFlag_ReturnedDisabledWithKillSwitchReason()
    {
        await _factory.SeedTenantAsync(_tenantId);
        var (client, _) = AsAdmin();
        await CreateFlagAsync(client, "feature.test.eval_kill_switch", enabled: false);

        var response = await client.GetAsync(TenantFlagsUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<FeatureFlagEvaluationsResponse>();
        var flagEval = body!.Flags.Should().ContainSingle(e => e.Key == "feature.test.eval_kill_switch").Subject;
        flagEval.Enabled.Should().BeFalse();
        flagEval.Reason.Should().Be("kill_switch");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EvaluateOne_ExistingFlag_Returns200()
    {
        await _factory.SeedTenantAsync(_tenantId);
        var (client, _) = AsAdmin();
        await CreateFlagAsync(client, "feature.test.eval_one");

        var response = await client.GetAsync($"{TenantFlagsUrl}/feature.test.eval_one");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<FeatureFlagEvaluationResponse>();
        body!.Key.Should().Be("feature.test.eval_one");
        body.Enabled.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EvaluateOne_UnknownFlag_Returns200Disabled()
    {
        await _factory.SeedTenantAsync(_tenantId);
        var (client, _) = AsAdmin();

        var response = await client.GetAsync($"{TenantFlagsUrl}/feature.unknown.flag");

        // Unknown flags evaluate to false, not 404 (fail-safe)
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<FeatureFlagEvaluationResponse>();
        body!.Enabled.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EvaluateAll_RequiresFlagReadPermission_UnauthenticatedReturns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(TenantFlagsUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}
