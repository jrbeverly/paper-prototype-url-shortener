using ControlPlane.Api.Authorization;
using ControlPlane.Api.Models.Requests;
using ControlPlane.Api.Models.Responses;

namespace ControlPlane.Tests.Endpoints;

[Collection("DynamoDB")]
public sealed class LinkEndpointTests : IAsyncDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly Guid _tenantId = Guid.NewGuid();

    public LinkEndpointTests(LocalStackFixture localStack)
    {
        _factory = new CustomWebApplicationFactory(localStack.DynamoDb, localStack.TableName);
    }

    private async Task<(Guid DomainId, string Hostname)> CreateVerifiedDomainAsync(
        HttpClient client, TestClaimsProvider claimsProvider)
    {
        var hostname = $"links-{Guid.NewGuid():N}.example.com";
        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = hostname });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var verify = await client.PostAsync(
            $"/api/v1/tenants/{_tenantId}/domains/{created!.Id}/verify", null);
        verify.StatusCode.Should().Be(HttpStatusCode.OK);

        return (created.Id, hostname);
    }

    // ── CreateLink ────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateLink_ValidRequest_Returns201WithShortUrl()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, hostname) = await CreateVerifiedDomainAsync(client, claimsProvider);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var request = new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://example.com/some/long/path"
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body.Should().NotBeNull();
        body!.Id.Should().NotBeEmpty();
        body.DomainId.Should().Be(domainId);
        body.DestinationUrl.Should().Be("https://example.com/some/long/path");
        body.Slug.Should().NotBeNullOrEmpty();
        body.Slug.Length.Should().BeGreaterThanOrEqualTo(6);
        body.ShortUrl.Should().Be($"https://{hostname}/{body.Slug}");
        body.RedirectType.Should().Be("302");
        body.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateLink_WithCustomSlug_Returns201()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, hostname) = await CreateVerifiedDomainAsync(client, claimsProvider);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var request = new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://example.com",
            Slug = "my-custom-slug"
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body.Should().NotBeNull();
        body!.Slug.Should().Be("my-custom-slug");
        body.ShortUrl.Should().Be($"https://{hostname}/my-custom-slug");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateLink_DuplicateSlug_Returns409()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var request = new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://example.com",
            Slug = "duplicate-slug"
        };

        var first = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links", request);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var second = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links", request);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateLink_DuplicateSlugDifferentCase_Returns409()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var first = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links",
            new CreateLinkRequest
            {
                DomainId = domainId,
                DestinationUrl = "https://example.com",
                Slug = "Case-Sensitive"
            });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var second = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links",
            new CreateLinkRequest
            {
                DomainId = domainId,
                DestinationUrl = "https://example.com",
                Slug = "case-sensitive"
            });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateLink_SameSlug_DifferentDomains_Succeeds()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId1, _) = await CreateVerifiedDomainAsync(client, claimsProvider);
        var (domainId2, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var first = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links",
            new CreateLinkRequest
            {
                DomainId = domainId1,
                DestinationUrl = "https://example.com",
                Slug = "shared-slug"
            });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var second = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links",
            new CreateLinkRequest
            {
                DomainId = domainId2,
                DestinationUrl = "https://example.com",
                Slug = "shared-slug"
            });

        second.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/path")]
    [InlineData("relative")]
    public async Task CreateLink_InvalidDestinationUrl_Returns400(string url)
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var request = new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = url
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateLink_InvalidSlug_Returns400()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var request = new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://example.com",
            Slug = "-starts-with-hyphen"
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateLink_InvalidRedirectType_Returns400()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var request = new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://example.com",
            RedirectType = "999"
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateLink_ExpiresInPast_Returns400()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var request = new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://example.com",
            ExpiresAt = DateTime.UtcNow.AddHours(-1)
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateLink_LinkLimitExceeded_Returns422()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        for (int i = 0; i < 50; i++)
        {
            claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
                _tenantId.ToString(), Roles.Member));
            var response = await client.PostAsJsonAsync(
                $"/api/v1/tenants/{_tenantId}/links",
                new CreateLinkRequest
                {
                    DomainId = domainId,
                    DestinationUrl = $"https://example.com/{i}"
                });
            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var overLimit = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links",
            new CreateLinkRequest
            {
                DomainId = domainId,
                DestinationUrl = "https://example.com/overflow"
            });

        overLimit.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateLink_WithoutLinkWritePermission_Returns403()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Viewer));
        var request = new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://example.com"
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateLink_WithoutAuth_Returns401()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        // Clear claims so the unauthenticated client gets no principal
        claimsProvider.SetClaims(null!);

        var unauthClient = _factory.CreateClient();
        var request = new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://example.com"
        };

        var response = await unauthClient.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateLink_DomainNotFound_Returns404()
    {
        var (client, _) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Member);

        var request = new CreateLinkRequest
        {
            DomainId = Guid.NewGuid(),
            DestinationUrl = "https://example.com"
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateLink_DomainNotActive_Returns400()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var hostname = $"pending-{Guid.NewGuid():N}.example.com";

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Admin));
        var create = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/domains",
            new CreateDomainRequest { Hostname = hostname });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<CreateDomainResponse>();

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var request = new CreateLinkRequest
        {
            DomainId = created!.Id,
            DestinationUrl = "https://example.com"
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateLink_ResponseContainsLocationHeader()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var request = new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://example.com"
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.OriginalString.Should()
            .Contain($"/tenants/{_tenantId}/links/");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateLink_AutoGeneratedSlugs_AreUniqueWithinDomain()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        var slugs = new HashSet<string>();
        for (int i = 0; i < 10; i++)
        {
            claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
                _tenantId.ToString(), Roles.Member));
            var response = await client.PostAsJsonAsync(
                $"/api/v1/tenants/{_tenantId}/links",
                new CreateLinkRequest
                {
                    DomainId = domainId,
                    DestinationUrl = $"https://example.com/{i}"
                });
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            var body = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
            slugs.Add(body!.Slug).Should().BeTrue("auto-generated slug should be unique");
        }

        slugs.Should().HaveCount(10);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateLink_AutoGeneratedSlug_LengthIsBetween6And8()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links",
            new CreateLinkRequest
            {
                DomainId = domainId,
                DestinationUrl = "https://example.com"
            });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body!.Slug.Length.Should().BeGreaterThanOrEqualTo(6);
        body.Slug.Length.Should().BeLessThanOrEqualTo(8);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateLink_With301RedirectType_ReturnsCorrectType()
    {
        var (client, claimsProvider) = _factory.CreateAuthenticatedClient(
            _tenantId.ToString(), Roles.Admin);
        var (domainId, _) = await CreateVerifiedDomainAsync(client, claimsProvider);

        claimsProvider.SetClaims(TestClaimsProvider.CreatePrincipal(
            _tenantId.ToString(), Roles.Member));
        var request = new CreateLinkRequest
        {
            DomainId = domainId,
            DestinationUrl = "https://example.com",
            RedirectType = "301"
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/tenants/{_tenantId}/links", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
        body!.RedirectType.Should().Be("301");
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
