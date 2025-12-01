namespace Common.ErrorHandling;

public sealed class ForbiddenException : DomainException
{
    public ForbiddenException(string message)
        : base(403, "Forbidden", message) { }
}
