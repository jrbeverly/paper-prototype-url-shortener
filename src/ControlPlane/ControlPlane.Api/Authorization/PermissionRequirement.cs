using Microsoft.AspNetCore.Authorization;

namespace ControlPlane.Api.Authorization;

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }
    public PermissionRequirement(string permission) => Permission = permission;
}
