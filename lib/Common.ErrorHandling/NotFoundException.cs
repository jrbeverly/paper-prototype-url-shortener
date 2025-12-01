namespace Common.ErrorHandling;

public sealed class NotFoundException : DomainException
{
    public NotFoundException(string entityType, string id)
        : base(404, "Not Found", $"{entityType} with ID '{id}' was not found.") { }

    public NotFoundException(string message)
        : base(404, "Not Found", message) { }
}
