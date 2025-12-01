using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;

namespace ControlPlane.Tests.Endpoints;

[Collection("DynamoDB")]
public sealed class ConcurrencyTests : IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly Guid _tenantId = Guid.NewGuid();

    public ConcurrencyTests(LocalStackFixture localStack)
    {
        _factory = new CustomWebApplicationFactory(localStack.DynamoDb, localStack.TableName);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentCreateRequests_AllSucceed()
    {
        var claimsProvider = _factory.Services.GetRequiredService<TestClaimsProvider>();
        var client = _factory.CreateClient();
        var adminPrincipal = TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin);

        var tasks = Enumerable.Range(0, 5).Select(async i =>
        {
            claimsProvider.SetClaims(adminPrincipal);
            var request = new CreateApiKeyRequest
            {
                Name = $"Concurrent Key {i}",
                Permissions = [Roles.Viewer]
            };
            return await client.PostAsJsonAsync(
                $"/api/v1/tenants/{_tenantId}/api-keys", request);
        });

        var responses = await Task.WhenAll(tasks);

        responses.Should().AllSatisfy(r =>
            r.StatusCode.Should().Be(HttpStatusCode.Created));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentRevokeSameKey_FirstSucceedsRestFail()
    {
        var claimsProvider = _factory.Services.GetRequiredService<TestClaimsProvider>();
        var client = _factory.CreateClient();
        var adminPrincipal = TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin);

        claimsProvider.SetClaims(adminPrincipal);
        var createResponse = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/api-keys",
            new CreateApiKeyRequest { Name = "Concurrent Revoke", Permissions = [Roles.Viewer] });
        var created = await createResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>();

        var revokeTasks = Enumerable.Range(0, 3).Select(_ =>
        {
            claimsProvider.SetClaims(adminPrincipal);
            return client.DeleteAsync(
                $"/api/v1/tenants/{_tenantId}/api-keys/{created!.Id}");
        });

        var revokeResponses = await Task.WhenAll(revokeTasks);

        revokeResponses.Count(r => r.StatusCode == HttpStatusCode.NoContent).Should().Be(1);
        revokeResponses.Count(r => r.StatusCode == HttpStatusCode.BadRequest).Should().Be(2);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentCreateAndList_ListReturnsConsistentResults()
    {
        var claimsProvider = _factory.Services.GetRequiredService<TestClaimsProvider>();
        var client = _factory.CreateClient();
        var adminPrincipal = TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin);

        var createTask = Task.Run(async () =>
        {
            for (int i = 0; i < 3; i++)
            {
                claimsProvider.SetClaims(adminPrincipal);
                await client.PostAsJsonAsync(
                    $"/api/v1/tenants/{_tenantId}/api-keys",
                    new CreateApiKeyRequest { Name = $"Concurrent List {i}", Permissions = [Roles.Viewer] });
                await Task.Delay(50);
            }
        });

        var listResults = new List<HttpResponseMessage>();
        var listTask = Task.Run(async () =>
        {
            for (int i = 0; i < 3; i++)
            {
                claimsProvider.SetClaims(adminPrincipal);
                var response = await client.GetAsync(
                    $"/api/v1/tenants/{_tenantId}/api-keys");
                listResults.Add(response);
                await Task.Delay(50);
            }
        });

        await Task.WhenAll(createTask, listTask);

        listResults.Should().AllSatisfy(r =>
            r.StatusCode.Should().Be(HttpStatusCode.OK));
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
