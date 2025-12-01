namespace Common.ErrorHandling;

public sealed class DomainVerificationException : DomainException
{
    public DomainVerificationException(string domain)
        : base(422, "Domain Not Verified", $"The domain '{domain}' has not been verified. Please complete domain verification before creating links.") { }
}
