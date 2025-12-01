namespace Common.ErrorHandling;

public abstract class DomainException : Exception
{
    public int StatusCode { get; }
    public string Title { get; }
    public string ErrorType { get; }

    /// <summary>Optional upgrade URL included in limit-exceeded responses to prompt users to upgrade their plan.</summary>
    public string? UpgradeUrl { get; protected init; }

    protected DomainException(int statusCode, string title, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Title = title;
        ErrorType = $"https://api.short.io/errors/{title.ToLowerInvariant().Replace(" ", "-")}";
    }
}
