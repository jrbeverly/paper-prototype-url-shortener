namespace ControlPlane.Api.Authentication;

public sealed record CurrentUser
{
    public string? UserId { get; init; }
    public string? TenantId { get; init; }
    public string Role { get; init; } = "anonymous";
    public string AuthScheme { get; init; } = "none";
    public List<string> Permissions { get; init; } = [];

    public bool IsAuthenticated => !string.IsNullOrEmpty(UserId)
        && AuthScheme != "none";
}
