namespace Common.ErrorHandling;

public sealed class TooManyRequestsException : DomainException
{
    public int RetryAfterSeconds { get; }

    public TooManyRequestsException(int retryAfterSeconds)
        : base(429, "Too Many Requests", $"Rate limit exceeded. Retry after {retryAfterSeconds} seconds.")
    {
        RetryAfterSeconds = retryAfterSeconds;
    }
}
