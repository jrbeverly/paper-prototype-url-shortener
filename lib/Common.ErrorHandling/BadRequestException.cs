namespace Common.ErrorHandling;

public sealed class BadRequestException : DomainException
{
    public BadRequestException(string message)
        : base(400, "Bad Request", message) { }
}
