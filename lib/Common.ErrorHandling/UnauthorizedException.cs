namespace Common.ErrorHandling;

public sealed class UnauthorizedException : DomainException
{
    public UnauthorizedException(string message)
        : base(401, "Unauthorized", message) { }
}
