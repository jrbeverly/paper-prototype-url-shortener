using System.Security.Claims;

namespace ControlPlane.Api.Authentication;

public sealed class CurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public CurrentUser GetCurrentUser()
    {
        var user = _httpContextAccessor.HttpContext?.User;

        if (user is not { Identity.IsAuthenticated: true })
            return new CurrentUser();

        return new CurrentUser
        {
            UserId = user.GetUserId(),
            TenantId = user.GetTenantId(),
            Role = user.GetRole() ?? "authenticated",
            AuthScheme = user.GetAuthScheme() ?? user.Identity?.AuthenticationType ?? "unknown",
            Permissions = user.GetPermissions().ToList()
        };
    }
}
