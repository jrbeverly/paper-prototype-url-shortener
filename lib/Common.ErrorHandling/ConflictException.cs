namespace Common.ErrorHandling;

public sealed class ConflictException : DomainException
{
    public ConflictException(string message)
        : base(409, "Conflict", message) { }
}
