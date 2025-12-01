namespace ControlPlane.Api.Authorization;

public static class Roles
{
    public const string Owner = "owner";
    public const string Admin = "admin";
    public const string Member = "member";
    public const string Viewer = "viewer";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Owner, Admin, Member, Viewer
    };

    public static IReadOnlySet<string> GetPermissions(string role) => role.ToLowerInvariant() switch
    {
        Owner => Permissions.AllSet,
        Admin => Permissions.AllSet,
        Member => Permissions.MemberSet,
        Viewer => Permissions.ViewerSet,
        _ => new HashSet<string>()
    };
}
