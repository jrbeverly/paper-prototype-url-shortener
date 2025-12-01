namespace ControlPlane.Api.Extensions;

/// <summary>
/// Marker interface for auto-discovered endpoint groups.
/// Implementations are found via reflection and registered at startup.
/// The <see cref="IEndpointRouteBuilder"/> parameter allows mounting under versioned route groups.
/// </summary>
public interface IEndpointGroup
{
    static abstract void Map(IEndpointRouteBuilder routes);
}
