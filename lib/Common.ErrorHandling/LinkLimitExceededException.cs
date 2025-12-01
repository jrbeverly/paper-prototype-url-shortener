namespace Common.ErrorHandling;

public sealed class LinkLimitExceededException : DomainException
{
    public LinkLimitExceededException(int planLimit)
        : base(402, "Link Limit Exceeded", $"You have reached your plan limit of {planLimit} links per domain. Upgrade your plan to create more links.")
    {
        UpgradeUrl = "https://short.io/pricing";
    }
}
