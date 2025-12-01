namespace Common.ErrorHandling;

/// <summary>
/// Thrown when a URL is flagged as malicious by the URL safety scanner.
/// Maps to HTTP 422 Unprocessable Entity — the request is well-formed but
/// the destination URL is blocked for security reasons.
/// </summary>
public sealed class UrlBlockedException : DomainException
{
    public UrlBlockedException(string reason)
        : base(422, "URL Blocked", $"The destination URL has been blocked: {reason}")
    {
    }
}
