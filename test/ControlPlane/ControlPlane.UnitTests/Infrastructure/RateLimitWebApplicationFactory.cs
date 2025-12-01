using ControlPlane.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ControlPlane.UnitTests.Infrastructure;

/// <summary>
/// Extends <see cref="UnitTestWebApplicationFactory"/> by replacing <see cref="ILinkRateLimiter"/>
/// with a controllable <see cref="TestLinkRateLimiter"/> for rate-limit endpoint tests.
/// </summary>
internal sealed class RateLimitWebApplicationFactory : UnitTestWebApplicationFactory
{
    public TestLinkRateLimiter TestRateLimiter { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ILinkRateLimiter>();
            services.AddSingleton<ILinkRateLimiter>(TestRateLimiter);
        });
    }
}
