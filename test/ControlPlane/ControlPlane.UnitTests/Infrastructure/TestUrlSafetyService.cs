using ControlPlane.Api.Services;

namespace ControlPlane.UnitTests.Infrastructure;

/// <summary>
/// Controllable URL safety service test double.
/// All scans pass (Safe) by default; set <see cref="Verdict"/> to simulate different outcomes.
/// </summary>
internal sealed class TestUrlSafetyService : IUrlSafetyService
{
    public UrlSafetyVerdict Verdict { get; set; } = UrlSafetyVerdict.Safe;
    public string Reason { get; set; } = "test";
    public string Source { get; set; } = "test-double";

    public Task<UrlSafetyResult> ScanAsync(string url, CancellationToken ct = default)
    {
        return Task.FromResult(new UrlSafetyResult
        {
            Verdict = Verdict,
            Reason = Reason,
            Source = Source
        });
    }
}
