using System.Net;
using DnsClient;
using DnsClient.Protocol;

namespace ControlPlane.Api.Services;

/// <summary>
/// Checks whether a hostname's CNAME record is visible from multiple well-known public DNS
/// resolvers (Google, Cloudflare, OpenDNS) to assess global propagation status.
/// Each resolver is queried in parallel; individual timeouts do not abort the others.
/// </summary>
public sealed class DnsPropagationCheck : IDnsPropagationCheck
{
    private static readonly (string Name, string Address)[] Resolvers =
    [
        ("Google Public DNS", "8.8.8.8"),
        ("Cloudflare DNS",   "1.1.1.1"),
        ("OpenDNS",          "208.67.222.222"),
    ];

    private readonly ILogger<DnsPropagationCheck> _logger;

    public DnsPropagationCheck(ILogger<DnsPropagationCheck> logger)
    {
        _logger = logger;
    }

    public async Task<PropagationCheckResult> CheckAsync(
        string hostname,
        string expectedCnameTarget,
        CancellationToken ct = default)
    {
        var tasks = Resolvers
            .Select(r => QueryResolverAsync(hostname, expectedCnameTarget, r.Name, r.Address, ct))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        var passed = results.Count(r => r.Resolved);
        return new PropagationCheckResult
        {
            AllResolved = passed == results.Length,
            PassedCount = passed,
            TotalCount = results.Length,
            Results = results
        };
    }

    private async Task<ResolverResult> QueryResolverAsync(
        string hostname,
        string expectedCnameTarget,
        string resolverName,
        string resolverAddress,
        CancellationToken ct)
    {
        try
        {
            var options = new LookupClientOptions(IPAddress.Parse(resolverAddress))
            {
                Timeout = TimeSpan.FromSeconds(5),
                Retries = 1
            };
            var client = new LookupClient(options);
            var response = await client.QueryAsync(hostname, QueryType.CNAME, QueryClass.IN, ct);

            var record = response.Answers.OfType<CNameRecord>().FirstOrDefault();
            if (record is null)
            {
                return new ResolverResult
                {
                    ResolverName = resolverName,
                    ResolverAddress = resolverAddress,
                    Resolved = false,
                    Error = "No CNAME record found."
                };
            }

            var actual = record.CanonicalName.Value.TrimEnd('.');
            var matches = actual.Equals(
                expectedCnameTarget.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);

            return new ResolverResult
            {
                ResolverName = resolverName,
                ResolverAddress = resolverAddress,
                Resolved = matches,
                ResolvedTarget = actual,
                Error = matches ? null : $"Expected '{expectedCnameTarget}', got '{actual}'"
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Propagation query failed: resolver={Resolver} address={Address} hostname={Hostname}",
                resolverName, resolverAddress, hostname);

            return new ResolverResult
            {
                ResolverName = resolverName,
                ResolverAddress = resolverAddress,
                Resolved = false,
                Error = ex.Message
            };
        }
    }
}
