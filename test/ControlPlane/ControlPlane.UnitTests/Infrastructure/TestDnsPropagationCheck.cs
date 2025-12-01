using ControlPlane.Api.Services;

namespace ControlPlane.UnitTests.Infrastructure;

public sealed class TestDnsPropagationCheck : IDnsPropagationCheck
{
    private PropagationCheckResult _result = AllPassed();

    public void SetResult(PropagationCheckResult result) => _result = result;

    public Task<PropagationCheckResult> CheckAsync(
        string hostname, string expectedCnameTarget, CancellationToken ct = default)
        => Task.FromResult(_result);

    public static PropagationCheckResult AllPassed(string target = "cf.example.com") => new()
    {
        AllResolved = true,
        PassedCount = 3,
        TotalCount = 3,
        Results =
        [
            new ResolverResult { ResolverName = "Google Public DNS", ResolverAddress = "8.8.8.8",          Resolved = true, ResolvedTarget = target },
            new ResolverResult { ResolverName = "Cloudflare DNS",    ResolverAddress = "1.1.1.1",          Resolved = true, ResolvedTarget = target },
            new ResolverResult { ResolverName = "OpenDNS",           ResolverAddress = "208.67.222.222",   Resolved = true, ResolvedTarget = target },
        ]
    };

    public static PropagationCheckResult AllFailed() => new()
    {
        AllResolved = false,
        PassedCount = 0,
        TotalCount = 3,
        Results =
        [
            new ResolverResult { ResolverName = "Google Public DNS", ResolverAddress = "8.8.8.8",          Resolved = false, Error = "No CNAME record found." },
            new ResolverResult { ResolverName = "Cloudflare DNS",    ResolverAddress = "1.1.1.1",          Resolved = false, Error = "No CNAME record found." },
            new ResolverResult { ResolverName = "OpenDNS",           ResolverAddress = "208.67.222.222",   Resolved = false, Error = "No CNAME record found." },
        ]
    };

    public static PropagationCheckResult Partial(string target = "cf.example.com") => new()
    {
        AllResolved = false,
        PassedCount = 1,
        TotalCount = 3,
        Results =
        [
            new ResolverResult { ResolverName = "Google Public DNS", ResolverAddress = "8.8.8.8",          Resolved = true,  ResolvedTarget = target },
            new ResolverResult { ResolverName = "Cloudflare DNS",    ResolverAddress = "1.1.1.1",          Resolved = false, Error = "No CNAME record found." },
            new ResolverResult { ResolverName = "OpenDNS",           ResolverAddress = "208.67.222.222",   Resolved = false, Error = "No CNAME record found." },
        ]
    };
}
