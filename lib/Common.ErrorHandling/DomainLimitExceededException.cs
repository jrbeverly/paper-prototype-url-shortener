namespace Common.ErrorHandling;

public sealed class DomainLimitExceededException : DomainException
{
    public DomainLimitExceededException(int planLimit)
        : base(402, "Domain Limit Exceeded", $"You have reached your plan limit of {planLimit} domains. Upgrade your plan to add more domains.")
    {
        UpgradeUrl = "https://short.io/pricing";
    }
}
